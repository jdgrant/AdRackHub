using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class ProfitLossRevenueReportService
{
    public const string SalesCategory = "Sales";
    public const int ReceivedFromYear = 2026;
    public const int ReportFromMonth = 9;

    private static readonly string[] MonthLabels =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly (string Section, string Category, string Label)[] StatementLines =
    [
        (ProfitLossSections.Income, "Sales", "Sales"),
        (ProfitLossSections.Expense, "Total Expense", "Total expenses"),
        (ProfitLossSections.Result, "Net Income", "Net income")
    ];

    private readonly ApplicationDbContext _context;

    public ProfitLossRevenueReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProfitLossRevenuePage> BuildAsync(CancellationToken cancellationToken = default)
    {
        var history = await _context.ProfitLossHistoryLines
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var invoices = await _context.BillingRunInvoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == BillingRunInvoiceStatus.Received
                && invoice.BillingRun.Year >= ReceivedFromYear)
            .Select(invoice => new HubInvoice(
                invoice.BillingRun.Year,
                invoice.BillingRun.Month,
                invoice.TotalAmount))
            .ToListAsync(cancellationToken);

        var sales = history
            .Where(line => line.Section == ProfitLossSections.Income && line.Category == SalesCategory)
            .GroupBy(line => (line.Year, line.Month))
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Amount));
        var received = new Dictionary<(int Year, int Month), decimal>();
        foreach (var invoice in invoices)
        {
            if (!InReport(invoice.Year, invoice.Month))
                continue;
            var key = (invoice.Year, invoice.Month);
            received[key] = received.GetValueOrDefault(key) + invoice.Amount;
        }

        var years = history.Select(line => line.Year)
            .Concat(received.Keys.Select(key => key.Year))
            .Where(year => year >= ReceivedFromYear)
            .Distinct()
            .OrderBy(year => year)
            .ToList();
        var receivedYears = received.Keys.Select(key => key.Year).Distinct().ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var latest = years.Count == 0 ? today.Year : years[^1];
        var columns = years.Select(year => new ProfitLossRevenueColumn
        {
            Year = year,
            Latest = year == latest,
            IncludeYoy = Enumerable.Range(1, 12).Any(month => HasPriorComparison(year, month, sales, received)),
            IncludeHub = year >= ReceivedFromYear && receivedYears.Contains(year)
        }).ToList();

        var months = new List<ProfitLossRevenueMonthRow>();
        for (var month = 1; month <= 12; month++)
        {
            if (!years.Any(year => InReport(year, month)))
                continue;
            months.Add(new ProfitLossRevenueMonthRow
            {
                Month = month,
                Label = MonthLabels[month - 1],
                Years = columns.Select(column => MonthCell(column, month, sales, received)).ToList()
            });
        }

        var page = new ProfitLossRevenuePage
        {
            SourceFile = history.Select(line => line.SourceFile).FirstOrDefault(file => !string.IsNullOrWhiteSpace(file)),
            LineCount = history.Count,
            Years = years,
            Columns = columns,
            LatestYear = latest,
            CurrentMonth = years.Contains(today.Year) ? today.Month : 0,
            Months = months,
            Total = new ProfitLossRevenueMonthRow
            {
                Label = "Total",
                Years = columns.Select(column => TotalCell(column, sales, received)).ToList()
            },
            BillingOnlyMonths = received.Keys
                .Where(key => years.Contains(key.Year) && InReport(key.Year, key.Month) && !sales.ContainsKey(key))
                .OrderBy(key => key.Year)
                .ThenBy(key => key.Month)
                .Select(key => $"{MonthLabels[key.Month - 1]} {key.Year}")
                .ToList(),
            Statement = StatementLines
                .Select(line => StatementRow(line.Section, line.Category, line.Label, history, years))
                .ToList()
        };
        page.Summaries = page.Total.Years.Select(cell => new ProfitLossYearSummary
        {
            Year = cell.Year,
            Sales = cell.Sales,
            HasSales = cell.HasSales,
            PeriodLabel = cell.PeriodLabel,
            VersusPriorAmount = cell.YoyAmount,
            VersusPriorPercent = cell.YoyPercent,
            HasBilling = cell.HasBilling,
            Billed = cell.Billed,
            Received = cell.Received
        }).ToList();
        return page;
    }

    private static ProfitLossRevenueYearCell MonthCell(
        ProfitLossRevenueColumn column,
        int month,
        IReadOnlyDictionary<(int Year, int Month), decimal> sales,
        IReadOnlyDictionary<(int Year, int Month), decimal> received)
    {
        var hasSales = sales.TryGetValue((column.Year, month), out var amount);
        var cell = new ProfitLossRevenueYearCell
        {
            Year = column.Year,
            IncludeYoy = column.IncludeYoy,
            IncludeHub = column.IncludeHub,
            HasSales = hasSales,
            Sales = amount
        };
        if (column.IncludeHub && received.TryGetValue((column.Year, month), out var receivedAmount))
        {
            cell.HasBilling = true;
            cell.Received = receivedAmount;
        }

        var previous = month == 1 ? (column.Year - 1, 12) : (column.Year, month - 1);
        if (hasSales && InReport(previous.Item1, previous.Item2) && sales.TryGetValue(previous, out var priorMonth))
        {
            cell.HasMom = true;
            cell.MomAmount = amount - priorMonth;
            cell.MomPercent = ChangePercent(priorMonth, amount);
        }
        else if (!hasSales && cell.HasBilling && InReport(previous.Item1, previous.Item2)
            && received.TryGetValue(previous, out var priorReceived))
        {
            cell.HasMom = true;
            cell.MomAmount = cell.Received - priorReceived;
            cell.MomPercent = ChangePercent(priorReceived, cell.Received);
        }

        if (column.IncludeYoy && sales.TryGetValue((column.Year - 1, month), out var priorYear))
        {
            if (hasSales)
            {
                cell.HasYoy = true;
                cell.YoyAmount = amount - priorYear;
                cell.YoyPercent = ChangePercent(priorYear, amount);
            }
            else if (cell.HasBilling)
            {
                cell.HasYoy = true;
                cell.YoyAmount = cell.Received - priorYear;
                cell.YoyPercent = ChangePercent(priorYear, cell.Received);
                cell.ComparisonLabel = $"vs {column.Year - 1} sales";
            }
        }

        return cell;
    }

    private static ProfitLossRevenueYearCell TotalCell(
        ProfitLossRevenueColumn column,
        IReadOnlyDictionary<(int Year, int Month), decimal> sales,
        IReadOnlyDictionary<(int Year, int Month), decimal> received)
    {
        var monthsInRange = Enumerable.Range(1, 12).Where(month => InReport(column.Year, month)).ToList();
        var salesMonths = monthsInRange.Where(month => sales.ContainsKey((column.Year, month))).ToList();
        var receivedMonths = monthsInRange.Where(month => received.ContainsKey((column.Year, month))).ToList();
        var amount = salesMonths.Sum(month => sales[(column.Year, month)]);
        var receivedTotal = receivedMonths.Sum(month => received[(column.Year, month)]);
        var cell = new ProfitLossRevenueYearCell
        {
            Year = column.Year,
            IncludeYoy = column.IncludeYoy,
            IncludeHub = column.IncludeHub,
            HasSales = salesMonths.Count > 0,
            Sales = amount,
            HasBilling = receivedMonths.Count > 0,
            Received = receivedTotal,
            PeriodLabel = salesMonths.Count > 0
                ? salesMonths.Count < 12 ? MonthSpan(salesMonths) : null
                : receivedMonths.Count > 0 ? MonthSpan(receivedMonths) : null
        };
        if (column.IncludeYoy && salesMonths.Count > 0
            && salesMonths.All(month => sales.ContainsKey((column.Year - 1, month))))
        {
            var prior = salesMonths.Sum(month => sales[(column.Year - 1, month)]);
            cell.HasYoy = true;
            cell.YoyAmount = amount - prior;
            cell.YoyPercent = ChangePercent(prior, amount);
            if (cell.PeriodLabel != null)
                cell.ComparisonLabel = $"vs {column.Year - 1} {cell.PeriodLabel}";
        }
        else if (column.IncludeYoy && salesMonths.Count == 0 && receivedMonths.Count > 0
            && receivedMonths.All(month => sales.ContainsKey((column.Year - 1, month))))
        {
            var prior = receivedMonths.Sum(month => sales[(column.Year - 1, month)]);
            cell.HasYoy = true;
            cell.YoyAmount = receivedTotal - prior;
            cell.YoyPercent = ChangePercent(prior, receivedTotal);
            cell.ComparisonLabel = $"vs {column.Year - 1} {MonthSpan(receivedMonths)} sales";
        }

        return cell;
    }

    private static ProfitLossStatementRow StatementRow(
        string section,
        string category,
        string label,
        IReadOnlyList<ProfitLossHistoryLine> history,
        IReadOnlyList<int> years)
    {
        var byYear = history
            .Where(line => line.Section == section && line.Category == category)
            .GroupBy(line => line.Year)
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(line => line.Month).ToDictionary(month => month.Key, month => month.Sum(line => line.Amount)));
        var first = years.FirstOrDefault();
        return new ProfitLossStatementRow
        {
            Label = label,
            Years = years.Select(year =>
            {
                byYear.TryGetValue(year, out var months);
                months ??= new Dictionary<int, decimal>();
                var monthNumbers = months.Keys.Where(month => InReport(year, month)).OrderBy(month => month).ToList();
                var amount = monthNumbers.Sum(month => months[month]);
                var cell = new ProfitLossStatementCell
                {
                    Year = year,
                    HasAmount = monthNumbers.Count > 0,
                    Amount = amount,
                    PeriodLabel = monthNumbers.Count is > 0 and < 12 ? MonthSpan(monthNumbers) : null
                };
                if (year != first
                    && monthNumbers.Count > 0
                    && byYear.TryGetValue(year - 1, out var prior)
                    && monthNumbers.All(prior.ContainsKey))
                {
                    var baseline = monthNumbers.Sum(month => prior[month]);
                    cell.HasComparison = true;
                    cell.VersusPriorAmount = amount - baseline;
                    cell.VersusPriorPercent = ChangePercent(baseline, amount);
                }

                return cell;
            }).ToList()
        };
    }

    private static bool InReport(int year, int month) =>
        month is >= 1 and <= 12
        && (year > ReceivedFromYear || (year == ReceivedFromYear && month >= ReportFromMonth));

    private static bool HasPriorComparison(
        int year,
        int month,
        IReadOnlyDictionary<(int Year, int Month), decimal> sales,
        IReadOnlyDictionary<(int Year, int Month), decimal> received) =>
        InReport(year, month)
        && sales.ContainsKey((year - 1, month))
        && (sales.ContainsKey((year, month)) || received.ContainsKey((year, month)));

    internal static decimal? ChangePercent(decimal baseline, decimal current)
    {
        if (baseline == 0m)
            return current == 0m ? 0m : null;
        return (current - baseline) / baseline;
    }

    private static string MonthSpan(IReadOnlyList<int> months)
    {
        var ordered = months.OrderBy(month => month).ToList();
        var contiguous = ordered.Zip(ordered.Skip(1), (left, right) => right == left + 1).All(step => step);
        if (!contiguous)
            return string.Join(", ", ordered.Select(month => MonthLabels[month - 1]));
        if (ordered.Count == 1)
            return MonthLabels[ordered[0] - 1];
        return $"{MonthLabels[ordered[0] - 1]}–{MonthLabels[ordered[^1] - 1]}";
    }

    private readonly record struct HubInvoice(int Year, int Month, decimal Amount);
}
