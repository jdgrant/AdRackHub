namespace AdRackHub.Models;

public static class BillingDueCalculator
{
    public static bool IsDue(CustomerContract contract, int year, int month) =>
        IsContractDue(contract, year, month);

    public static bool IsContractDue(CustomerContract contract, int year, int month)
    {
        if (month is < 1 or > 12)
            return false;

        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Print-only contracts have no next bill date and are never due.
        if (contract.NextBillDate is not { } nextBill)
            return false;

        // Due only when NextBillDate falls in the selected billing period (date picker).
        if (nextBill < periodStart || nextBill > periodEnd)
            return false;

        return WouldBeDueOn(contract, nextBill);
    }

    public static bool WouldBeDueOn(CustomerContract contract, DateOnly billDate, bool ignoreEndDate = false)
    {
        if (contract.IsProposal)
            return false;

        if (!contract.ContractRoutes.Any())
            return false;

        if (!ignoreEndDate)
        {
            var periodStart = new DateOnly(billDate.Year, billDate.Month, 1);
            if (contract.ContractEndDate.HasValue && contract.ContractEndDate.Value < periodStart)
                return false;
        }

        return HasServiceForBillingPeriod(
            contract.ServiceMonthMask,
            AnnualBillingHelper.BillingMonths(contract),
            billDate);
    }

    public static bool HasServiceForBillingPeriod(int serviceMonthMask, int monthCount, DateOnly billDate)
    {
        if (serviceMonthMask == 0)
            return false;

        var months = GetBillingPeriodMonths(monthCount, billDate);
        if (months.Any(m => IsMonthInService(serviceMonthMask, m)))
            return true;

        // Pre-season bill: NextBillDate is the month immediately before the next selected service month.
        return IsMonthInService(serviceMonthMask, billDate.AddMonths(1).Month);
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
        SnapToMonthBeforeNextService(
            AdvanceNextBillDate(current, AnnualBillingHelper.BillingMonths(contract)),
            contract.ServiceMonthMask);

    public static DateOnly RewindNextBillDate(DateOnly current, int monthCount) =>
        current.AddMonths(-Math.Max(monthCount, 1));

    public static DateOnly RewindNextBillDate(DateOnly current, CustomerContract contract) =>
        RewindToServiceMonth(
            RewindNextBillDate(current, AnnualBillingHelper.BillingMonths(contract)),
            contract.ServiceMonthMask);

    public static DateOnly SnapToMonthBeforeNextService(DateOnly date, int serviceMonthMask)
    {
        if (serviceMonthMask == 0 || IsMonthInService(serviceMonthMask, date.Month))
            return date;

        for (var i = 1; i <= 12; i++)
        {
            var nextSelected = date.AddMonths(i);
            if (IsMonthInService(serviceMonthMask, nextSelected.Month))
                return nextSelected.AddMonths(-1);
        }

        return date;
    }

    static DateOnly RewindToServiceMonth(DateOnly date, int serviceMonthMask)
    {
        if (serviceMonthMask == 0 || IsMonthInService(serviceMonthMask, date.Month))
            return date;

        for (var i = 1; i <= 12; i++)
        {
            var previousSelected = date.AddMonths(-i);
            if (IsMonthInService(serviceMonthMask, previousSelected.Month))
                return previousSelected;
        }

        return date;
    }

    public static int InclusiveMonthCount(DateOnly start, DateOnly end)
    {
        if (end < start)
            return 0;

        return (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
    }

    public static int ContractPeriodMonths(CustomerContract contract)
    {
        var start = contract.ContractStartDate ?? contract.NextBillDate;
        if (start.HasValue && contract.ContractEndDate.HasValue)
            return Math.Max(InclusiveMonthCount(start.Value, contract.ContractEndDate.Value), 1);

        return AnnualBillingHelper.BillingMonths(contract);
    }

    public static bool IsActiveContract(CustomerContract contract, DateOnly? asOf = null)
    {
        if (contract.IsProposal)
            return false;

        asOf ??= DateOnly.FromDateTime(DateTime.Today);
        return !contract.ContractEndDate.HasValue || contract.ContractEndDate.Value >= asOf.Value;
    }

    public static string FormatContractEndDate(DateOnly? endDate) =>
        endDate?.ToString("MMM d, yyyy") ?? "Never";

    /// <summary>
    /// Renewal term for a duplicated contract. Same months one year later.
    /// An expired contract starts on the first of the current month and ends
    /// in its old end month next calendar year.
    /// </summary>
    public static (DateOnly? Start, DateOnly? End) NextDuplicateTerm(DateOnly? start, DateOnly? end, DateOnly today)
    {
        var expired = end.HasValue && end.Value < today;
        if (expired)
        {
            return (
                new DateOnly(today.Year, today.Month, 1),
                SameDayInYear(end!.Value, today.Year + 1));
        }

        return (
            start.HasValue ? SameDayInYear(start.Value, start.Value.Year + 1) : null,
            end.HasValue ? SameDayInYear(end.Value, end.Value.Year + 1) : null);
    }

    private static DateOnly SameDayInYear(DateOnly date, int year)
    {
        var day = Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month));
        return new DateOnly(year, date.Month, day);
    }

    public static string PeriodLabel(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMMM yyyy");
}
