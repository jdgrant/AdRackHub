namespace AdRackHub.Models;

public static class BillingDueCalculator
{
    public static bool IsDue(CustomerContract contract, int year, int month) =>
        IsContractDue(contract, year, month);

    public static bool IsContractDue(CustomerContract contract, int year, int month)
    {
        if (month is < 1 or > 12)
            return false;

        if (!contract.ContractRoutes.Any())
            return false;

        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        if (contract.ContractEndDate.HasValue && contract.ContractEndDate.Value < periodStart)
            return false;

        // Due only when NextBillDate falls in the selected billing period (date picker).
        if (contract.NextBillDate < periodStart || contract.NextBillDate > periodEnd)
            return false;

        if (!HasServiceForBillingPeriod(contract.ServiceMonthMask, contract.Term, contract.NextBillDate))
            return false;

        return true;
    }

    public static bool HasServiceForBillingPeriod(int serviceMonthMask, BillingFrequency term, DateOnly billDate)
    {
        var months = GetBillingPeriodMonths(term, billDate);
        return months.Any(m => IsMonthInService(serviceMonthMask, m));
    }

    public static IEnumerable<int> GetBillingPeriodMonths(BillingFrequency term, DateOnly billDate) =>
        term switch
        {
            BillingFrequency.Monthly => new[] { billDate.Month },
            BillingFrequency.Quarterly => Enumerable.Range(0, 3).Select(i => ((billDate.Month - 1 + i) % 12) + 1),
            BillingFrequency.Annual => Enumerable.Range(0, 12).Select(i => ((billDate.Month - 1 + i) % 12) + 1),
            _ => Array.Empty<int>()
        };

    public static bool IsMonthInService(int serviceMonthMask, int month) =>
        month is >= 1 and <= 12 && (serviceMonthMask & (1 << (month - 1))) != 0;

    public static DateOnly AdvanceNextBillDate(DateOnly current, BillingFrequency term) => term switch
    {
        BillingFrequency.Monthly => current.AddMonths(1),
        BillingFrequency.Quarterly => current.AddMonths(3),
        BillingFrequency.Annual => current.AddYears(1),
        _ => current.AddMonths(1)
    };

    public static bool IsActiveContract(CustomerContract contract, DateOnly? asOf = null)
    {
        asOf ??= DateOnly.FromDateTime(DateTime.Today);
        return !contract.ContractEndDate.HasValue || contract.ContractEndDate.Value >= asOf.Value;
    }

    public static string FormatContractEndDate(DateOnly? endDate) =>
        endDate?.ToString("MMM d, yyyy") ?? "Never";

    public static string PeriodLabel(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMMM yyyy");
}
