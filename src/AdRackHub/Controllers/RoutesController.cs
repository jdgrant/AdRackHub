using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using DistributionRoute = AdRackHub.Models.Route;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.RoutesStops)]
public class RoutesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly StopImportService _stopImportService;
    private readonly RouteSheetSyncService _routeSheetSyncService;
    private readonly StopVisitService _visitService;

    public RoutesController(
        ApplicationDbContext context,
        StopImportService stopImportService,
        RouteSheetSyncService routeSheetSyncService,
        StopVisitService visitService)
    {
        _context = context;
        _stopImportService = stopImportService;
        _routeSheetSyncService = routeSheetSyncService;
        _visitService = visitService;
    }

    public async Task<IActionResult> Index(RouteStatus? status)
    {
        var query = _context.Routes
            .Include(r => r.Stops)
            .Include(r => r.CustomerRoutes)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        ViewBag.Status = status;
        return View(await query.OrderBy(r => r.RouteName).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var route = await _context.Routes
            .Include(r => r.Stops)
            .Include(r => r.CustomerRoutes)
                .ThenInclude(cr => cr.Customer)
            .Include(r => r.CustomerRoutes)
                .ThenInclude(cr => cr.CustomerRouteStops)
                    .ThenInclude(crs => crs.Stop)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (route == null) return NotFound();

        route.Stops = route.Stops
            .Where(s => s.Status == StopStatus.Active)
            .OrderBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .ToList();

        var allStopsSubscriptions = route.CustomerRoutes
            .Count(cr => cr.Status == CustomerRouteStatus.Active && cr.AllStops);

        ViewBag.BrochureCounts = route.Stops.ToDictionary(
            stop => stop.Id,
            stop => allStopsSubscriptions + route.CustomerRoutes.Count(cr =>
                cr.Status == CustomerRouteStatus.Active &&
                !cr.AllStops &&
                cr.CustomerRouteStops.Any(crs => crs.StopId == stop.Id)));

        ViewBag.GoogleSheetsConfigured = _routeSheetSyncService.IsConfigured;
        ViewBag.RouteSheetSlug = RouteSheetMap.GetSlugForRouteName(route.RouteName);

        return View(route);
    }

    public async Task<IActionResult> Map(int? id)
    {
        if (id == null) return NotFound();

        var route = await _context.Routes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (route == null) return NotFound();

        var stops = route.Stops
            .Where(s => s.Status == StopStatus.Active)
            .OrderBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .ToList();

        var model = new RouteMapViewModel
        {
            RouteId = route.Id,
            RouteName = route.RouteName,
            Stops = BuildMapStops(stops)
        };

        return View(model);
    }

    private static List<RouteMapStopItem> BuildMapStops(IEnumerable<Stop> stops) =>
        stops
            .Where(StopAddressHelper.HasMappableLocation)
            .Select(s => new RouteMapStopItem
            {
                Id = s.Id,
                StepNumber = s.StepNumber,
                Name = s.StopName,
                Address = StopAddressHelper.FormatFullAddress(s),
                Query = StopAddressHelper.GetGeocodingQuery(s)
            })
            .ToList();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogAllStops(int id, string? notes)
    {
        var route = await _context.Routes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (route == null) return NotFound();

        var stops = route.Stops.Where(s => s.Status == StopStatus.Active).ToList();
        if (stops.Count == 0)
        {
            TempData["Error"] = "No active stops to log on this route.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var count = await _visitService.LogVisitsForStopsAsync(stops, User, notes);
        TempData["Message"] = $"Logged visits for {count} stops on {route.RouteName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncFromSheets(int id)
    {
        var route = await _context.Routes.FindAsync(id);
        if (route == null) return NotFound();

        var slug = RouteSheetMap.GetSlugForRouteName(route.RouteName);
        if (slug == null)
        {
            TempData["Error"] = $"No Google Sheets tab mapping exists for {route.RouteName}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var result = await _routeSheetSyncService.SyncRouteAsync(slug);
            TempData["Message"] = $"Synced {result.Imported} stops from Google Sheets.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    public IActionResult Import(int id)
    {
        ViewBag.RouteId = id;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(int id, IFormFile csvFile, bool replaceExisting = true)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            ModelState.AddModelError("", "Choose a CSV file to import.");
            ViewBag.RouteId = id;
            return View();
        }

        try
        {
            await using var stream = csvFile.OpenReadStream();
            var result = await _stopImportService.ImportAsync(id, stream, replaceExisting);
            TempData["Message"] = $"Imported {result.Imported} stops.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.RouteId = id;
            return View();
        }
    }

    public IActionResult Create() => View(new DistributionRoute
    {
        Status = RouteStatus.Active,
        BillingFrequency = BillingFrequency.Quarterly
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DistributionRoute route)
    {
        if (ModelState.IsValid)
        {
            _context.Add(route);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = route.Id });
        }
        return View(route);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var route = await _context.Routes.FindAsync(id);
        if (route == null) return NotFound();
        return View(route);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, DistributionRoute route)
    {
        if (id != route.Id) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(route);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Routes.AnyAsync(r => r.Id == id))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Details), new { id = route.Id });
        }
        return View(route);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var route = await _context.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (route == null) return NotFound();
        return View(route);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var route = await _context.Routes.FindAsync(id);
        if (route != null)
        {
            _context.Routes.Remove(route);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppRoles.Customers)]
    public async Task<IActionResult> AssignCustomer(int id)
    {
        var route = await _context.Routes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (route == null)
            return NotFound();

        var stops = route.Stops
            .Where(s => s.Status == StopStatus.Active)
            .OrderBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .ToList();

        var vm = new RouteAssignCustomerViewModel
        {
            RouteId = route.Id,
            RouteName = route.RouteName,
            RatePerMonth = SubscribedMonths.DefaultRatePerMonth(route),
            AvailableStops = stops.Select(s => new StopSelectionItem
            {
                StopId = s.Id,
                StopName = s.StopName
            }).ToList()
        };

        await PopulateAssignableCustomersAsync(route.Id);
        return View(vm);
    }

    [Authorize(Policy = AppRoles.Customers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCustomer(int id, RouteAssignCustomerViewModel vm)
    {
        if (id != vm.RouteId)
            return NotFound();

        var route = await _context.Routes.FindAsync(id);
        if (route == null)
            return NotFound();

        if (!vm.AllStops && !vm.SelectedStopIds.Any())
            ModelState.AddModelError("", "Select at least one stop, or choose All Stops.");

        if (!vm.SelectedMonthNumbers.Any())
            ModelState.AddModelError("", "Select at least one subscribed month.");

        if (await _context.CustomerRoutes.AnyAsync(cr => cr.CustomerId == vm.CustomerId && cr.RouteId == id))
            ModelState.AddModelError("", "This customer is already assigned to this route.");

        if (!ModelState.IsValid)
        {
            vm.RouteName = route.RouteName;
            vm.AvailableStops = await GetRouteStopsAsync(id);
            await PopulateAssignableCustomersAsync(id, vm.CustomerId);
            return View(vm);
        }

        var customerRoute = new CustomerRoute
        {
            CustomerId = vm.CustomerId,
            RouteId = id,
            AllStops = vm.AllStops,
            Status = vm.Status,
            BillingTerm = vm.BillingTerm,
            RatePerMonth = vm.RatePerMonth,
            SubscribedMonthMask = SubscribedMonths.BuildMask(vm.SelectedMonthNumbers)
        };

        _context.CustomerRoutes.Add(customerRoute);
        await _context.SaveChangesAsync();
        await SyncCustomerRouteStopsAsync(customerRoute.Id, vm.AllStops, vm.SelectedStopIds);

        var customer = await _context.Customers.FindAsync(vm.CustomerId);
        TempData["Message"] = $"{customer?.CustomerName} added to {route.RouteName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<List<StopSelectionItem>> GetRouteStopsAsync(int routeId)
    {
        return await _context.Stops
            .Where(s => s.RouteId == routeId && s.Status == StopStatus.Active)
            .OrderBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .Select(s => new StopSelectionItem
            {
                StopId = s.Id,
                StopName = s.StopName
            })
            .ToListAsync();
    }

    private async Task PopulateAssignableCustomersAsync(int routeId, int? selectedCustomerId = null)
    {
        var assignedIds = await _context.CustomerRoutes
            .Where(cr => cr.RouteId == routeId)
            .Select(cr => cr.CustomerId)
            .ToListAsync();

        var customers = await _context.Customers
            .Where(c => c.Status == CustomerStatus.Active
                && c.Type == CustomerType.Customer
                && !assignedIds.Contains(c.Id))
            .OrderBy(c => c.CustomerName)
            .ToListAsync();

        ViewBag.CustomerId = new SelectList(customers, "Id", "CustomerName", selectedCustomerId);
    }

    private async Task SyncCustomerRouteStopsAsync(int customerRouteId, bool allStops, List<int> selectedStopIds)
    {
        var existing = await _context.CustomerRouteStops
            .Where(crs => crs.CustomerRouteId == customerRouteId)
            .ToListAsync();

        _context.CustomerRouteStops.RemoveRange(existing);

        if (!allStops)
        {
            foreach (var stopId in selectedStopIds.Distinct())
            {
                _context.CustomerRouteStops.Add(new CustomerRouteStop
                {
                    CustomerRouteId = customerRouteId,
                    StopId = stopId
                });
            }
        }

        await _context.SaveChangesAsync();
    }
}
