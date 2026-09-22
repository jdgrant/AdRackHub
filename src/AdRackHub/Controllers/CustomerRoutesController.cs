using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class CustomerRoutesController : Controller
{
    private readonly ApplicationDbContext _context;

    public CustomerRoutesController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Create(int customerId)
    {
        var customer = await _context.Customers.FindAsync(customerId);
        if (customer == null) return NotFound();

        var vm = await BuildEditViewModelAsync(new CustomerRoute
        {
            CustomerId = customerId,
            AllStops = true,
            Status = CustomerRouteStatus.Active,
            BillingTerm = BillingFrequency.Quarterly,
            BillingMonthCount = 3
        });

        ViewBag.CustomerName = customer.CustomerName;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerRouteEditViewModel vm)
    {
        if (!vm.CustomerRoute.AllStops && !vm.SelectedStopIds.Any())
            ModelState.AddModelError("", "Select at least one stop, or choose All Stops.");

        if (!vm.SelectedMonthNumbers.Any())
            ModelState.AddModelError("", "Select at least one subscribed month.");

        if (ModelState.IsValid)
        {
            AnnualBillingHelper.ApplyBillingMonths(vm.CustomerRoute, vm.CustomerRoute.BillingMonthCount);
            vm.CustomerRoute.SubscribedMonthMask = SubscribedMonths.BuildMask(vm.SelectedMonthNumbers);
            _context.Add(vm.CustomerRoute);
            await _context.SaveChangesAsync();
            await SyncStopsAsync(vm.CustomerRoute.Id, vm.CustomerRoute.AllStops, vm.SelectedStopIds);
            return RedirectToAction("Details", "Customers", new { id = vm.CustomerRoute.CustomerId });
        }

        vm.AvailableStops = await GetAvailableStopsAsync(vm.CustomerRoute.RouteId, vm.SelectedStopIds);
        await PopulateRoutesAsync(vm.CustomerRoute.RouteId);
        var customer = await _context.Customers.FindAsync(vm.CustomerRoute.CustomerId);
        ViewBag.CustomerName = customer?.CustomerName;
        return View(vm);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var customerRoute = await _context.CustomerRoutes
            .Include(cr => cr.Customer)
            .Include(cr => cr.CustomerRouteStops)
            .FirstOrDefaultAsync(cr => cr.Id == id);

        if (customerRoute == null) return NotFound();

        var selectedIds = customerRoute.CustomerRouteStops.Select(crs => crs.StopId).ToList();
        var vm = await BuildEditViewModelAsync(customerRoute, selectedIds);
        vm.SelectedMonthNumbers = SubscribedMonths.GetMonths(customerRoute.SubscribedMonthMask).ToList();
        ViewBag.CustomerName = customerRoute.Customer.CustomerName;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CustomerRouteEditViewModel vm)
    {
        if (id != vm.CustomerRoute.Id) return NotFound();

        if (!vm.CustomerRoute.AllStops && !vm.SelectedStopIds.Any())
            ModelState.AddModelError("", "Select at least one stop, or choose All Stops.");

        if (!vm.SelectedMonthNumbers.Any())
            ModelState.AddModelError("", "Select at least one subscribed month.");

        if (ModelState.IsValid)
        {
            AnnualBillingHelper.ApplyBillingMonths(vm.CustomerRoute, vm.CustomerRoute.BillingMonthCount);
            vm.CustomerRoute.SubscribedMonthMask = SubscribedMonths.BuildMask(vm.SelectedMonthNumbers);
            try
            {
                _context.Update(vm.CustomerRoute);
                await _context.SaveChangesAsync();
                await SyncStopsAsync(vm.CustomerRoute.Id, vm.CustomerRoute.AllStops, vm.SelectedStopIds);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.CustomerRoutes.AnyAsync(cr => cr.Id == id))
                    return NotFound();
                throw;
            }
            return RedirectToAction("Details", "Customers", new { id = vm.CustomerRoute.CustomerId });
        }

        vm.AvailableStops = await GetAvailableStopsAsync(vm.CustomerRoute.RouteId, vm.SelectedStopIds);
        await PopulateRoutesAsync(vm.CustomerRoute.RouteId);
        var customer = await _context.Customers.FindAsync(vm.CustomerRoute.CustomerId);
        ViewBag.CustomerName = customer?.CustomerName;
        return View(vm);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var customerRoute = await _context.CustomerRoutes
            .Include(cr => cr.Customer)
            .Include(cr => cr.Route)
            .FirstOrDefaultAsync(cr => cr.Id == id);

        if (customerRoute == null) return NotFound();
        return View(customerRoute);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var customerRoute = await _context.CustomerRoutes.FindAsync(id);
        if (customerRoute != null)
        {
            var customerId = customerRoute.CustomerId;
            _context.CustomerRoutes.Remove(customerRoute);
            await _context.SaveChangesAsync();
            return RedirectToAction("Details", "Customers", new { id = customerId });
        }
        return RedirectToAction("Index", "Customers");
    }

    [HttpGet]
    public async Task<IActionResult> GetRouteDefaults(int routeId)
    {
        var route = await _context.Routes.FindAsync(routeId);
        if (route == null)
            return NotFound();

        return Json(new
        {
            ratePerMonth = SubscribedMonths.DefaultRatePerMonth(route),
            billingFrequency = route.BillingFrequency.ToString(),
            billingMonths = AnnualBillingHelper.MonthsInTerm(route.BillingFrequency),
            routePrice = route.Price
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetStopsForRoute(int routeId, int? customerRouteId)
    {
        List<int> selectedIds = new();
        if (customerRouteId.HasValue)
        {
            selectedIds = await _context.CustomerRouteStops
                .Where(crs => crs.CustomerRouteId == customerRouteId.Value)
                .Select(crs => crs.StopId)
                .ToListAsync();
        }

        var stops = await GetAvailableStopsAsync(routeId, selectedIds);
        return Json(stops);
    }

    private async Task<CustomerRouteEditViewModel> BuildEditViewModelAsync(CustomerRoute customerRoute, List<int>? selectedStopIds = null)
    {
        selectedStopIds ??= new List<int>();
        await PopulateRoutesAsync(customerRoute.RouteId);
        return new CustomerRouteEditViewModel
        {
            CustomerRoute = customerRoute,
            SelectedStopIds = selectedStopIds,
            AvailableStops = await GetAvailableStopsAsync(customerRoute.RouteId, selectedStopIds)
        };
    }

    private async Task<List<StopSelectionItem>> GetAvailableStopsAsync(int routeId, List<int> selectedStopIds)
    {
        if (routeId <= 0)
            return new List<StopSelectionItem>();

        var stops = await _context.Stops
            .Where(s => s.RouteId == routeId && s.Status == StopStatus.Active)
            .OrderBy(s => s.StopName)
            .ToListAsync();

        return stops.Select(s => new StopSelectionItem
        {
            StopId = s.Id,
            StopName = s.StopName,
            IsSelected = selectedStopIds.Contains(s.Id)
        }).ToList();
    }

    private async Task SyncStopsAsync(int customerRouteId, bool allStops, List<int> selectedStopIds)
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

    private async Task PopulateRoutesAsync(int? selectedRouteId = null)
    {
        ViewBag.RouteId = new SelectList(
            await _context.Routes.Where(r => r.Status == RouteStatus.Active).OrderBy(r => r.RouteName).ToListAsync(),
            "Id", "RouteName", selectedRouteId);
    }
}
