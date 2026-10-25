using AdRackHub.Models;

namespace AdRackHub.Services;

public interface IAgentBillingApi
{
    Task<AgentCallResult<AgentBillingDueDto>> DueAsync(
        int? year,
        int? month,
        string? search,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken);

    Task<AgentCallResult<AgentBillingOutstandingDto>> OutstandingAsync(
        string? search,
        string? age,
        int? weeks,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken);

    Task<AgentCallResult<AgentBillingForecastDto>> ForecastAsync(
        int? year,
        int? month,
        int? months,
        bool ignoreEndDate,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken);
}

public class AgentBillingService : IAgentBillingApi
{
    private readonly MonthlyBillingService _billing;

    public AgentBillingService(MonthlyBillingService billing)
    {
        _billing = billing;
    }

    public async Task<AgentCallResult<AgentBillingDueDto>> DueAsync(
        int? year,
        int? month,
        string? search,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken)
    {
        if (!TryPeriod(year, month, out var selectedYear, out var selectedMonth, out var error))
            return AgentCallResult<AgentBillingDueDto>.Invalid([error]);

        (page, pageSize) = Page(page, pageSize);
        var hub = hubBase.TrimEnd('/');
        var due = await _billing.GetDueContractsAsync(selectedYear, selectedMonth, cancellationToken);
        var submitted = await _billing.GetSubmittedInvoicesAsync(selectedYear, selectedMonth, cancellationToken);

        var items = due
            .GroupBy(item => item.CustomerContractId)
            .Select(group =>
            {
                var first = group.First();
                return new AgentBillingDueItemDto
                {
                    CustomerId = first.CustomerId,
                    CustomerName = first.CustomerName,
                    ContractId = first.CustomerContractId,
                    ContractName = first.ContractName,
                    Term = BillingTermDisplay.Label(first.BillingMonthCount > 0 ? first.BillingMonthCount : AnnualBillingHelper.MonthsInTerm(first.Term)),
                    NextBillDate = first.NextBillDate.ToString("yyyy-MM-dd"),
                    Routes = group.Select(item => item.RouteName).Distinct().OrderBy(name => name).ToList(),
                    Amount = group.Sum(item => item.Amount),
                    HubUrl = $"{hub}/Customers/Details/{first.CustomerId}"
                };
            })
            .OrderBy(item => item.CustomerName)
            .ThenBy(item => item.ContractName)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            items = items.Where(item =>
                item.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.ContractName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Routes.Any(route => route.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        var pageItems = Slice(items, page, pageSize);
        return AgentCallResult<AgentBillingDueDto>.Ok(new AgentBillingDueDto
        {
            Year = selectedYear,
            Month = selectedMonth,
            PeriodLabel = BillingDueCalculator.PeriodLabel(selectedYear, selectedMonth),
            DueTotal = items.Sum(item => item.Amount),
            DueCustomerCount = items.Select(item => item.CustomerId).Distinct().Count(),
            DueContractCount = items.Count,
            Page = page,
            PageSize = pageSize,
            Total = items.Count,
            TotalPages = Pages(items.Count, pageSize),
            HasMore = page < Pages(items.Count, pageSize),
            HubUrl = $"{hub}/Billing?year={selectedYear}&month={selectedMonth}",
            Items = pageItems,
            SubmittedInvoices = submitted.Select(invoice => new AgentBillingInvoiceDto
            {
                InvoiceId = invoice.Id,
                CustomerId = invoice.CustomerId,
                CustomerName = invoice.Customer.CustomerName,
                Status = invoice.Status.ToString(),
                PeriodLabel = BillingDueCalculator.PeriodLabel(invoice.BillingRun.Year, invoice.BillingRun.Month),
                WaveInvoiceNumber = invoice.WaveInvoiceNumber,
                Amount = invoice.TotalAmount,
                ContractNames = string.Join(", ", invoice.Lines.Select(line => line.ContractName).Distinct().OrderBy(name => name)),
                HubUrl = $"{hub}/Customers/Details/{invoice.CustomerId}"
            }).ToList()
        });
    }

    public async Task<AgentCallResult<AgentBillingOutstandingDto>> OutstandingAsync(
        string? search,
        string? age,
        int? weeks,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken)
    {
        var selectedAge = age?.Trim();
        if (!string.IsNullOrWhiteSpace(selectedAge)
            && selectedAge is not ("under30" or "30-60" or "over60"))
            return AgentCallResult<AgentBillingOutstandingDto>.Invalid(["age must be under30, 30-60, or over60."]);

        if (weeks is < 0)
            return AgentCallResult<AgentBillingOutstandingDto>.Invalid(["weeks must be zero or greater."]);

        (page, pageSize) = Page(page, pageSize);
        var hub = hubBase.TrimEnd('/');
        var asOf = DateOnly.FromDateTime(DateTime.Today);
        var invoices = await _billing.GetUnreceivedInvoicesAsync(cancellationToken);
        var rows = invoices.Select(invoice =>
        {
            var invoiceDate = new DateOnly(invoice.BillingRun.Year, invoice.BillingRun.Month, 1);
            var daysLate = Math.Max(0, asOf.DayNumber - invoiceDate.DayNumber);
            return new AgentBillingOutstandingItemDto
            {
                InvoiceId = invoice.Id,
                CustomerId = invoice.CustomerId,
                CustomerName = invoice.Customer.CustomerName,
                PeriodLabel = BillingDueCalculator.PeriodLabel(invoice.BillingRun.Year, invoice.BillingRun.Month),
                InvoiceDate = invoiceDate.ToString("yyyy-MM-dd"),
                DaysLate = daysLate,
                WeeksLate = daysLate / 7,
                WaveInvoiceNumber = invoice.WaveInvoiceNumber,
                ContractNames = string.Join(", ", invoice.Lines.Select(line => line.ContractName).Distinct().OrderBy(name => name)),
                Amount = invoice.TotalAmount,
                HubUrl = $"{hub}/Customers/Details/{invoice.CustomerId}"
            };
        }).OrderByDescending(row => row.DaysLate).ThenBy(row => row.CustomerName).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(row =>
                row.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.ContractNames.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.PeriodLabel.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (row.WaveInvoiceNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || row.InvoiceId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var under30 = Bucket(rows, days => days < 30);
        var from30To60 = Bucket(rows, days => days is >= 30 and <= 60);
        var over60 = Bucket(rows, days => days > 60);
        if (weeks is > 0)
            rows = rows.Where(row => row.WeeksLate >= weeks.Value).ToList();
        rows = selectedAge switch
        {
            "under30" => rows.Where(row => row.DaysLate < 30).ToList(),
            "30-60" => rows.Where(row => row.DaysLate is >= 30 and <= 60).ToList(),
            "over60" => rows.Where(row => row.DaysLate > 60).ToList(),
            _ => rows
        };

        return AgentCallResult<AgentBillingOutstandingDto>.Ok(new AgentBillingOutstandingDto
        {
            AsOf = asOf.ToString("yyyy-MM-dd"),
            TotalAmount = rows.Sum(row => row.Amount),
            InvoiceCount = rows.Count,
            ClientCount = rows.Select(row => row.CustomerId).Distinct().Count(),
            Under30 = under30,
            From30To60 = from30To60,
            Over60 = over60,
            Page = page,
            PageSize = pageSize,
            Total = rows.Count,
            TotalPages = Pages(rows.Count, pageSize),
            HasMore = page < Pages(rows.Count, pageSize),
            HubUrl = $"{hub}/Billing/Outstanding",
            Items = Slice(rows, page, pageSize)
        });
    }

    public async Task<AgentCallResult<AgentBillingForecastDto>> ForecastAsync(
        int? year,
        int? month,
        int? months,
        bool ignoreEndDate,
        int page,
        int pageSize,
        string hubBase,
        CancellationToken cancellationToken)
    {
        if (!TryPeriod(year, month, out var selectedYear, out var selectedMonth, out var error))
            return AgentCallResult<AgentBillingForecastDto>.Invalid([error]);

        var horizon = months ?? 12;
        if (horizon is < 1 or > 24)
            return AgentCallResult<AgentBillingForecastDto>.Invalid(["months must be from 1 to 24."]);

        (page, pageSize) = Page(page, pageSize);
        var hub = hubBase.TrimEnd('/');
        var today = DateTime.Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        var forecast = await _billing.GetForecastAsync(start, horizon, selectedYear, selectedMonth, ignoreEndDate, cancellationToken);
        var selected = forecast.FirstOrDefault(item => item.IsSelectedPeriod) ?? forecast.FirstOrDefault();
        var lines = (selected?.Lines ?? []).Select(line => new AgentBillingForecastLineDto
        {
            CustomerId = line.CustomerId,
            CustomerName = line.CustomerName,
            ContractId = line.ContractId,
            ContractName = line.ContractName,
            Term = line.TermLabel,
            ContractEndDate = line.ContractEndDate?.ToString("yyyy-MM-dd"),
            Amount = line.Amount,
            Sent = line.Sent,
            HubUrl = $"{hub}/Customers/Details/{line.CustomerId}"
        }).ToList();

        return AgentCallResult<AgentBillingForecastDto>.Ok(new AgentBillingForecastDto
        {
            ForecastTotal = forecast.Sum(item => item.Amount),
            ForecastInvoiceCount = forecast.Sum(item => item.InvoiceCount),
            SelectedYear = selected?.Year ?? selectedYear,
            SelectedMonth = selected?.Month ?? selectedMonth,
            SelectedPeriodLabel = BillingDueCalculator.PeriodLabel(selected?.Year ?? selectedYear, selected?.Month ?? selectedMonth),
            IgnoreEndDate = ignoreEndDate,
            HubUrl = $"{hub}/Billing/Forecast?year={selectedYear}&month={selectedMonth}",
            Months = forecast.Select(item => new AgentBillingForecastMonthDto
            {
                Year = item.Year,
                Month = item.Month,
                Label = item.Label,
                InvoiceCount = item.InvoiceCount,
                CustomerCount = item.CustomerCount,
                Amount = item.Amount,
                IsCurrentMonth = item.IsCurrentMonth,
                IsSelected = item.IsSelectedPeriod
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = lines.Count,
            TotalPages = Pages(lines.Count, pageSize),
            HasMore = page < Pages(lines.Count, pageSize),
            Lines = Slice(lines, page, pageSize)
        });
    }

    private static bool TryPeriod(int? year, int? month, out int selectedYear, out int selectedMonth, out string error)
    {
        var today = DateTime.Today;
        selectedYear = year ?? today.Year;
        selectedMonth = month ?? today.Month;
        error = "";
        if (year.HasValue != month.HasValue)
        {
            error = "Send both year and month, or omit both to use the current month.";
            return false;
        }

        if (selectedYear is < 2000 or > 2100 || selectedMonth is < 1 or > 12)
        {
            error = "year must be from 2000 to 2100 and month must be from 1 to 12.";
            return false;
        }

        return true;
    }

    private static AgentBillingAgingDto Bucket(IEnumerable<AgentBillingOutstandingItemDto> rows, Func<int, bool> match)
    {
        var matched = rows.Where(row => match(row.DaysLate)).ToList();
        return new AgentBillingAgingDto
        {
            Amount = matched.Sum(row => row.Amount),
            InvoiceCount = matched.Count
        };
    }

    private static (int Page, int PageSize) Page(int page, int pageSize)
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = AgentCustomerApiService.DefaultPageSize;
        if (pageSize > AgentCustomerApiService.MaxPageSize)
            pageSize = AgentCustomerApiService.MaxPageSize;
        return (page, pageSize);
    }

    private static int Pages(int total, int pageSize) =>
        total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

    private static List<T> Slice<T>(List<T> items, int page, int pageSize) =>
        items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
}
