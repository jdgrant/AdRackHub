using System.Text.Json;
using System.Text.Json.Serialization;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class Sept2026ContractImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<Sept2026ContractImportService> _logger;

    public Sept2026ContractImportService(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        ILogger<Sept2026ContractImportService> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    public async Task<Sept2026ContractImportResult> ImportAsync(
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var file = await LoadFileAsync(cancellationToken);
        var result = new Sept2026ContractImportResult { DryRun = dryRun, SeedCount = file.Contracts.Count };

        var routes = await _context.Routes.AsNoTracking().ToListAsync(cancellationToken);
        var stopsByRoute = await _context.Stops
            .AsNoTracking()
            .Where(s => s.Status == StopStatus.Active)
            .GroupBy(s => s.RouteId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList(), cancellationToken);

        var customers = await _context.Customers
            .Include(c => c.Contracts)
                .ThenInclude(c => c.ContractRoutes)
                    .ThenInclude(cr => cr.Route)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        foreach (var seed in file.Contracts)
        {
            var row = ValidateSeed(seed, file, routes, stopsByRoute, customers, out var skipReason);
            if (row == null)
            {
                result.Errors.Add(skipReason ?? $"Invalid seed {seed.BillName}");
                continue;
            }

            if (HasMatchingProductContract(row.Customer, row.Product))
            {
                result.Skipped.Add($"{row.Label} — already has a {row.Product} contract");
                continue;
            }

            if (dryRun)
            {
                result.WouldCreate.Add(row.Label);
                continue;
            }

            var created = await CreateContractAsync(row, file, stopsByRoute, cancellationToken);
            result.Created.Add(created);
        }

        if (!dryRun)
            result.Verifications.AddRange(await VerifyAsync(cancellationToken));

        return result;
    }

    /// <summary>
    /// Re-attaches hotel/Exit routes onto existing contracts that lost their route rows
    /// after Exit routes were replaced. Matches routes by name, not the old IDs.
    /// </summary>
    public async Task<Sept2026ContractImportResult> RepairDisconnectedExitRoutesAsync(
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var file = await LoadFileAsync(cancellationToken);
        var result = new Sept2026ContractImportResult { DryRun = dryRun, SeedCount = file.Contracts.Count };

        var routes = await _context.Routes.ToListAsync(cancellationToken);
        var stopsByRoute = await _context.Stops
            .Where(s => s.Status == StopStatus.Active)
            .GroupBy(s => s.RouteId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList(), cancellationToken);

        var customers = await _context.Customers
            .Include(c => c.Contracts)
                .ThenInclude(c => c.ContractRoutes)
                    .ThenInclude(cr => cr.Route)
            .Include(c => c.CustomerRoutes)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        foreach (var seed in file.Contracts.Where(s => s.Product == RouteProduct.Exits))
        {
            var row = ValidateSeed(seed, file, routes, stopsByRoute, customers, out var skipReason);
            if (row == null)
            {
                result.Errors.Add(skipReason ?? $"Invalid seed {seed.BillName}");
                continue;
            }

            var contract = FindContractForSeed(row.Customer, seed);
            if (contract == null)
            {
                result.Errors.Add($"{row.Label} — no existing Exits contract to repair");
                continue;
            }

            var resolved = seed.Routes
                .Select(rs => (Seed: rs, Route: ResolveRoute(rs, routes)))
                .ToList();
            if (resolved.Any(x => x.Route == null))
            {
                result.Errors.Add($"{row.Label} — could not resolve a current hotel route by name");
                continue;
            }

            var expectedIds = resolved.Select(x => x.Route!.Id).OrderBy(id => id).ToArray();
            var actualIds = contract.ContractRoutes.Select(cr => cr.RouteId).OrderBy(id => id).ToArray();
            var amountsMatch = resolved.All(x =>
                contract.ContractRoutes.Any(cr =>
                    cr.RouteId == x.Route!.Id && cr.BillingAmount == x.Seed.Amount));

            if (expectedIds.SequenceEqual(actualIds) && amountsMatch)
            {
                result.Skipped.Add($"{row.Label} — already on current hotel routes (contract #{contract.Id})");
                continue;
            }

            var routeSummary = string.Join(", ", resolved.Select(x => $"{x.Route!.RouteName} ${x.Seed.Amount:0.00}"));
            var label = $"{row.Label} → contract #{contract.Id} {contract.ContractName}: {routeSummary}";

            if (dryRun)
            {
                result.WouldRepair.Add(label);
                continue;
            }

            _context.CustomerContractRoutes.RemoveRange(contract.ContractRoutes);
            foreach (var item in resolved)
            {
                _context.CustomerContractRoutes.Add(new CustomerContractRoute
                {
                    CustomerContractId = contract.Id,
                    RouteId = item.Route!.Id,
                    BillingAmount = item.Seed.Amount
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await UpsertCustomerRoutesAsync(row, routes, stopsByRoute, cancellationToken);
            result.Repaired.Add(label);
            _logger.LogInformation(
                "Repaired Exit routes on contract {ContractId} for customer {CustomerId}",
                contract.Id,
                row.Customer.Id);
        }

        await RepairEmptyHotelContractsAsync(result, dryRun, cancellationToken);
        return result;
    }

    private async Task RepairEmptyHotelContractsAsync(
        Sept2026ContractImportResult result,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_environment.ContentRootPath, "Data", "Imports", "EmptyHotelContracts.json");
        if (!File.Exists(path))
        {
            result.Errors.Add("Missing EmptyHotelContracts.json");
            return;
        }

        var file = JsonSerializer.Deserialize<EmptyHotelContractFile>(
            await File.ReadAllTextAsync(path, cancellationToken),
            JsonOptions) ?? throw new InvalidOperationException("EmptyHotelContracts.json is empty.");

        var routes = await _context.Routes.ToListAsync(cancellationToken);
        var stopsByRoute = await _context.Stops
            .Where(s => s.Status == StopStatus.Active)
            .GroupBy(s => s.RouteId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList(), cancellationToken);

        foreach (var seed in file.Contracts)
        {
            var contract = await _context.CustomerContracts
                .Include(c => c.ContractRoutes)
                .Include(c => c.Customer)
                .FirstOrDefaultAsync(c => c.Id == seed.ContractId, cancellationToken);
            if (contract == null)
            {
                result.Errors.Add($"Empty hotel seed contract #{seed.ContractId} not found");
                continue;
            }

            if (contract.CustomerId != seed.CustomerId)
            {
                result.Errors.Add(
                    $"Empty hotel seed #{seed.ContractId} is on customer #{contract.CustomerId}, expected #{seed.CustomerId}");
                continue;
            }

            if (contract.ContractRoutes.Count > 0)
            {
                result.Skipped.Add(
                    $"{seed.CustomerName} — contract #{contract.Id} already has {contract.ContractRoutes.Count} routes");
                continue;
            }

            var resolved = seed.Routes
                .Select(rs => (
                    Seed: rs,
                    Route: routes.FirstOrDefault(r =>
                        string.Equals(r.RouteName, rs.RouteName, StringComparison.OrdinalIgnoreCase)
                        && RouteProductHelper.FromRouteName(r.RouteName) == RouteProduct.Exits)))
                .ToList();
            if (resolved.Any(x => x.Route == null))
            {
                var missing = string.Join(", ", resolved.Where(x => x.Route == null).Select(x => x.Seed.RouteName));
                result.Errors.Add($"{seed.CustomerName} — missing hotel routes: {missing}");
                continue;
            }

            var serviceMonths = seed.Routes
                .SelectMany(r => r.Months)
                .Where(m => m is >= 1 and <= 12)
                .Distinct()
                .OrderBy(m => m)
                .ToList();
            if (serviceMonths.Count == 0)
            {
                result.Errors.Add($"{seed.CustomerName} — no service months");
                continue;
            }

            var termMonths = AnnualBillingHelper.BillingMonths(contract);
            var periodTotal = Math.Round(
                seed.AnnualHotel * termMonths / serviceMonths.Count,
                2,
                MidpointRounding.AwayFromZero);
            var amounts = SplitAmount(periodTotal, resolved.Count);
            var monthMask = SubscribedMonths.BuildMask(serviceMonths);
            var summary = string.Join(
                ", ",
                resolved.Select((x, i) => $"{x.Route!.RouteName} ${amounts[i]:0.00}"));
            var label =
                $"{seed.CustomerName} → contract #{contract.Id} {contract.ContractName}: " +
                $"${periodTotal:0.00} / period from ${seed.AnnualHotel:0.00} annual over {serviceMonths.Count} months · {summary}";

            if (dryRun)
            {
                result.WouldRepair.Add(label);
                continue;
            }

            contract.ServiceMonthMask = monthMask;
            for (var i = 0; i < resolved.Count; i++)
            {
                _context.CustomerContractRoutes.Add(new CustomerContractRoute
                {
                    CustomerContractId = contract.Id,
                    RouteId = resolved[i].Route!.Id,
                    BillingAmount = amounts[i]
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            var customer = contract.Customer;
            var seedRow = new SeedRow(
                new Sept2026ContractSeed
                {
                    BillName = seed.SheetName,
                    CustomerId = seed.CustomerId,
                    ExpectedCustomerName = seed.CustomerName,
                    Product = RouteProduct.Exits,
                    ContractName = contract.ContractName,
                    Term = contract.Term,
                    Months = serviceMonths,
                    ExpectedTotal = periodTotal,
                    Routes = resolved.Select((x, i) => new Sept2026ContractRouteSeed
                    {
                        RouteId = x.Route!.Id,
                        RouteName = x.Route.RouteName,
                        Amount = amounts[i]
                    }).ToList()
                },
                customer);
            await UpsertCustomerRoutesAsync(seedRow, routes, stopsByRoute, cancellationToken);
            result.Repaired.Add(label);
            _logger.LogInformation(
                "Restored empty hotel contract {ContractId} for customer {CustomerId} from hotel workbook",
                contract.Id,
                seed.CustomerId);
        }
    }

    private static decimal[] SplitAmount(decimal total, int parts)
    {
        if (parts <= 0)
            return Array.Empty<decimal>();

        var cents = (int)Math.Round(total * 100m, MidpointRounding.AwayFromZero);
        var baseCents = cents / parts;
        var remainder = cents - baseCents * parts;
        var amounts = new decimal[parts];
        for (var i = 0; i < parts; i++)
            amounts[i] = (baseCents + (i < remainder ? 1 : 0)) / 100m;
        return amounts;
    }

    public async Task<List<Sept2026ContractVerifyItem>> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var file = await LoadFileAsync(cancellationToken);
        var items = new List<Sept2026ContractVerifyItem>();

        var customers = await _context.Customers
            .Include(c => c.Contracts)
                .ThenInclude(c => c.ContractRoutes)
                    .ThenInclude(cr => cr.Route)
            .Include(c => c.CustomerRoutes)
                .ThenInclude(cr => cr.CustomerRouteStops)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var allRoutes = await _context.Routes.AsNoTracking().ToListAsync(cancellationToken);

        foreach (var seed in file.Contracts)
        {
            var item = new Sept2026ContractVerifyItem
            {
                BillName = seed.BillName,
                Product = seed.Product,
                CustomerId = seed.CustomerId,
                ExpectedTotal = seed.ExpectedTotal
            };

            if (!customers.TryGetValue(seed.CustomerId, out var customer))
            {
                item.Failures.Add("Customer not found");
                items.Add(item);
                continue;
            }

            item.CustomerName = customer.CustomerName;
            var contract = FindContractForSeed(customer, seed);
            if (contract == null)
            {
                item.Failures.Add($"No {seed.Product} contract on customer");
                items.Add(item);
                continue;
            }

            item.ContractId = contract.Id;
            item.ContractName = contract.ContractName;
            item.ActualTotal = contract.ContractRoutes.Sum(cr => cr.BillingAmount);

            if (!string.Equals(customer.CustomerName, seed.ExpectedCustomerName, StringComparison.Ordinal))
                item.Failures.Add($"Customer name '{customer.CustomerName}' != '{seed.ExpectedCustomerName}'");

            if (contract.NextBillDate != file.NextBillDate)
                item.Failures.Add($"Next bill {contract.NextBillDate:yyyy-MM-dd} != {file.NextBillDate:yyyy-MM-dd}");

            if (contract.Term != seed.Term)
                item.Failures.Add($"Term {contract.Term} != {seed.Term}");

            var expectedMask = SubscribedMonths.BuildMask(seed.Months);
            if (contract.ServiceMonthMask != expectedMask)
                item.Failures.Add($"Months {SubscribedMonths.Format(contract.ServiceMonthMask)} != {SubscribedMonths.Format(expectedMask)}");

            if (contract.ContractEndDate != file.ContractEndDate)
                item.Failures.Add($"End date {contract.ContractEndDate} != {file.ContractEndDate}");

            var mixed = contract.ContractRoutes
                .Select(cr => RouteProductHelper.FromRouteName(cr.Route.RouteName))
                .Distinct()
                .ToList();
            if (mixed.Count != 1 || mixed[0] != seed.Product)
                item.Failures.Add($"Contract routes are {string.Join(",", mixed)} instead of {seed.Product} only");

            if (item.ActualTotal != seed.ExpectedTotal)
                item.Failures.Add($"Amount {item.ActualTotal:0.00} != {seed.ExpectedTotal:0.00}");

            foreach (var routeSeed in seed.Routes)
            {
                var route = ResolveRoute(routeSeed, allRoutes);
                var saved = route == null
                    ? null
                    : contract.ContractRoutes.FirstOrDefault(cr => cr.RouteId == route.Id);
                if (saved == null)
                {
                    item.Failures.Add($"Missing route {routeSeed.RouteName}");
                    continue;
                }

                if (saved.BillingAmount != routeSeed.Amount)
                    item.Failures.Add($"{routeSeed.RouteName} amount {saved.BillingAmount:0.00} != {routeSeed.Amount:0.00}");

                var expectedStops = routeSeed.StopIds ?? new List<int>();
                if (expectedStops.Count == 0)
                    continue;

                var customerRoute = customer.CustomerRoutes.FirstOrDefault(cr => cr.RouteId == route.Id);
                if (customerRoute == null)
                {
                    item.Failures.Add($"Missing customer route placement for {routeSeed.RouteName}");
                    continue;
                }

                var actualStops = customerRoute.AllStops
                    ? Array.Empty<int>()
                    : customerRoute.CustomerRouteStops.Select(s => s.StopId).OrderBy(id => id).ToArray();
                if (!customerRoute.AllStops)
                {
                    var expected = expectedStops.OrderBy(id => id).ToArray();
                    if (!actualStops.SequenceEqual(expected))
                        item.Failures.Add($"{routeSeed.RouteName} stops [{string.Join(",", actualStops)}] != [{string.Join(",", expected)}]");
                }
            }

            if (!BillingDueCalculator.IsContractDue(contract, 2026, 9))
                item.Failures.Add("Not due on /Billing for September 2026");

            item.Passed = item.Failures.Count == 0;
            items.Add(item);
        }

        return items;
    }

    private async Task<Sept2026ContractFile> LoadFileAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_environment.ContentRootPath, "Data", "Imports", "Sept2026Contracts.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing import file: {path}");

        var file = JsonSerializer.Deserialize<Sept2026ContractFile>(
            await File.ReadAllTextAsync(path, cancellationToken),
            JsonOptions) ?? throw new InvalidOperationException("Sept2026Contracts.json is empty.");

        if (file.Contracts.Count == 0)
            throw new InvalidOperationException("Sept2026Contracts.json has no contracts.");

        return file;
    }

    private static SeedRow? ValidateSeed(
        Sept2026ContractSeed seed,
        Sept2026ContractFile file,
        IReadOnlyList<Models.Route> routes,
        Dictionary<int, List<Stop>> stopsByRoute,
        Dictionary<int, Customer> customers,
        out string? error)
    {
        error = null;
        if (!customers.TryGetValue(seed.CustomerId, out var customer))
        {
            error = $"{seed.BillName}: customer #{seed.CustomerId} not found";
            return null;
        }

        if (seed.Routes.Count == 0)
        {
            error = $"{seed.BillName}: no routes";
            return null;
        }

        var routeTotal = seed.Routes.Sum(r => r.Amount);
        if (routeTotal != seed.ExpectedTotal)
        {
            error = $"{seed.BillName}: route amounts {routeTotal:0.00} != expected {seed.ExpectedTotal:0.00}";
            return null;
        }

        foreach (var routeSeed in seed.Routes)
        {
            var route = ResolveRoute(routeSeed, routes);
            if (route == null)
            {
                error = $"{seed.BillName}: route {routeSeed.RouteName} not found";
                return null;
            }

            var product = RouteProductHelper.FromRouteName(route.RouteName);
            if (product != seed.Product)
            {
                error = $"{seed.BillName}: {route.RouteName} is {product}, seed product is {seed.Product}";
                return null;
            }

            var stopIds = routeSeed.StopIds ?? new List<int>();
            if (seed.Product == RouteProduct.RestArea && stopIds.Count == 0)
            {
                error = $"{seed.BillName}: Rest Area route {route.RouteName} has no stops";
                return null;
            }

            if (stopsByRoute.TryGetValue(route.Id, out var routeStops))
            {
                var valid = routeStops.Select(s => s.Id).ToHashSet();
                var unknown = stopIds.Where(id => !valid.Contains(id)).ToList();
                if (unknown.Count > 0)
                {
                    error = $"{seed.BillName}: stop ids {string.Join(",", unknown)} are not on {route.RouteName}";
                    return null;
                }
            }
        }

        return new SeedRow(seed, customer);
    }

    private static CustomerContract? FindContractForSeed(Customer customer, Sept2026ContractSeed seed)
    {
        var byName = customer.Contracts.FirstOrDefault(c =>
            string.Equals(c.ContractName.Trim(), seed.ContractName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (byName != null)
            return byName;

        var productMatch = FindMatchingProductContract(customer, seed.Product);
        if (productMatch != null)
            return productMatch;

        if (seed.Product == RouteProduct.Exits)
        {
            return customer.Contracts.FirstOrDefault(c =>
                !c.ContractRoutes.Any()
                && (c.ContractName.Contains("Exits", StringComparison.OrdinalIgnoreCase)
                    || c.ContractName.Contains("hotel", StringComparison.OrdinalIgnoreCase)));
        }

        return null;
    }

    private static Models.Route? ResolveRoute(Sept2026ContractRouteSeed routeSeed, IEnumerable<Models.Route> routes)
    {
        var list = routes as IList<Models.Route> ?? routes.ToList();
        return list.FirstOrDefault(r => string.Equals(r.RouteName, routeSeed.RouteName, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(r => RouteNaming.NamesMatch(r.RouteName, routeSeed.RouteName))
            ?? list.FirstOrDefault(r => r.Id == routeSeed.RouteId);
    }

    private static bool HasMatchingProductContract(Customer customer, RouteProduct product) =>
        FindMatchingProductContract(customer, product) != null;

    private static CustomerContract? FindMatchingProductContract(Customer customer, RouteProduct product)
    {
        foreach (var contract in customer.Contracts)
        {
            var products = contract.ContractRoutes
                .Select(cr => RouteProductHelper.FromRouteName(cr.Route?.RouteName))
                .Distinct()
                .ToList();
            if (products.Count == 1 && products[0] == product)
                return contract;
        }

        return null;
    }

    private async Task<string> CreateContractAsync(
        SeedRow row,
        Sept2026ContractFile file,
        Dictionary<int, List<Stop>> stopsByRoute,
        CancellationToken cancellationToken)
    {
        var contract = new CustomerContract
        {
            CustomerId = row.Customer.Id,
            ContractName = await UniqueNameAsync(row.Customer.Id, row.Seed.ContractName, cancellationToken),
            Term = row.Seed.Term,
            BillingMonthCount = AnnualBillingHelper.MonthsInTerm(row.Seed.Term),
            NextBillDate = file.NextBillDate,
            ContractEndDate = file.ContractEndDate,
            BillingAnchorMonth = file.NextBillDate.Month,
            ServiceMonthMask = SubscribedMonths.BuildMask(row.Seed.Months),
            Notes = row.Seed.Notes
        };
        _context.CustomerContracts.Add(contract);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var routeSeed in row.Seed.Routes)
        {
            var route = ResolveRoute(routeSeed, await _context.Routes.ToListAsync(cancellationToken));
            if (route == null)
                continue;
            _context.CustomerContractRoutes.Add(new CustomerContractRoute
            {
                CustomerContractId = contract.Id,
                RouteId = route.Id,
                BillingAmount = routeSeed.Amount
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await UpsertCustomerRoutesAsync(row, await _context.Routes.ToListAsync(cancellationToken), stopsByRoute, cancellationToken);
        _logger.LogInformation("Created {Contract} for customer {CustomerId}", contract.ContractName, row.Customer.Id);
        return $"{row.Label} → #{contract.Id} {contract.ContractName} ${row.Seed.ExpectedTotal:0.00}";
    }

    private async Task<string> UniqueNameAsync(int customerId, string requested, CancellationToken cancellationToken)
    {
        var name = requested.Trim();
        var existing = await _context.CustomerContracts.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => c.ContractName)
            .ToListAsync(cancellationToken);

        if (!existing.Any(n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return name;

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{name} ({suffix})";
            if (!existing.Any(n => string.Equals(n.Trim(), candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{name} ({DateTime.UtcNow:HHmmss})";
    }

    private async Task UpsertCustomerRoutesAsync(
        SeedRow row,
        IReadOnlyList<Models.Route> routes,
        Dictionary<int, List<Stop>> stopsByRoute,
        CancellationToken cancellationToken)
    {
        var existing = await _context.CustomerRoutes
            .Include(cr => cr.CustomerRouteStops)
            .Where(cr => cr.CustomerId == row.Customer.Id)
            .ToListAsync(cancellationToken);

        foreach (var routeSeed in row.Seed.Routes)
        {
            var route = ResolveRoute(routeSeed, routes);
            if (route == null)
                continue;

            var customerRoute = existing.FirstOrDefault(cr => cr.RouteId == route.Id);
            if (customerRoute == null)
            {
                customerRoute = new CustomerRoute
                {
                    CustomerId = row.Customer.Id,
                    RouteId = route.Id
                };
                _context.CustomerRoutes.Add(customerRoute);
                existing.Add(customerRoute);
            }

            var stopIds = (routeSeed.StopIds ?? new List<int>()).Distinct().ToList();
            var routeStops = stopsByRoute.GetValueOrDefault(route.Id) ?? new List<Stop>();
            var allStops = row.Product == RouteProduct.Exits
                || (stopIds.Count > 0 && routeStops.Count > 0 && stopIds.Count == routeStops.Count && stopIds.All(id => routeStops.Any(s => s.Id == id)));

            customerRoute.Status = CustomerRouteStatus.Active;
            AnnualBillingHelper.ApplyBillingMonths(customerRoute, AnnualBillingHelper.MonthsInTerm(row.Seed.Term));
            customerRoute.SubscribedMonthMask = SubscribedMonths.BuildMask(row.Seed.Months);
            customerRoute.RatePerMonth = AnnualBillingHelper.BillingPeriodAmountToMonthlyRate(routeSeed.Amount, row.Seed.Term);
            customerRoute.AllStops = allStops;

            await _context.SaveChangesAsync(cancellationToken);

            var currentStops = await _context.CustomerRouteStops
                .Where(crs => crs.CustomerRouteId == customerRoute.Id)
                .ToListAsync(cancellationToken);
            _context.CustomerRouteStops.RemoveRange(currentStops);

            if (!allStops)
            {
                foreach (var stopId in stopIds)
                {
                    _context.CustomerRouteStops.Add(new CustomerRouteStop
                    {
                        CustomerRouteId = customerRoute.Id,
                        StopId = stopId
                    });
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private sealed record SeedRow(Sept2026ContractSeed Seed, Customer Customer)
    {
        public RouteProduct Product => Seed.Product;
        public string Label => $"{Seed.Product} · {Seed.BillName} (#{Customer.Id} {Customer.CustomerName}) ${Seed.ExpectedTotal:0.00}";
    }
}

public class Sept2026ContractFile
{
    public DateOnly NextBillDate { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public List<Sept2026ContractSeed> Contracts { get; set; } = new();
}

public class Sept2026ContractSeed
{
    public string BillName { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string ExpectedCustomerName { get; set; } = string.Empty;
    public RouteProduct Product { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public BillingFrequency Term { get; set; }
    public List<int> Months { get; set; } = new();
    public string? Notes { get; set; }
    public decimal ExpectedTotal { get; set; }
    public List<Sept2026ContractRouteSeed> Routes { get; set; } = new();
}

public class Sept2026ContractRouteSeed
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public List<int>? StopIds { get; set; }
    public List<int>? Locations { get; set; }
}

public class EmptyHotelContractFile
{
    public List<EmptyHotelContractSeed> Contracts { get; set; } = new();
}

public class EmptyHotelContractSeed
{
    public int CustomerId { get; set; }
    public int ContractId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public decimal AnnualHotel { get; set; }
    public List<EmptyHotelRouteSeed> Routes { get; set; } = new();
}

public class EmptyHotelRouteSeed
{
    public string RouteName { get; set; } = string.Empty;
    public List<int> Months { get; set; } = new();
}

public class Sept2026ContractImportResult
{
    public bool DryRun { get; set; }
    public int SeedCount { get; set; }
    public List<string> WouldCreate { get; } = new();
    public List<string> WouldRepair { get; } = new();
    public List<string> Created { get; } = new();
    public List<string> Repaired { get; } = new();
    public List<string> Skipped { get; } = new();
    public List<string> Errors { get; } = new();
    public List<Sept2026ContractVerifyItem> Verifications { get; } = new();
}

public class Sept2026ContractVerifyItem
{
    public string BillName { get; set; } = string.Empty;
    public RouteProduct Product { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? ContractId { get; set; }
    public string? ContractName { get; set; }
    public decimal ExpectedTotal { get; set; }
    public decimal ActualTotal { get; set; }
    public bool Passed { get; set; }
    public List<string> Failures { get; } = new();
}
