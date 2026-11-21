using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class ContractHistoryKpiService
{
    private static readonly string[] MonthLabels =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly string[] RouteOrder =
    [
        "Exit - Northeast OH",
        "Exit - Central OH",
        "Exit - Northern Interstate",
        "Exit - Cincinnati-NKY",
        "Exit - Louisville",
        "Exit - Lex-Frankfort",
        "Exit - I-75",
        "Exit - I-65 & 24",
        "Exit - Mid-TN",
        "Rest Area - I-64 East of Lexington",
        "Rest Area - I-64 West & I-71",
        "Rest Area - I-75",
        "Rest Area - I-65"
    ];

    private readonly ApplicationDbContext _context;

    public ContractHistoryKpiService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ContractHistoryKpiPage> BuildAsync(string? product, CancellationToken cancellationToken = default)
    {
        var selectedProduct = product?.ToLowerInvariant() switch
        {
            "hotel" => "hotel",
            "rest" => "rest",
            _ => "all"
        };
        var lines = await _context.ContractHistoryLines
            .AsNoTracking()
            .Where(line => line.RouteName != null && line.RouteName != "")
            .Select(line => new HistoryLine(
                line.CustomerId,
                line.ContractName,
                line.Product,
                line.RouteId,
                line.RouteName!,
                line.MonthlyAmount,
                line.StartDate,
                line.EndDate))
            .ToListAsync(cancellationToken);

        var routeClients = new Dictionary<(string Route, int Year, int Month), HashSet<string>>();
        var routeRevenue = new Dictionary<(string Route, int Year, int Month), decimal>();
        var routeYearClients = new Dictionary<(string Route, int Year), HashSet<string>>();
        var allClients = new Dictionary<(int Year, int Month), HashSet<string>>();
        var allYearClients = new Dictionary<int, HashSet<string>>();
        var names = new Dictionary<string, string>();
        var years = new HashSet<int>();

        foreach (var line in lines)
        {
            if (!MatchesProduct(line.Product, selectedProduct))
                continue;
            if (line.StartDate is not DateOnly start || line.EndDate is not DateOnly end || end < start)
                continue;

            var routeKey = line.RouteId?.ToString() ?? line.RouteName;
            names[routeKey] = line.RouteName;
            var clientKey = line.CustomerId is int customerId
                ? $"id:{customerId}"
                : $"name:{line.ContractName.Trim().ToUpperInvariant()}";
            var amount = line.MonthlyAmount ?? 0m;
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            var guard = 0;
            while (cursor <= last && guard < 36)
            {
                if (cursor.Year < HubContractFromYear)
                    Record(routeKey, line.RouteName, clientKey, amount, cursor.Year, cursor.Month);
                else if (cursor.Year == HubContractFromYear)
                    Record(routeKey, line.RouteName, clientKey, amount, Archive2026Key, cursor.Month);
                cursor = cursor.AddMonths(1);
                guard++;
            }
        }

        await AddHubContractsAsync(
            selectedProduct,
            Record,
            cancellationToken);

        var orderedYears = years.Where(year => year is >= 2000 and <= 2100).OrderBy(year => year).ToList();
        var columns = new List<ContractHistoryKpiColumn>();
        foreach (var year in orderedYears)
        {
            columns.Add(new ContractHistoryKpiColumn
            {
                Key = year,
                Label = year == HubContractFromYear ? "2026 Hub" : year.ToString()
            });
            if (year == HubContractFromYear)
                columns.Add(new ContractHistoryKpiColumn { Key = Archive2026Key, Label = "2026 archive", Debug = true });
        }
        var columnKeys = columns.Select(column => column.Key).ToList();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var latest = orderedYears.Contains(today.Year) ? today.Year : orderedYears.LastOrDefault();
        var prior = orderedYears.Contains(latest - 1) ? latest - 1 : (int?)null;
        var page = new ContractHistoryKpiPage
        {
            Product = selectedProduct,
            Years = orderedYears,
            Columns = columns,
            LatestYear = latest,
            PriorYear = prior,
            LinesWithoutDates = lines.Count(line => line.StartDate == null || line.EndDate == null || line.EndDate < line.StartDate),
            Months = Enumerable.Range(1, 12)
                .Select(month => MonthRow(month, MonthLabels[month - 1], columnKeys, latest, prior, allClients, SumRevenue(routeRevenue, columnKeys, month)))
                .ToList()
        };
        page.YearTotal = YearRow(columnKeys, latest, prior, allYearClients, routeRevenue, null);
        page.Routes = names
            .Select(pair =>
            {
                var routeClientsForYear = routeYearClients
                    .Where(item => item.Key.Route == pair.Key)
                    .ToDictionary(item => item.Key.Year, item => item.Value);
                return new ContractHistoryKpiRouteRow
                {
                    RouteName = pair.Value,
                    Months = Enumerable.Range(1, 12)
                        .Select(month => MonthRow(
                            month,
                            MonthLabels[month - 1],
                            columnKeys,
                            latest,
                            prior,
                            allClients: null,
                            revenueByYear: columnKeys.ToDictionary(
                                year => year,
                                year => routeRevenue.GetValueOrDefault((pair.Key, year, month))),
                            routeClients: routeClients,
                            routeKey: pair.Key))
                        .ToList(),
                    YearTotal = YearRow(columnKeys, latest, prior, routeClientsForYear, routeRevenue, pair.Key)
                };
            })
            .Where(row => row.YearTotal.Years.Any(cell => cell.Clients > 0 || cell.Revenue > 0))
            .OrderBy(row => OrderOf(row.RouteName))
            .ThenBy(row => row.RouteName)
            .ToList();
        return page;

        void Record(string routeKey, string routeName, string clientKey, decimal amount, int year, int month)
        {
            names[routeKey] = routeName;
            years.Add(year);
            AddClient(routeClients, (routeKey, year, month), clientKey);
            routeRevenue[(routeKey, year, month)] = routeRevenue.GetValueOrDefault((routeKey, year, month)) + amount;
            AddClient(routeYearClients, (routeKey, year), clientKey);
            AddClient(allClients, (year, month), clientKey);
            AddClient(allYearClients, year, clientKey);
        }
    }

    private const int HubContractFromYear = 2026;
    private const int HubContractToYear = 2027;
    private const int Archive2026Key = 20261;

    private async Task AddHubContractsAsync(
        string selectedProduct,
        Action<string, string, string, decimal, int, int> record,
        CancellationToken cancellationToken)
    {
        var contracts = await _context.CustomerContracts
            .AsNoTracking()
            .Include(contract => contract.Customer)
            .Include(contract => contract.ContractRoutes)
                .ThenInclude(contractRoute => contractRoute.Route)
            .Where(contract => !contract.IsProposal)
            .Where(contract => contract.Customer.Type == CustomerType.Customer)
            .Where(contract => contract.Customer.Status == CustomerStatus.Active)
            .Where(contract => contract.ContractRoutes.Any())
            .ToListAsync(cancellationToken);

        foreach (var contract in contracts)
        {
            var clientKey = $"id:{contract.CustomerId}";
            foreach (var contractRoute in contract.ContractRoutes)
            {
                var routeName = contractRoute.Route?.RouteName;
                if (string.IsNullOrWhiteSpace(routeName) || !MatchesProduct(ProductFor(routeName), selectedProduct))
                    continue;

                var monthly = AnnualBillingHelper.BillingPeriodAmountToMonthlyRate(
                    AnnualBillingHelper.GetBillingAmount(contractRoute),
                    contract);
                if (monthly <= 0)
                    continue;

                for (var year = HubContractFromYear; year <= HubContractToYear; year++)
                {
                    for (var month = 1; month <= 12; month++)
                    {
                        if (!IsRunning(contract, year, month))
                            continue;
                        record(contractRoute.RouteId.ToString(), routeName, clientKey, monthly, year, month);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A Hub contract with no end date is running. A beginning date does not turn it off.
    /// A contract with an end date stops after that date. The month also has to be a service month.
    /// </summary>
    private static bool IsRunning(CustomerContract contract, int year, int month)
    {
        var monthStart = new DateOnly(year, month, 1);
        return BillingDueCalculator.IsActiveContract(contract, monthStart)
            && BillingDueCalculator.IsMonthInService(contract.ServiceMonthMask, month);
    }

    private static string ProductFor(string routeName) =>
        routeName.Contains("Rest Area", StringComparison.OrdinalIgnoreCase) ? "Rest Area" : "Hotel";

    private static ContractHistoryKpiMonthRow MonthRow(
        int month,
        string label,
        List<int> years,
        int latest,
        int? prior,
        Dictionary<(int Year, int Month), HashSet<string>>? allClients,
        Dictionary<int, decimal> revenueByYear,
        Dictionary<(string Route, int Year, int Month), HashSet<string>>? routeClients = null,
        string? routeKey = null)
    {
        var cells = years.Select(year => new ContractHistoryKpiCell
        {
            Clients = routeKey == null
                ? allClients?.GetValueOrDefault((year, month))?.Count ?? 0
                : routeClients?.GetValueOrDefault((routeKey, year, month))?.Count ?? 0,
            Revenue = revenueByYear.GetValueOrDefault(year)
        }).ToList();
        var row = new ContractHistoryKpiMonthRow
        {
            Month = month,
            Label = label,
            Years = cells
        };
        ApplyChange(row, cells, years, latest, prior);
        return row;
    }

    private static Dictionary<int, decimal> SumRevenue(
        Dictionary<(string Route, int Year, int Month), decimal> routeRevenue,
        List<int> years,
        int month)
    {
        return years.ToDictionary(
            year => year,
            year => routeRevenue.Where(item => item.Key.Year == year && item.Key.Month == month).Sum(item => item.Value));
    }

    private static ContractHistoryKpiMonthRow YearRow(
        List<int> years,
        int latest,
        int? prior,
        Dictionary<int, HashSet<string>> yearClients,
        Dictionary<(string Route, int Year, int Month), decimal> routeRevenue,
        string? routeKey)
    {
        var cells = years.Select(year => new ContractHistoryKpiCell
        {
            Clients = yearClients.GetValueOrDefault(year)?.Count ?? 0,
            Revenue = routeRevenue
                .Where(item => item.Key.Year == year && (routeKey == null || item.Key.Route == routeKey))
                .Sum(item => item.Value)
        }).ToList();
        var row = new ContractHistoryKpiMonthRow
        {
            Label = "Year",
            Years = cells
        };
        ApplyChange(row, cells, years, latest, prior);
        return row;
    }

    private static void ApplyChange(
        ContractHistoryKpiMonthRow row,
        List<ContractHistoryKpiCell> cells,
        List<int> years,
        int latest,
        int? prior)
    {
        if (prior is not int previous || latest <= 0 || cells.Count == 0)
            return;
        var current = cells[years.IndexOf(latest)];
        var before = cells[years.IndexOf(previous)];
        if (current.Clients == 0 && before.Clients == 0 && current.Revenue == 0 && before.Revenue == 0)
            return;
        row.ClientChange = current.Clients - before.Clients;
        row.RevenueChange = current.Revenue - before.Revenue;
    }

    private static void AddClient<TKey>(Dictionary<TKey, HashSet<string>> sets, TKey key, string client) where TKey : notnull
    {
        if (!sets.TryGetValue(key, out var clients))
        {
            clients = new HashSet<string>();
            sets[key] = clients;
        }
        clients.Add(client);
    }

    private static bool MatchesProduct(string? product, string selected)
    {
        if (selected == "all")
            return true;
        var normalized = product?.Contains("rest", StringComparison.OrdinalIgnoreCase) == true ? "rest" : "hotel";
        return normalized == selected;
    }

    private static int OrderOf(string routeName)
    {
        var index = Array.FindIndex(RouteOrder, name => string.Equals(name, routeName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? RouteOrder.Length : index;
    }

    private sealed record HistoryLine(
        int? CustomerId,
        string ContractName,
        string? Product,
        int? RouteId,
        string RouteName,
        decimal? MonthlyAmount,
        DateOnly? StartDate,
        DateOnly? EndDate);
}
