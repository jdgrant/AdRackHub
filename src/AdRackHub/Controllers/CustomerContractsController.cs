using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
[Route("CustomerContracts/[action]/{id?}")]
[Route("Contracts/[action]/{id?}")]
public class CustomerContractsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly WaveSyncService _waveSyncService;

    public CustomerContractsController(ApplicationDbContext context, WaveSyncService waveSyncService)
    {
        _context = context;
        _waveSyncService = waveSyncService;
    }

    public async Task<IActionResult> Create(int customerId)
    {
        var customer = await _context.Customers.FindAsync(customerId);
        if (customer == null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var vm = await BuildEditViewModelAsync(new CustomerContract
        {
            CustomerId = customerId,
            Term = BillingFrequency.Quarterly,
            ContractName = await SuggestUniqueContractNameAsync(customerId, customer.CustomerName, today),
            ServiceMonthMask = SubscribedMonths.AllMonthsMask,
            NextBillDate = new DateOnly(today.Year, today.Month, 1),
            BillingAnchorMonth = today.Month
        }, selectedMonthNumbers: Enumerable.Range(1, 12).ToList());

        ViewBag.CustomerId = customerId;
        ViewBag.CustomerName = customer.CustomerName;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(ValueCountLimit = 16384)]
    public async Task<IActionResult> Create([FromForm(Name = "customerId")] int customerId, CustomerContractEditViewModel vm)
    {
        customerId = ResolveCustomerId(customerId, vm);
        vm.Contract ??= new CustomerContract();
        vm.Contract.CustomerId = customerId;
        ClearCustomerIdModelState();

        if (customerId <= 0)
        {
            TempData["Error"] = "Could not read the contract form. Please try again.";
            return RedirectToAction(nameof(Create), new { customerId });
        }

        if (vm.SelectedRouteIds == null || !vm.SelectedRouteIds.Any())
            ModelState.AddModelError("", "Select at least one route for this contract.");

        ApplyContractFields(vm);
        ValidateRouteStops(vm);
        await ValidateUniqueContractNameAsync(vm);

        if (ModelState.IsValid)
        {
            try
            {
                _context.Add(vm.Contract);
                await _context.SaveChangesAsync();
                await SyncRoutesAsync(vm);
                await _waveSyncService.PushContractToWaveAsync(vm.Contract.Id);
                return RedirectToAction("Details", "Customers", new { id = vm.Contract.CustomerId });
            }
            catch (DbUpdateException ex) when (IsDuplicateContractNameException(ex))
            {
                ModelState.AddModelError(
                    "Contract.ContractName",
                    DuplicateContractNameMessage(vm.Contract.ContractName));
            }
        }

        vm.AvailableRoutes = await GetAvailableRoutesAsync(
            vm.Contract.CustomerId,
            vm.SelectedRouteIds ?? new List<int>(),
            vm.Contract.Term,
            vm.Contract.ContractRoutes,
            vm.AvailableRoutes);
        var customer = await _context.Customers.FindAsync(vm.Contract.CustomerId);
        ViewBag.CustomerId = vm.Contract.CustomerId;
        ViewBag.CustomerName = customer?.CustomerName;
        return View(vm);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var contract = await _context.CustomerContracts
            .Include(c => c.Customer)
            .Include(c => c.ContractRoutes)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contract == null) return NotFound();

        var selectedIds = contract.ContractRoutes.Select(cr => cr.RouteId).ToList();
        var vm = await BuildEditViewModelAsync(contract, selectedIds);
        ViewBag.CustomerId = contract.CustomerId;
        ViewBag.CustomerName = contract.Customer.CustomerName;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(ValueCountLimit = 16384)]
    public async Task<IActionResult> Edit(int id, [FromForm(Name = "customerId")] int customerId, CustomerContractEditViewModel vm)
    {
        vm.Contract ??= new CustomerContract();
        if (customerId <= 0 && vm.Contract.Id > 0)
        {
            var existing = await _context.CustomerContracts.AsNoTracking()
                .Where(c => c.Id == vm.Contract.Id)
                .Select(c => c.CustomerId)
                .FirstOrDefaultAsync();
            customerId = existing;
        }

        customerId = ResolveCustomerId(customerId, vm);
        vm.Contract.CustomerId = customerId;
        ClearCustomerIdModelState();

        if (id != vm.Contract.Id) return NotFound();

        if (vm.SelectedRouteIds == null || !vm.SelectedRouteIds.Any())
            ModelState.AddModelError("", "Select at least one route for this contract.");

        ApplyContractFields(vm);
        ValidateRouteStops(vm);
        await ValidateUniqueContractNameAsync(vm);

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(vm.Contract);
                await _context.SaveChangesAsync();
                await SyncRoutesAsync(vm);
                return RedirectToAction("Details", "Customers", new { id = vm.Contract.CustomerId });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.CustomerContracts.AnyAsync(c => c.Id == id))
                    return NotFound();
                throw;
            }
            catch (DbUpdateException ex) when (IsDuplicateContractNameException(ex))
            {
                ModelState.AddModelError(
                    "Contract.ContractName",
                    DuplicateContractNameMessage(vm.Contract.ContractName));
            }
        }

        vm.AvailableRoutes = await GetAvailableRoutesAsync(
            vm.Contract.CustomerId,
            vm.SelectedRouteIds ?? new List<int>(),
            vm.Contract.Term,
            vm.Contract.ContractRoutes,
            vm.AvailableRoutes);
        var customer = await _context.Customers.FindAsync(vm.Contract.CustomerId);
        ViewBag.CustomerId = vm.Contract.CustomerId;
        ViewBag.CustomerName = customer?.CustomerName;
        return View(vm);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var contract = await _context.CustomerContracts
            .Include(c => c.Customer)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contract == null) return NotFound();
        return View(contract);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var contract = await _context.CustomerContracts.FindAsync(id);
        if (contract != null)
        {
            var customerId = contract.CustomerId;
            _context.CustomerContracts.Remove(contract);
            await _context.SaveChangesAsync();
            await SyncCustomerRoutesFromContractsAsync(customerId);
            return RedirectToAction("Details", "Customers", new { id = customerId });
        }

        return RedirectToAction("Index", "Customers");
    }

    private int ResolveCustomerId(int customerId, CustomerContractEditViewModel vm)
    {
        if (customerId > 0)
            return customerId;
        if (Request.Form.TryGetValue("customerId", out var formValue) && int.TryParse(formValue, out var formId) && formId > 0)
            return formId;
        if (Request.Query.TryGetValue("customerId", out var queryValue) && int.TryParse(queryValue, out var queryId) && queryId > 0)
            return queryId;
        return 0;
    }

    private void ClearCustomerIdModelState()
    {
        foreach (var key in ModelState.Keys.Where(k =>
            k.Contains("CustomerId", StringComparison.OrdinalIgnoreCase) ||
            k.Contains(".Customer", StringComparison.OrdinalIgnoreCase)).ToList())
            ModelState.Remove(key);
    }

    private async Task<CustomerContractEditViewModel> BuildEditViewModelAsync(
        CustomerContract contract,
        List<int>? selectedRouteIds = null,
        List<int>? selectedMonthNumbers = null,
        List<RouteSelectionItem>? postedRoutes = null)
    {
        selectedRouteIds ??= contract.ContractRoutes.Select(cr => cr.RouteId).ToList();
        selectedMonthNumbers ??= SubscribedMonths.GetMonths(contract.ServiceMonthMask).ToList();
        if (!selectedMonthNumbers.Any())
            selectedMonthNumbers = Enumerable.Range(1, 12).ToList();

        var availableRoutes = await GetAvailableRoutesAsync(
            contract.CustomerId,
            selectedRouteIds,
            contract.Term,
            contract.ContractRoutes,
            postedRoutes);

        return new CustomerContractEditViewModel
        {
            Contract = contract,
            SelectedRouteIds = selectedRouteIds,
            SelectedMonthNumbers = selectedMonthNumbers,
            RouteBillingAmounts = availableRoutes
                .Where(r => selectedRouteIds.Contains(r.RouteId))
                .ToDictionary(r => r.RouteId, r => r.BillingAmount),
            AvailableRoutes = availableRoutes
        };
    }

    private void ApplyContractFields(CustomerContractEditViewModel vm)
    {
        if (vm.SelectedMonthNumbers == null || !vm.SelectedMonthNumbers.Any())
            ModelState.AddModelError("", "Select at least one month of service.");

        vm.Contract.ContractName = (vm.Contract.ContractName ?? string.Empty).Trim();
        vm.Contract.ServiceMonthMask = SubscribedMonths.BuildMask(vm.SelectedMonthNumbers ?? new List<int>());
        vm.Contract.BillingAnchorMonth = vm.Contract.NextBillDate.Month;
    }

    private async Task ValidateUniqueContractNameAsync(CustomerContractEditViewModel vm)
    {
        var name = (vm.Contract.ContractName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || vm.Contract.CustomerId <= 0)
            return;

        var duplicate = await _context.CustomerContracts.AsNoTracking().AnyAsync(c =>
            c.CustomerId == vm.Contract.CustomerId
            && c.Id != vm.Contract.Id
            && c.ContractName == name);

        if (duplicate)
            ModelState.AddModelError("Contract.ContractName", DuplicateContractNameMessage(name));
    }

    private static string DuplicateContractNameMessage(string? name)
    {
        var display = string.IsNullOrWhiteSpace(name) ? "this name" : $"\"{name.Trim()}\"";
        return $"A contract named {display} already exists for this customer. Choose a different contract name.";
    }

    private static bool IsDuplicateContractNameException(DbUpdateException ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("IX_CustomerBillings_CustomerId_BillName", StringComparison.OrdinalIgnoreCase)
                || (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("BillName", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private void ValidateRouteStops(CustomerContractEditViewModel vm)
    {
        if (vm.SelectedRouteIds == null)
            return;

        foreach (var routeId in vm.SelectedRouteIds.Distinct())
        {
            var route = vm.AvailableRoutes.FirstOrDefault(r => r.RouteId == routeId);
            if (route == null || !route.Stops.Any())
                continue;

            if (!route.AllStops && !route.Stops.Any(s => s.IsSelected))
                ModelState.AddModelError("", $"Select at least one location for {route.RouteName}, or choose all locations.");
        }
    }

    private static Dictionary<int, RouteStopSelection> ExtractRouteStopSelections(CustomerContractEditViewModel vm) =>
        vm.AvailableRoutes
            .Where(r => vm.SelectedRouteIds.Contains(r.RouteId))
            .ToDictionary(
                r => r.RouteId,
                r => new RouteStopSelection
                {
                    AllStops = r.AllStops,
                    StopIds = r.AllStops
                        ? new List<int>()
                        : r.Stops.Where(s => s.IsSelected).Select(s => s.StopId).ToList()
                });

    private static Dictionary<int, decimal> ExtractRouteBillingAmounts(CustomerContractEditViewModel vm) =>
        vm.AvailableRoutes
            .Where(r => vm.SelectedRouteIds.Contains(r.RouteId))
            .ToDictionary(r => r.RouteId, r => r.BillingAmount);

    private static string DefaultContractName(string customerName, DateOnly? asOf = null)
    {
        asOf ??= DateOnly.FromDateTime(DateTime.Today);
        return $"{customerName} {asOf.Value.Year}";
    }

    private async Task<string> SuggestUniqueContractNameAsync(int customerId, string customerName, DateOnly asOf)
    {
        var baseName = DefaultContractName(customerName, asOf).Trim();
        var existing = await _context.CustomerContracts.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => c.ContractName)
            .ToListAsync();

        if (!existing.Any(n => string.Equals(n.Trim(), baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!existing.Any(n => string.Equals(n.Trim(), candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{baseName} ({DateTime.UtcNow:HHmmss})";
    }

    private async Task<List<RouteSelectionItem>> GetAvailableRoutesAsync(
        int customerId,
        List<int> selectedRouteIds,
        BillingFrequency term,
        IEnumerable<CustomerContractRoute>? existingContractRoutes = null,
        List<RouteSelectionItem>? postedRoutes = null)
    {
        var existingAmounts = existingContractRoutes?
            .ToDictionary(cr => cr.RouteId, cr => cr.BillingAmount) ?? new Dictionary<int, decimal>();
        var postedByRouteId = postedRoutes?.ToDictionary(r => r.RouteId) ?? new Dictionary<int, RouteSelectionItem>();

        var routes = await _context.Routes
            .Where(r => r.Status == RouteStatus.Active)
            .OrderBy(r => r.RouteName)
            .ToListAsync();

        var routeIds = routes.Select(r => r.Id).ToList();
        var stops = await _context.Stops
            .Where(s => routeIds.Contains(s.RouteId) && s.Status == StopStatus.Active)
            .OrderBy(s => s.StepNumber)
            .ThenBy(s => s.StopName)
            .ToListAsync();

        var customerRoutes = await _context.CustomerRoutes
            .Include(cr => cr.CustomerRouteStops)
            .Where(cr => cr.CustomerId == customerId && routeIds.Contains(cr.RouteId))
            .ToListAsync();

        return routes.Select(r =>
        {
            var defaultMonthly = AnnualBillingHelper.ToRatePerMonth(r.Price);
            var routeStops = stops.Where(s => s.RouteId == r.Id).ToList();
            var customerRoute = customerRoutes.FirstOrDefault(cr => cr.RouteId == r.Id);
            postedByRouteId.TryGetValue(r.Id, out var posted);

            var billingAmount = posted?.BillingAmount > 0
                ? posted.BillingAmount
                : existingAmounts.TryGetValue(r.Id, out var existingAmount) && existingAmount > 0
                    ? AnnualBillingHelper.BillingPeriodAmountToMonthlyRate(existingAmount, term)
                    : defaultMonthly;

            bool allStops;
            HashSet<int> selectedStopIds;

            if (posted != null && posted.Stops.Any())
            {
                allStops = posted.AllStops;
                selectedStopIds = posted.AllStops
                    ? routeStops.Select(s => s.Id).ToHashSet()
                    : posted.Stops.Where(s => s.IsSelected).Select(s => s.StopId).ToHashSet();
            }
            else if (customerRoute != null)
            {
                allStops = customerRoute.AllStops;
                selectedStopIds = allStops
                    ? routeStops.Select(s => s.Id).ToHashSet()
                    : customerRoute.CustomerRouteStops.Select(crs => crs.StopId).ToHashSet();
            }
            else
            {
                allStops = true;
                selectedStopIds = routeStops.Select(s => s.Id).ToHashSet();
            }

            return new RouteSelectionItem
            {
                RouteId = r.Id,
                RouteName = r.RouteName,
                Price = r.Price,
                DefaultBillingAmount = defaultMonthly,
                BillingAmount = billingAmount,
                BillingFrequency = r.BillingFrequency,
                IsSelected = selectedRouteIds.Contains(r.Id),
                AllStops = allStops,
                Stops = routeStops.Select(s => new StopSelectionItem
                {
                    StopId = s.Id,
                    StopName = s.StopName,
                    IsSelected = selectedStopIds.Contains(s.Id)
                }).ToList()
            };
        }).ToList();
    }

    private async Task SyncRoutesAsync(CustomerContractEditViewModel vm)
    {
        var selectedRouteIds = vm.SelectedRouteIds;
        var routeBillingAmounts = ExtractRouteBillingAmounts(vm);
        var routeStopSelections = ExtractRouteStopSelections(vm);

        var contract = await _context.CustomerContracts
            .Include(c => c.ContractRoutes)
            .FirstOrDefaultAsync(c => c.Id == vm.Contract.Id);

        if (contract == null)
            return;

        _context.CustomerContractRoutes.RemoveRange(contract.ContractRoutes);

        if (selectedRouteIds.Any())
        {
            var routes = await _context.Routes
                .Where(r => selectedRouteIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);

            foreach (var routeId in selectedRouteIds.Distinct())
            {
                if (!routes.TryGetValue(routeId, out var route))
                    continue;

                var monthlyRate = routeBillingAmounts.TryGetValue(routeId, out var postedAmount) && postedAmount >= 0
                    ? postedAmount
                    : AnnualBillingHelper.ToRatePerMonth(route.Price);
                var billingAmount = AnnualBillingHelper.MonthlyRateToBillingPeriodAmount(monthlyRate, contract.Term);

                _context.CustomerContractRoutes.Add(new CustomerContractRoute
                {
                    CustomerContractId = contract.Id,
                    RouteId = routeId,
                    BillingAmount = billingAmount
                });
            }
        }

        await _context.SaveChangesAsync();
        await SyncCustomerRoutesFromContractsAsync(contract.CustomerId, routeStopSelections);
    }

    private async Task SyncCustomerRoutesFromContractsAsync(
        int customerId,
        Dictionary<int, RouteStopSelection>? routeStopSelections = null)
    {
        var contracts = await _context.CustomerContracts
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
            .Where(c => c.CustomerId == customerId)
            .ToListAsync();

        var routesOnActiveContracts = contracts
            .Where(c => BillingDueCalculator.IsActiveContract(c))
            .SelectMany(c => c.ContractRoutes.Select(cr => (Contract: c, ContractRoute: cr)))
            .GroupBy(x => x.ContractRoute.RouteId)
            .ToDictionary(g => g.Key, g => g.First());

        var existing = await _context.CustomerRoutes
            .Include(cr => cr.CustomerRouteStops)
            .Where(cr => cr.CustomerId == customerId)
            .ToListAsync();

        var activeRouteIds = routesOnActiveContracts.Keys.ToHashSet();

        foreach (var routeId in activeRouteIds)
        {
            var entry = routesOnActiveContracts[routeId];
            var customerRoute = existing.FirstOrDefault(cr => cr.RouteId == routeId);

            if (customerRoute == null)
            {
                customerRoute = new CustomerRoute
                {
                    CustomerId = customerId,
                    RouteId = routeId
                };
                _context.CustomerRoutes.Add(customerRoute);
                existing.Add(customerRoute);
            }

            customerRoute.Status = CustomerRouteStatus.Active;
            customerRoute.BillingTerm = entry.Contract.Term;
            customerRoute.SubscribedMonthMask = entry.Contract.ServiceMonthMask;
            var billingAmount = AnnualBillingHelper.GetBillingAmount(entry.ContractRoute);
            customerRoute.RatePerMonth = AnnualBillingHelper.ToRatePerMonth(
                AnnualBillingHelper.ToAnnualPrice(billingAmount, entry.Contract.Term));

            if (routeStopSelections?.TryGetValue(routeId, out var stopSelection) == true)
                customerRoute.AllStops = stopSelection.AllStops;
            else if (customerRoute.Id == 0)
                customerRoute.AllStops = true;
        }

        foreach (var customerRoute in existing.Where(cr => !activeRouteIds.Contains(cr.RouteId)))
            _context.CustomerRoutes.Remove(customerRoute);

        await _context.SaveChangesAsync();

        foreach (var routeId in activeRouteIds)
        {
            if (routeStopSelections == null || !routeStopSelections.TryGetValue(routeId, out var stopSelection))
                continue;

            var customerRoute = existing.First(cr => cr.RouteId == routeId);
            await ApplyCustomerRouteStopsAsync(customerRoute.Id, stopSelection.AllStops, stopSelection.StopIds);
        }

        await _context.SaveChangesAsync();
    }

    private async Task ApplyCustomerRouteStopsAsync(int customerRouteId, bool allStops, List<int> selectedStopIds)
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
    }
}
