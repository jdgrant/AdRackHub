using System.Text.RegularExpressions;
using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly BrochureInventoryReportService _inventoryReport;
    private readonly UserManager<ApplicationUser> _userManager;

    public RoutesController(
        ApplicationDbContext context,
        StopImportService stopImportService,
        RouteSheetSyncService routeSheetSyncService,
        StopVisitService visitService,
        BrochureInventoryReportService inventoryReport,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _stopImportService = stopImportService;
        _routeSheetSyncService = routeSheetSyncService;
        _visitService = visitService;
        _inventoryReport = inventoryReport;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(RouteStatus? status, RouteProduct? product)
    {
        var query = _context.Routes
            .Include(r => r.Stops)
            .Include(r => r.CustomerRoutes)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (product == RouteProduct.RestArea)
            query = query.Where(r => r.RouteName.Contains("Rest Area"));
        else if (product == RouteProduct.Exits)
            query = query.Where(r => !r.RouteName.Contains("Rest Area"));

        ViewBag.Status = status;
        ViewBag.Product = product;
        return View(await query.OrderBy(r => r.RouteName).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Estimate(
        List<int>? routeIds,
        int brochuresPerClient = 100,
        int truckCount = 4,
        int? vanCapacity = null)
    {
        if (brochuresPerClient < 1)
            brochuresPerClient = 100;
        if (truckCount < 1)
            truckCount = 4;
        if (truckCount > 20)
            truckCount = 20;
        if (vanCapacity is <= 0)
            vanCapacity = null;

        var selected = (routeIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
        var options = await LoadRouteOptionsAsync(selected);
        var page = new RouteLoadEstimatePageViewModel
        {
            SelectedRouteIds = selected,
            BrochuresPerClient = brochuresPerClient,
            TruckCount = truckCount,
            VanCapacity = vanCapacity,
            AvailableRoutes = options
        };
        if (selected.Count > 0)
            page.Result = await BuildRouteLoadEstimateAsync(selected, brochuresPerClient, truckCount, vanCapacity);
        return View(page);
    }

    [HttpGet]
    public async Task<IActionResult> Inventory(string? warehouse)
    {
        var report = await _inventoryReport.BuildAsync();
        var countedBy = await GetCurrentUserLabelAsync();
        return View(BuildInventoryPage(report, countedBy, warehouse));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(ValueCountLimit = 16384)]
    public async Task<IActionResult> Inventory(WarehouseInvoicePageViewModel model)
    {
        return await SaveWarehouseInventoryAsync(model, rebuild => View(rebuild));
    }

    [HttpGet]
    public IActionResult Invoice(string? warehouse) =>
        RedirectToAction(nameof(Inventory), new { warehouse });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(ValueCountLimit = 16384)]
    public async Task<IActionResult> Invoice(WarehouseInvoicePageViewModel model)
    {
        return await SaveWarehouseInventoryAsync(model, _ => RedirectToAction(nameof(Inventory), new { warehouse = model.ActiveWarehouse }));
    }

    private async Task<IActionResult> SaveWarehouseInventoryAsync(
        WarehouseInvoicePageViewModel model,
        Func<WarehouseInvoicePageViewModel, IActionResult> onEmpty)
    {
        var rows = model.Rows ?? new List<WarehouseInvoiceRowForm>();
        var date = model.InventoryDate == default
            ? DateOnly.FromDateTime(DateTime.Today)
            : model.InventoryDate;
        var countedBy = string.IsNullOrWhiteSpace(model.CountedBy)
            ? await GetCurrentUserLabelAsync()
            : model.CountedBy.Trim();

        var toLog = new List<(WarehouseInvoiceRowForm Row, int Quantity)>();
        foreach (var row in rows)
        {
            if (row.CustomerId <= 0)
                continue;
            var quantity = ResolveQuantity(row);
            if (!quantity.HasValue)
                continue;
            toLog.Add((row, quantity.Value));
        }

        if (toLog.Count == 0)
        {
            TempData["Error"] = "Enter a total (or cases and per case) on at least one brochure.";
            var emptyReport = await _inventoryReport.BuildAsync();
            var page = BuildInventoryPage(emptyReport, countedBy, model.ActiveWarehouse);
            page.InventoryDate = date;
            page.CountedBy = countedBy;
            CopyEnteredCounts(page.Rows, rows);
            return onEmpty(page);
        }

        var customerIds = toLog.Select(item => item.Row.CustomerId).Distinct().ToList();
        var customers = await _context.Customers
            .Include(c => c.WarehouseLocations)
            .Where(c => customerIds.Contains(c.Id) && c.Type == CustomerType.Customer)
            .ToDictionaryAsync(c => c.Id);

        var logged = 0;
        foreach (var (row, quantity) in toLog)
        {
            if (!customers.TryGetValue(row.CustomerId, out var customer))
                continue;

            var rack = WarehouseLocation.NullIfEmpty(row.Rack);
            var bin = WarehouseLocation.NullIfEmpty(row.Bin);
            var warehouse = WarehouseLocation.Parse(row.Warehouse);
            var shelf = WarehouseLocation.ParseShelf(row.Shelf);
            warehouse = WarehouseLocation.DefaultKy(warehouse, rack, bin);
            if (warehouse != null || rack != null || bin != null || shelf != null)
                WarehouseLocation.Upsert(customer, warehouse, rack, bin, shelf);

            var notes = InventoryNote(row);
            _context.CustomerBrochureInventories.Add(new CustomerBrochureInventory
            {
                CustomerId = customer.Id,
                Quantity = quantity,
                InventoryDate = date,
                Warehouse = warehouse,
                Rack = rack,
                Bin = bin,
                Shelf = shelf,
                Notes = notes,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = countedBy
            });
            logged++;
        }

        await _context.SaveChangesAsync();
        TempData["Message"] = logged == 1
            ? "Saved 1 inventory log."
            : $"Saved {logged} inventory logs.";
        return RedirectToAction(nameof(Inventory), new { warehouse = model.ActiveWarehouse });
    }

    private static WarehouseInvoicePageViewModel BuildInventoryPage(
        BrochureInventoryReport report,
        string countedBy,
        string? warehouse)
    {
        var active = string.Equals(warehouse, "O", StringComparison.OrdinalIgnoreCase) ? "O"
            : BrochureInventoryReportService.IsUnassignedCode(warehouse) ? "U"
            : "K";
        var page = new WarehouseInvoicePageViewModel
        {
            InventoryDate = report.AsOf,
            CountedBy = countedBy,
            ActiveWarehouse = active
        };

        foreach (var row in BrochureInventoryReportService.RowsForWarehouse(report, active))
        {
            page.Rows.Add(new WarehouseInvoiceRowForm
            {
                CustomerId = row.CustomerId,
                BrochureName = row.BrochureName,
                BrochureCode = row.BrochureCode,
                Location = row.Location,
                ContractLabel = row.ContractLabel,
                Warehouse = row.Warehouse?.ToString(),
                Rack = row.Rack,
                Bin = row.Bin,
                Shelf = row.Shelf?.ToString(),
                LastQuantity = row.Quantity,
                LastReceivedQuantity = row.ReceivedQuantity,
                LastCountedDate = row.InventoryDate,
                PerCase = row.PerCase
            });
        }

        return page;
    }

    private static void CopyEnteredCounts(
        IReadOnlyList<WarehouseInvoiceRowForm> pageRows,
        IReadOnlyList<WarehouseInvoiceRowForm> posted)
    {
        var byKey = posted
            .GroupBy(r => $"{r.CustomerId}|{r.Warehouse}|{r.Rack}|{r.Bin}|{r.Shelf}")
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var row in pageRows)
        {
            var key = $"{row.CustomerId}|{row.Warehouse}|{row.Rack}|{row.Bin}|{row.Shelf}";
            if (!byKey.TryGetValue(key, out var entered))
                continue;
            row.Cases = entered.Cases;
            row.PerCase = entered.PerCase;
            row.Total = entered.Total;
        }
    }

    private static int? ResolveQuantity(WarehouseInvoiceRowForm row)
    {
        if (row.Total.HasValue)
            return row.Total.Value;
        if (row.Cases.HasValue && row.PerCase.HasValue)
            return row.Cases.Value * row.PerCase.Value;
        return null;
    }

    private static string? InventoryNote(WarehouseInvoiceRowForm row)
    {
        if (row.Cases.HasValue && row.PerCase.HasValue)
            return $"{row.Cases.Value} cases × {row.PerCase.Value}";
        return null;
    }

    private async Task<string> GetCurrentUserLabelAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            return user.DisplayName;
        if (!string.IsNullOrWhiteSpace(user?.UserName))
            return user.UserName;
        return User.Identity?.Name ?? "User";
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
                Query = StopAddressHelper.GetGeocodingQuery(s),
                Latitude = s.Latitude,
                Longitude = s.Longitude
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
            BillingTerm = route.BillingFrequency,
            BillingMonthCount = AnnualBillingHelper.MonthsInTerm(route.BillingFrequency),
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
            RatePerMonth = vm.RatePerMonth,
            SubscribedMonthMask = SubscribedMonths.BuildMask(vm.SelectedMonthNumbers)
        };
        AnnualBillingHelper.ApplyBillingMonths(customerRoute, vm.BillingMonthCount);

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

    private async Task<List<RouteLoadRouteOption>> LoadRouteOptionsAsync(IReadOnlyCollection<int> selectedIds)
    {
        var routes = await _context.Routes
            .AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active)
            .Select(r => new
            {
                r.Id,
                r.RouteName,
                ActiveCustomers = r.CustomerRoutes.Count(cr =>
                    cr.Status == CustomerRouteStatus.Active
                    && cr.Customer.Type == CustomerType.Customer
                    && cr.Customer.Status == CustomerStatus.Active)
            })
            .OrderBy(r => r.RouteName)
            .ToListAsync();

        return routes.Select(r => new RouteLoadRouteOption
        {
            Id = r.Id,
            Name = r.RouteName,
            Product = RouteProductHelper.FromRouteName(r.RouteName),
            ActiveCustomers = r.ActiveCustomers,
            Selected = selectedIds.Contains(r.Id)
        }).ToList();
    }

    private async Task<RouteLoadEstimateResult> BuildRouteLoadEstimateAsync(
        IReadOnlyList<int> selectedIds,
        int brochuresPerClient,
        int truckCount,
        int? vanCapacity)
    {
        var rows = await _context.CustomerRoutes
            .AsNoTracking()
            .Where(cr => selectedIds.Contains(cr.RouteId)
                && cr.Status == CustomerRouteStatus.Active
                && cr.Customer.Type == CustomerType.Customer
                && cr.Customer.Status == CustomerStatus.Active)
            .Select(cr => new
            {
                cr.RouteId,
                RouteName = cr.Route.RouteName,
                cr.CustomerId,
                CustomerName = cr.Customer.CustomerName,
                cr.Customer.BrochureCode,
                cr.Customer.Warehouse,
                cr.Customer.WarehouseRack,
                cr.Customer.WarehouseBin,
                cr.Customer.WarehouseShelf,
                cr.AllStops,
                AssignedStopIds = cr.CustomerRouteStops.Select(s => s.StopId).ToList(),
                ActiveStops = cr.Route.Stops
                    .Where(s => s.Status == StopStatus.Active)
                    .Select(s => new { s.Id, s.StopName })
                    .ToList()
            })
            .ToListAsync();

        var customers = rows
            .GroupBy(r => r.CustomerId)
            .Select(g =>
            {
                var first = g.First();
                var routeIds = g.Select(x => x.RouteId).Distinct().OrderBy(id => id).ToList();
                var routeNames = g.Select(x => x.RouteName).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                return new RouteLoadCustomerRow
                {
                    CustomerId = g.Key,
                    CustomerName = first.CustomerName,
                    BrochureCode = first.BrochureCode,
                    Warehouse = WarehouseLocation.Format(
                        first.Warehouse, first.WarehouseRack, first.WarehouseBin, first.WarehouseShelf),
                    Brochures = brochuresPerClient,
                    RouteIds = routeIds,
                    RouteNames = routeNames
                };
            })
            .OrderBy(c => c.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var routeNamesById = rows
            .GroupBy(r => r.RouteId)
            .ToDictionary(g => g.Key, g => g.First().RouteName);
        foreach (var id in selectedIds)
            routeNamesById.TryAdd(id, optionsNameFallback(id));

        var customersByRoute = rows
            .GroupBy(r => r.RouteId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CustomerId).ToHashSet());
        var routeCountByCustomer = customers.ToDictionary(c => c.CustomerId, c => c.RouteNames.Count);
        var sharedCustomerIds = customers.Where(c => c.RouteNames.Count > 1).Select(c => c.CustomerId).ToHashSet();
        var stopKeysByRoute = rows
            .GroupBy(r => r.RouteId)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(r =>
                        (r.AllStops
                            ? r.ActiveStops
                            : r.ActiveStops.Where(s => r.AssignedStopIds.Contains(s.Id)))
                        .Select(s => StopKey(s.StopName, s.Id)))
                    .ToHashSet());
        var routesWithKey = stopKeysByRoute
            .SelectMany(pair => pair.Value.Select(key => (pair.Key, key)))
            .ToList();
        var routeCountByStopKey = routesWithKey
            .GroupBy(item => item.key)
            .ToDictionary(g => g.Key, g => g.Select(item => item.Key).Distinct().Count());

        var routeSummaries = selectedIds
            .Select(id =>
            {
                customersByRoute.TryGetValue(id, out var ids);
                ids ??= new HashSet<int>();
                var shared = ids.Count(cid => routeCountByCustomer.GetValueOrDefault(cid) > 1);
                stopKeysByRoute.TryGetValue(id, out var keys);
                keys ??= new HashSet<string>();
                var uniqueStops = keys.Count(key => routeCountByStopKey.GetValueOrDefault(key) == 1);
                var remainderStops = keys.Count(key => routeCountByStopKey.GetValueOrDefault(key) > 1);
                return new RouteLoadRouteSummary
                {
                    RouteId = id,
                    RouteName = routeNamesById.GetValueOrDefault(id, $"Route {id}"),
                    Customers = ids.Count,
                    SharedCustomers = shared,
                    UniqueStops = uniqueStops,
                    RemainderStops = remainderStops,
                    SharedStops = uniqueStops + remainderStops,
                    ExclusiveCustomers = ids.Count - shared,
                    BrochuresIfAlone = ids.Count * brochuresPerClient
                };
            })
            .OrderBy(r => r.RouteName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var separateBrochures = routeSummaries.Sum(r => r.BrochuresIfAlone);
        var combinedBrochures = customers.Count * brochuresPerClient;
        var sharedCustomers = sharedCustomerIds.Count;
        var uniqueStops = routeCountByStopKey.Count(pair => pair.Value == 1);
        var remainderStops = routeCountByStopKey.Count(pair => pair.Value > 1);
        var sharedStops = uniqueStops + remainderStops;
        int? combinedVans = null;
        int? separateVans = null;
        double? fill = null;
        var combinedFits = true;
        if (vanCapacity is int cap and > 0)
        {
            combinedVans = combinedBrochures == 0 ? 0 : (int)Math.Ceiling(combinedBrochures / (double)cap);
            separateVans = routeSummaries.Sum(r =>
                r.BrochuresIfAlone == 0 ? 0 : (int)Math.Ceiling(r.BrochuresIfAlone / (double)cap));
            fill = cap == 0 ? 0 : Math.Round(100.0 * combinedBrochures / cap, 1);
            combinedFits = combinedBrochures <= cap;
        }

        var packs = PackTrucks(
            selectedIds,
            customersByRoute,
            stopKeysByRoute,
            routeNamesById,
            brochuresPerClient,
            truckCount,
            vanCapacity);
        var truckByRoute = packs
            .SelectMany(pack => pack.RouteIds.Select(id => (id, pack.VanNumber)))
            .ToDictionary(item => item.id, item => item.VanNumber);
        var splitCustomers = 0;
        var capturedShared = 0;
        foreach (var customer in customers)
        {
            var truckNumbers = customer.RouteIds
                .Where(id => truckByRoute.ContainsKey(id))
                .Select(id => truckByRoute[id])
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            customer.TruckNumbers = truckNumbers;
            if (truckNumbers.Count > 1)
                splitCustomers++;
            else if (customer.RouteIds.Count > 1)
                capturedShared++;
        }

        return new RouteLoadEstimateResult
        {
            DistinctCustomers = customers.Count,
            CombinedBrochures = combinedBrochures,
            SeparateBrochures = separateBrochures,
            SharedCustomers = sharedCustomers,
            UniqueStops = uniqueStops,
            RemainderStops = remainderStops,
            SharedStops = sharedStops,
            BrochureSavings = Math.Max(0, separateBrochures - combinedBrochures),
            CombinedVans = combinedVans,
            SeparateVans = separateVans,
            FillPercent = fill,
            CombinedFits = combinedFits,
            RequestedTrucks = truckCount,
            SplitCustomers = splitCustomers,
            CapturedSharedCustomers = capturedShared,
            TruckBrochures = packs.Sum(pack => pack.Brochures),
            Routes = routeSummaries,
            Customers = customers,
            SuggestedVans = packs
        };

        static string optionsNameFallback(int id) => $"Route {id}";
    }

    private static string StopKey(string? name, int id)
    {
        var key = (name ?? "").Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(key) ? $"#{id}" : key;
    }

    private static List<RouteLoadVanPack> PackTrucks(
        IReadOnlyList<int> selectedIds,
        Dictionary<int, HashSet<int>> customersByRoute,
        Dictionary<int, HashSet<string>> stopKeysByRoute,
        Dictionary<int, string> routeNamesById,
        int brochuresPerClient,
        int truckCount,
        int? vanCapacity)
    {
        if (selectedIds.Count == 0 || truckCount < 1)
            return new List<RouteLoadVanPack>();

        List<RouteCluster> clusters;
        if (selectedIds.Count <= truckCount)
        {
            clusters = selectedIds.Select(id => MakeCluster(id, customersByRoute)).ToList();
            while (TryMergeBestOverlap(clusters, customersByRoute, brochuresPerClient, vanCapacity))
            { }
        }
        else
        {
            var minRoutes = selectedIds.Count >= truckCount * 2 ? 2 : 1;
            clusters = SeedAndAssign(selectedIds, customersByRoute, routeNamesById, brochuresPerClient, vanCapacity, truckCount);
            EnsureMinRoutes(clusters, customersByRoute, routeNamesById, minRoutes);
            ImproveSharedAssignments(clusters, customersByRoute, routeNamesById, brochuresPerClient, vanCapacity, minRoutes);
        }

        return clusters
            .Where(cluster => cluster.RouteIds.Count > 0)
            .Select(cluster => BuildTruckPack(cluster, customersByRoute, stopKeysByRoute, routeNamesById, brochuresPerClient, vanCapacity))
            .OrderByDescending(pack => pack.SharedCustomers)
            .ThenByDescending(pack => pack.DistinctCustomers)
            .ThenBy(pack => pack.RouteNames.FirstOrDefault() ?? "", StringComparer.OrdinalIgnoreCase)
            .Select((pack, index) =>
            {
                pack.VanNumber = index + 1;
                return pack;
            })
            .ToList();
    }

    private static List<RouteCluster> SeedAndAssign(
        IReadOnlyList<int> selectedIds,
        Dictionary<int, HashSet<int>> customersByRoute,
        Dictionary<int, string> routeNamesById,
        int brochuresPerClient,
        int? vanCapacity,
        int truckCount)
    {
        var remaining = selectedIds
            .OrderByDescending(id => CustomerCount(id, customersByRoute))
            .ThenBy(id => id)
            .ToList();
        var seeds = new List<int> { remaining[0] };
        remaining.RemoveAt(0);
        while (seeds.Count < truckCount && remaining.Count > 0)
        {
            var bestId = remaining[0];
            var bestExclusive = int.MinValue;
            var bestSize = -1;
            foreach (var id in remaining)
            {
                var maxOverlap = 0;
                foreach (var seed in seeds)
                    maxOverlap = Math.Max(maxOverlap, CountOverlap(CustomersOf(id, customersByRoute), CustomersOf(seed, customersByRoute)));
                var size = CustomerCount(id, customersByRoute);
                var exclusive = size - maxOverlap;
                if (exclusive > bestExclusive || (exclusive == bestExclusive && size > bestSize))
                {
                    bestExclusive = exclusive;
                    bestSize = size;
                    bestId = id;
                }
            }
            seeds.Add(bestId);
            remaining.Remove(bestId);
        }

        var clusters = seeds.Select(id => MakeCluster(id, customersByRoute)).ToList();
        BindCorridorPartners(clusters, remaining, customersByRoute, routeNamesById, brochuresPerClient, vanCapacity);
        var maxRoutes = Math.Max(1, (int)Math.Ceiling(selectedIds.Count / (double)clusters.Count));
        while (remaining.Count > 0)
        {
            var bestRoute = -1;
            var bestCluster = -1;
            var bestOverlap = -1;
            var bestLoad = int.MaxValue;
            var preferUnderCap = clusters.Any(cluster => cluster.RouteIds.Count < maxRoutes);
            foreach (var id in remaining)
            {
                var cust = CustomersOf(id, customersByRoute);
                for (var i = 0; i < clusters.Count; i++)
                {
                    if (preferUnderCap && clusters[i].RouteIds.Count >= maxRoutes)
                        continue;
                    var overlap = CountOverlap(clusters[i].Customers, cust);
                    var load = clusters[i].Customers.Count + cust.Count - overlap;
                    if (!FitsCapacity(load, brochuresPerClient, vanCapacity))
                        continue;
                    if (overlap > bestOverlap || (overlap == bestOverlap && load < bestLoad))
                    {
                        bestOverlap = overlap;
                        bestLoad = load;
                        bestRoute = id;
                        bestCluster = i;
                    }
                }
            }

            if (bestRoute < 0)
            {
                var id = remaining[0];
                remaining.RemoveAt(0);
                clusters.Add(MakeCluster(id, customersByRoute));
                continue;
            }

            clusters[bestCluster].RouteIds.Add(bestRoute);
            clusters[bestCluster].Customers.UnionWith(CustomersOf(bestRoute, customersByRoute));
            remaining.Remove(bestRoute);
            BindCorridorPartners(clusters, remaining, customersByRoute, routeNamesById, brochuresPerClient, vanCapacity);
        }

        return clusters;
    }

    private static void BindCorridorPartners(
        List<RouteCluster> clusters,
        List<int> remaining,
        Dictionary<int, HashSet<int>> customersByRoute,
        Dictionary<int, string> routeNamesById,
        int brochuresPerClient,
        int? vanCapacity)
    {
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var id in remaining.ToList())
            {
                var bestCluster = -1;
                var bestOverlap = -1;
                for (var i = 0; i < clusters.Count; i++)
                {
                    if (!clusters[i].RouteIds.Any(other => SameCorridor(id, other, routeNamesById)))
                        continue;
                    var overlap = CountOverlap(clusters[i].Customers, CustomersOf(id, customersByRoute));
                    var load = clusters[i].Customers.Count + CustomersOf(id, customersByRoute).Count - overlap;
                    if (!FitsCapacity(load, brochuresPerClient, vanCapacity))
                        continue;
                    if (overlap > bestOverlap)
                    {
                        bestOverlap = overlap;
                        bestCluster = i;
                    }
                }

                if (bestCluster < 0)
                    continue;
                clusters[bestCluster].RouteIds.Add(id);
                clusters[bestCluster].Customers.UnionWith(CustomersOf(id, customersByRoute));
                remaining.Remove(id);
                progress = true;
            }
        }
    }

    private static bool TryMergeBestOverlap(
        List<RouteCluster> clusters,
        Dictionary<int, HashSet<int>> customersByRoute,
        int brochuresPerClient,
        int? vanCapacity)
    {
        var bestI = -1;
        var bestJ = -1;
        var bestOverlap = 0;
        var bestCombined = int.MaxValue;

        for (var i = 0; i < clusters.Count; i++)
        {
            for (var j = i + 1; j < clusters.Count; j++)
            {
                var overlap = CountOverlap(clusters[i].Customers, clusters[j].Customers);
                if (overlap <= 0)
                    continue;
                var combined = clusters[i].Customers.Count + clusters[j].Customers.Count - overlap;
                if (!FitsCapacity(combined, brochuresPerClient, vanCapacity))
                    continue;
                if (overlap > bestOverlap || (overlap == bestOverlap && combined < bestCombined))
                {
                    bestOverlap = overlap;
                    bestCombined = combined;
                    bestI = i;
                    bestJ = j;
                }
            }
        }

        if (bestI < 0)
            return false;

        clusters[bestI].RouteIds.AddRange(clusters[bestJ].RouteIds);
        clusters[bestI].Customers = UnionCustomers(clusters[bestI].RouteIds, customersByRoute);
        clusters.RemoveAt(bestJ);
        return true;
    }

    private static void EnsureMinRoutes(
        List<RouteCluster> clusters,
        Dictionary<int, HashSet<int>> customersByRoute,
        Dictionary<int, string> routeNamesById,
        int minRoutes)
    {
        if (minRoutes < 2)
            return;
        for (var step = 0; step < 50; step++)
        {
            var small = clusters.FirstOrDefault(cluster => cluster.RouteIds.Count < minRoutes);
            if (small is null)
                return;
            var donor = clusters
                .Where(cluster => !ReferenceEquals(cluster, small) && cluster.RouteIds.Count > minRoutes)
                .OrderByDescending(cluster => cluster.RouteIds.Count)
                .FirstOrDefault();
            if (donor is null)
                return;
            var movable = donor.RouteIds
                .Where(id => !donor.RouteIds.Any(other => other != id && SameCorridor(id, other, routeNamesById)))
                .ToList();
            if (movable.Count == 0)
                return;
            var bestId = movable[0];
            var bestOverlap = -1;
            foreach (var id in movable)
            {
                var overlap = CountOverlap(CustomersOf(id, customersByRoute), small.Customers);
                if (overlap <= bestOverlap)
                    continue;
                bestOverlap = overlap;
                bestId = id;
            }
            donor.RouteIds.Remove(bestId);
            small.RouteIds.Add(bestId);
            donor.Customers = UnionCustomers(donor.RouteIds, customersByRoute);
            small.Customers = UnionCustomers(small.RouteIds, customersByRoute);
        }
    }

    private static void ImproveSharedAssignments(
        List<RouteCluster> clusters,
        Dictionary<int, HashSet<int>> customersByRoute,
        Dictionary<int, string> routeNamesById,
        int brochuresPerClient,
        int? vanCapacity,
        int minRoutes)
    {
        for (var step = 0; step < 400; step++)
        {
            var moved = false;
            var currentShared = SharedScore(clusters, customersByRoute);
            foreach (var source in clusters)
            {
                if (source.RouteIds.Count <= minRoutes)
                    continue;
                foreach (var routeId in source.RouteIds.ToList())
                {
                    if (source.RouteIds.Any(other => other != routeId && SameCorridor(routeId, other, routeNamesById)))
                        continue;
                    var routeCustomers = CustomersOf(routeId, customersByRoute);
                    RouteCluster? bestDest = null;
                    var bestShared = currentShared;
                    var bestBalance = LoadSpread(clusters);
                    foreach (var dest in clusters)
                    {
                        if (ReferenceEquals(dest, source))
                            continue;
                        var destRoutes = dest.RouteIds.Concat(new[] { routeId }).ToList();
                        var destCount = UnionCustomers(destRoutes, customersByRoute).Count;
                        if (!FitsCapacity(destCount, brochuresPerClient, vanCapacity))
                            continue;
                        var sourceRoutes = source.RouteIds.Where(id => id != routeId).ToList();
                        var nextShared = currentShared
                            - SharedOnRoutes(source.RouteIds, customersByRoute)
                            - SharedOnRoutes(dest.RouteIds, customersByRoute)
                            + SharedOnRoutes(sourceRoutes, customersByRoute)
                            + SharedOnRoutes(destRoutes, customersByRoute);
                        var nextBalance = LoadSpreadAfterMove(clusters, source, dest, sourceRoutes, destRoutes, customersByRoute);
                        if (nextShared > bestShared || (nextShared == bestShared && nextBalance < bestBalance))
                        {
                            bestShared = nextShared;
                            bestBalance = nextBalance;
                            bestDest = dest;
                        }
                    }

                    if (bestDest is null)
                        continue;

                    source.RouteIds.Remove(routeId);
                    bestDest.RouteIds.Add(routeId);
                    source.Customers = UnionCustomers(source.RouteIds, customersByRoute);
                    bestDest.Customers = UnionCustomers(bestDest.RouteIds, customersByRoute);
                    moved = true;
                    break;
                }

                if (moved)
                    break;
            }

            if (!moved)
                break;
        }
    }

    private static RouteLoadVanPack BuildTruckPack(
        RouteCluster cluster,
        Dictionary<int, HashSet<int>> customersByRoute,
        Dictionary<int, HashSet<string>> stopKeysByRoute,
        Dictionary<int, string> routeNamesById,
        int brochuresPerClient,
        int? vanCapacity)
    {
        var customerCounts = new Dictionary<int, int>();
        var stopCounts = new Dictionary<string, int>();
        foreach (var routeId in cluster.RouteIds)
        {
            if (customersByRoute.TryGetValue(routeId, out var customers))
            {
                foreach (var customerId in customers)
                    customerCounts[customerId] = customerCounts.GetValueOrDefault(customerId) + 1;
            }

            if (stopKeysByRoute.TryGetValue(routeId, out var keys))
            {
                foreach (var key in keys)
                    stopCounts[key] = stopCounts.GetValueOrDefault(key) + 1;
            }
        }

        var uniqueStops = stopCounts.Count(pair => pair.Value == 1);
        var remainderStops = stopCounts.Count(pair => pair.Value > 1);
        var brochures = cluster.Customers.Count * brochuresPerClient;
        return new RouteLoadVanPack
        {
            DistinctCustomers = cluster.Customers.Count,
            SharedCustomers = customerCounts.Count(pair => pair.Value > 1),
            UniqueStops = uniqueStops,
            RemainderStops = remainderStops,
            SharedStops = uniqueStops + remainderStops,
            Brochures = brochures,
            FillPercent = vanCapacity is int cap and > 0
                ? Math.Round(100.0 * brochures / cap, 1)
                : null,
            OverCapacity = vanCapacity is int limit && brochures > limit,
            RouteIds = cluster.RouteIds.ToList(),
            RouteNames = cluster.RouteIds
                .Select(id => routeNamesById.GetValueOrDefault(id, $"Route {id}"))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static RouteCluster MakeCluster(int routeId, Dictionary<int, HashSet<int>> customersByRoute) =>
        new()
        {
            RouteIds = new List<int> { routeId },
            Customers = new HashSet<int>(CustomersOf(routeId, customersByRoute))
        };

    private static HashSet<int> CustomersOf(int routeId, Dictionary<int, HashSet<int>> customersByRoute) =>
        customersByRoute.TryGetValue(routeId, out var customers) ? customers : new HashSet<int>();

    private static int CustomerCount(int routeId, Dictionary<int, HashSet<int>> customersByRoute) =>
        CustomersOf(routeId, customersByRoute).Count;

    private static HashSet<int> UnionCustomers(
        IEnumerable<int> routeIds,
        Dictionary<int, HashSet<int>> customersByRoute)
    {
        var set = new HashSet<int>();
        foreach (var id in routeIds)
            set.UnionWith(CustomersOf(id, customersByRoute));
        return set;
    }

    private static int SharedOnRoutes(
        IEnumerable<int> routeIds,
        Dictionary<int, HashSet<int>> customersByRoute)
    {
        var counts = new Dictionary<int, int>();
        foreach (var routeId in routeIds)
        {
            foreach (var customerId in CustomersOf(routeId, customersByRoute))
                counts[customerId] = counts.GetValueOrDefault(customerId) + 1;
        }
        return counts.Count(pair => pair.Value > 1);
    }

    private static int SharedScore(List<RouteCluster> clusters, Dictionary<int, HashSet<int>> customersByRoute) =>
        clusters.Sum(cluster => SharedOnRoutes(cluster.RouteIds, customersByRoute));

    private static int LoadSpread(List<RouteCluster> clusters) =>
        clusters.Sum(cluster => cluster.Customers.Count * cluster.Customers.Count);

    private static int LoadSpreadAfterMove(
        List<RouteCluster> clusters,
        RouteCluster source,
        RouteCluster dest,
        List<int> sourceRoutes,
        List<int> destRoutes,
        Dictionary<int, HashSet<int>> customersByRoute)
    {
        var sourceCount = UnionCustomers(sourceRoutes, customersByRoute).Count;
        var destCount = UnionCustomers(destRoutes, customersByRoute).Count;
        var total = 0;
        foreach (var cluster in clusters)
        {
            var count = cluster.Customers.Count;
            if (ReferenceEquals(cluster, source))
                count = sourceCount;
            else if (ReferenceEquals(cluster, dest))
                count = destCount;
            total += count * count;
        }
        return total;
    }

    private static int CountOverlap(HashSet<int> left, HashSet<int> right)
    {
        var (small, large) = left.Count <= right.Count ? (left, right) : (right, left);
        var count = 0;
        foreach (var id in small)
        {
            if (large.Contains(id))
                count++;
        }
        return count;
    }

    private static bool FitsCapacity(int distinctCustomers, int brochuresPerClient, int? vanCapacity) =>
        vanCapacity is not int cap || cap <= 0 || distinctCustomers * brochuresPerClient <= cap;

    private static readonly Regex HighwayPattern = new(@"I-\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool SameCorridor(int leftId, int rightId, Dictionary<int, string> routeNamesById)
    {
        routeNamesById.TryGetValue(leftId, out var leftName);
        routeNamesById.TryGetValue(rightId, out var rightName);
        var leftHighways = HighwaysOn(leftName);
        var rightHighways = HighwaysOn(rightName);
        if (!leftHighways.Overlaps(rightHighways))
            return false;
        return IsRestAreaName(leftName) != IsRestAreaName(rightName);
    }

    private static HashSet<int> HighwaysOn(string? routeName)
    {
        var highways = new HashSet<int>();
        var name = routeName ?? "";
        foreach (Match match in HighwayPattern.Matches(name))
        {
            if (int.TryParse(match.Groups[1].Value, out var highway))
                highways.Add(highway);
        }
        if (name.Contains("Mid-TN", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mid TN", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mid-Tennessee", StringComparison.OrdinalIgnoreCase))
        {
            highways.Add(75);
        }
        return highways;
    }

    private static bool IsRestAreaName(string? routeName) =>
        (routeName ?? "").Contains("Rest Area", StringComparison.OrdinalIgnoreCase);

    private sealed class RouteCluster
    {
        public List<int> RouteIds { get; set; } = new();
        public HashSet<int> Customers { get; set; } = new();
    }
}
