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

        if (!HasServiceForBillingPeriod(contract.ServiceMonthMask, AnnualBillingHelper.BillingMonths(contract), contract.NextBillDate))
            return false;

        return true;
    }

    public static bool HasServiceForBillingPeriod(int serviceMonthMask, int monthCount, DateOnly billDate)
    {
        var months = GetBillingPeriodMonths(monthCount, billDate);
        return months.Any(m => IsMonthInService(serviceMonthMask, m));
    }

    public static bool HasServiceForBillingPeriod(int serviceMonthMask, BillingFrequency term, DateOnly billDate) =>
        HasServiceForBillingPeriod(serviceMonthMask, AnnualBillingHelper.MonthsInTerm(term), billDate);

    public static IEnumerable<int> GetBillingPeriodMonths(int monthCount, DateOnly billDate) =>
        Enumerable.Range(0, Math.Max(monthCount, 1)).Select(i => ((billDate.Month - 1 + i) % 12) + 1);

    public static IEnumerable<int> GetBillingPeriodMonths(BillingFrequency term, DateOnly billDate) =>
        GetBillingPeriodMonths(AnnualBillingHelper.MonthsInTerm(term), billDate);

    public static bool IsMonthInService(int serviceMonthMask, int month) =>
        month is >= 1 and <= 12 && (serviceMonthMask & (1 << (month - 1))) != 0;

    public static DateOnly AdvanceNextBillDate(DateOnly current, int monthCount) =>
        current.AddMonths(Math.Max(monthCount, 1));

    public static DateOnly AdvanceNextBillDate(DateOnly current, BillingFrequency term) =>
        AdvanceNextBillDate(current, AnnualBillingHelper.MonthsInTerm(term));

    public static DateOnly AdvanceNextBillDate(DateOnly current, CustomerContract contract) =>
        AdvanceNextBillDate(current, AnnualBillingHelper.BillingMonths(contract));

    public static DateOnly RewindNextBillDate(DateOnly current, int monthCount) =>
        current.AddMonths(-Math.Max(monthCount, 1));

    public static int InclusiveMonthCount(DateOnly start, DateOnly end)
    {
        if (end < start)
            return 0;

        return (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
    }

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
