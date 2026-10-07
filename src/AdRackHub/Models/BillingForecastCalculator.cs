namespace AdRackHub.Models;

public class BillingForecastLine
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int ContractId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public BillingFrequency Term { get; set; }
    public int BillingMonthCount { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public decimal Amount { get; set; }
    public bool Sent { get; set; }

    public string TermLabel =>
        BillingTermDisplay.Label(BillingMonthCount > 0 ? BillingMonthCount : AnnualBillingHelper.MonthsInTerm(Term));
}

public class BillingMonthForecast
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public int CustomerCount { get; set; }
    public decimal Amount { get; set; }
    public bool IsSelectedPeriod { get; set; }
    public bool IsCurrentMonth { get; set; }
    public List<BillingForecastLine> Lines { get; } = new();

    internal HashSet<int> CustomerIds { get; } = new();
}

public class SentForecastInvoice
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public int ContractId { get; init; }
    public string ContractName { get; init; } = string.Empty;
    public BillingFrequency Term { get; init; }
    public int BillingMonthCount { get; init; }
    public DateOnly? ContractEndDate { get; init; }
    public decimal Amount { get; init; }
}

public static class BillingForecastCalculator
{
    public static IReadOnlyList<BillingMonthForecast> Build(
        IEnumerable<CustomerContract> contracts,
        DateOnly fromMonth,
        int months,
        int selectedYear,
        int selectedMonth,
        IEnumerable<SentForecastInvoice>? sentInvoices = null,
        bool ignoreEndDate = false)
    {
        months = Math.Clamp(months, 1, 24);
        var start = new DateOnly(fromMonth.Year, fromMonth.Month, 1);
        var horizon = start.AddMonths(months);

        var buckets = Enumerable.Range(0, months).Select(i =>
        {
            var date = start.AddMonths(i);
            return new BillingMonthForecast
            {
                Year = date.Year,
                Month = date.Month,
                Label = date.ToString("MMM yyyy"),
                IsSelectedPeriod = date.Year == selectedYear && date.Month == selectedMonth,
                IsCurrentMonth = date.Year == start.Year && date.Month == start.Month
            };
        }).ToList();

        var index = buckets.ToDictionary(b => (b.Year, b.Month));

        foreach (var sent in sentInvoices ?? Enumerable.Empty<SentForecastInvoice>())
        {
            if (!index.TryGetValue((sent.Year, sent.Month), out var bucket))
                continue;

            AddLine(bucket, new BillingForecastLine
            {
                CustomerId = sent.CustomerId,
                CustomerName = sent.CustomerName,
                ContractId = sent.ContractId,
                ContractName = sent.ContractName,
                Term = sent.Term,
                BillingMonthCount = sent.BillingMonthCount,
                ContractEndDate = sent.ContractEndDate,
                Amount = sent.Amount,
                Sent = true
            });
        }

        foreach (var contract in contracts)
        {
            if (!BillingDueCalculator.IsActiveContract(contract, start))
                continue;

            if (contract.Customer is { Type: not CustomerType.Customer })
                continue;

            var amount = contract.ContractRoutes.Sum(AnnualBillingHelper.GetBillingAmount);
            if (amount <= 0m)
                continue;

            var customerName = contract.Customer?.CustomerName ?? "";
            AddScheduledDates(contract, amount, customerName, start, horizon, index, ignoreEndDate);
        }

        foreach (var bucket in buckets)
        {
            bucket.CustomerCount = bucket.CustomerIds.Count;
            bucket.Lines.Sort((a, b) =>
            {
                var name = string.Compare(a.CustomerName, b.CustomerName, StringComparison.OrdinalIgnoreCase);
                return name != 0 ? name : string.Compare(a.ContractName, b.ContractName, StringComparison.OrdinalIgnoreCase);
            });
        }

        return buckets;
    }

    static void AddScheduledDates(
        CustomerContract contract,
        decimal amount,
        string customerName,
        DateOnly start,
        DateOnly horizon,
        Dictionary<(int Year, int Month), BillingMonthForecast> index,
        bool ignoreEndDate)
    {
        void consider(DateOnly date)
        {
            if (date < start || date >= horizon)
                return;
            if (!BillingDueCalculator.WouldBeDueOn(contract, date, ignoreEndDate))
                return;
            if (!index.TryGetValue((date.Year, date.Month), out var bucket))
                return;

            AddLine(bucket, new BillingForecastLine
            {
                CustomerId = contract.CustomerId,
                CustomerName = customerName,
                ContractId = contract.Id,
                ContractName = contract.ContractName,
                Term = contract.Term,
                BillingMonthCount = AnnualBillingHelper.BillingMonths(contract),
                ContractEndDate = contract.ContractEndDate,
                Amount = amount,
                Sent = false
            });
        }

        if (contract.NextBillDate is not { } nextBill)
            return;

        var forward = nextBill;
        var guard = 0;
        while (forward < horizon && guard++ < 48)
        {
            consider(forward);
            if (!TryAdvance(forward, contract, out var advanced) || advanced >= horizon)
                break;
            forward = advanced;
        }

        if (nextBill < start)
            return;

        var backward = nextBill;
        guard = 0;
        while (guard++ < 48)
        {
            if (!TryRewind(backward, contract, out var previous))
                break;
            if (previous < start)
                break;
            consider(previous);
            backward = previous;
        }
    }

    static bool TryAdvance(DateOnly current, CustomerContract contract, out DateOnly next)
    {
        next = current;
        try
        {
            next = BillingDueCalculator.AdvanceNextBillDate(current, contract);
            return next > current;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool TryRewind(DateOnly current, CustomerContract contract, out DateOnly previous)
    {
        previous = current;
        try
        {
            previous = BillingDueCalculator.RewindNextBillDate(current, contract);
            return previous < current;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static void AddLine(BillingMonthForecast bucket, BillingForecastLine line)
    {
        if (bucket.Lines.Any(existing => existing.ContractId == line.ContractId))
            return;

        bucket.Lines.Add(line);
        bucket.Amount += line.Amount;
        bucket.InvoiceCount++;
        bucket.CustomerIds.Add(line.CustomerId);
    }
}
