namespace AdRackHub.Models;

public static class AnnualBillingHelper
{
    public static int MonthsInTerm(BillingFrequency term) => term switch
    {
        BillingFrequency.Monthly => 1,
        BillingFrequency.Quarterly => 3,
        BillingFrequency.EveryFourMonths => 4,
        BillingFrequency.Annual => 12,
        _ => 1
    };

    public static BillingFrequency TermFromMonthCount(int months) => months switch
    {
        1 => BillingFrequency.Monthly,
        3 => BillingFrequency.Quarterly,
        4 => BillingFrequency.EveryFourMonths,
        12 => BillingFrequency.Annual,
        _ => BillingFrequency.Monthly
    };

    public static int BillingMonths(CustomerContract contract) =>
        contract.BillingMonthCount > 0 ? contract.BillingMonthCount : MonthsInTerm(contract.Term);

    public static int BillingMonths(CustomerRoute route) =>
        route.BillingMonthCount > 0 ? route.BillingMonthCount : MonthsInTerm(route.BillingTerm);

    public static void ApplyBillingMonths(CustomerContract contract, int months)
    {
        contract.BillingMonthCount = Math.Clamp(months, 1, 36);
        contract.Term = TermFromMonthCount(contract.BillingMonthCount);
    }

    public static void ApplyBillingMonths(CustomerRoute route, int months)
    {
        route.BillingMonthCount = Math.Clamp(months, 1, 36);
        route.BillingTerm = TermFromMonthCount(route.BillingMonthCount);
    }

    public static decimal ToRatePerMonth(decimal annualPrice) =>
        Math.Round(annualPrice / 12m, 2, MidpointRounding.AwayFromZero);

    public static decimal ToBillingPeriodAmount(decimal annualPrice, int months) =>
        Math.Round(ToRatePerMonth(annualPrice) * Math.Max(months, 1), 2, MidpointRounding.AwayFromZero);

    public static decimal ToBillingPeriodAmount(decimal annualPrice, BillingFrequency term) =>
        ToBillingPeriodAmount(annualPrice, MonthsInTerm(term));

    public static BillingFrequency ParseBillingCycle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Billing cycle is required.");

        return value.Trim().ToUpperInvariant() switch
        {
            "M" or "MONTHLY" or "MONTH" => BillingFrequency.Monthly,
            "Q" or "QUARTERLY" or "QUARTER" => BillingFrequency.Quarterly,
            "4" or "EVERY4MONTHS" or "EVERY 4 MONTHS" or "EVERY FOUR MONTHS" or "4 MONTHS" or "FOUR MONTHS"
                => BillingFrequency.EveryFourMonths,
            "Y" or "YEARLY" or "YEAR" or "ANNUAL" or "ANNUALLY" => BillingFrequency.Annual,
            _ => throw new FormatException($"Unknown billing cycle '{value}'. Use Monthly, Quarterly, Every 4 Months, or Annual.")
        };
    }

    public static string FormatBillingCycle(BillingFrequency term) => BillingTermDisplay.Label(term);

    public static decimal ToAnnualPrice(decimal billingPeriodAmount, int months)
    {
        months = Math.Max(months, 1);
        return Math.Round(billingPeriodAmount * 12m / months, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal ToAnnualPrice(decimal billingPeriodAmount, BillingFrequency term) =>
        ToAnnualPrice(billingPeriodAmount, MonthsInTerm(term));

    public static decimal ToAnnualPrice(decimal billingPeriodAmount, CustomerContract contract) =>
        ToAnnualPrice(billingPeriodAmount, BillingMonths(contract));

    public static decimal BillingPeriodAmountToMonthlyRate(decimal billingPeriodAmount, int months) =>
        Math.Round(billingPeriodAmount / Math.Max(months, 1), 2, MidpointRounding.AwayFromZero);

    public static decimal BillingPeriodAmountToMonthlyRate(decimal billingPeriodAmount, BillingFrequency term) =>
        BillingPeriodAmountToMonthlyRate(billingPeriodAmount, MonthsInTerm(term));

    public static decimal BillingPeriodAmountToMonthlyRate(decimal billingPeriodAmount, CustomerContract contract) =>
        BillingPeriodAmountToMonthlyRate(billingPeriodAmount, BillingMonths(contract));

    public static decimal MonthlyRateToBillingPeriodAmount(decimal monthlyRate, int months) =>
        Math.Round(monthlyRate * Math.Max(months, 1), 2, MidpointRounding.AwayFromZero);

    public static decimal MonthlyRateToBillingPeriodAmount(decimal monthlyRate, BillingFrequency term) =>
        MonthlyRateToBillingPeriodAmount(monthlyRate, MonthsInTerm(term));

    public static decimal MonthlyRateToBillingPeriodAmount(decimal monthlyRate, CustomerContract contract) =>
        MonthlyRateToBillingPeriodAmount(monthlyRate, BillingMonths(contract));

    public static decimal GetBillingAmount(CustomerContractRoute contractRoute) =>
        contractRoute.BillingAmount > 0 ? contractRoute.BillingAmount : contractRoute.Route.Price;
}
