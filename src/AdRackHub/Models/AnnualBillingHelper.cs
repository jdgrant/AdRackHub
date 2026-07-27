namespace AdRackHub.Models;

public static class AnnualBillingHelper
{
    public static decimal ToRatePerMonth(decimal annualPrice) =>
        Math.Round(annualPrice / 12m, 2, MidpointRounding.AwayFromZero);

    public static decimal ToBillingPeriodAmount(decimal annualPrice, BillingFrequency term) => term switch
    {
        BillingFrequency.Monthly => ToRatePerMonth(annualPrice),
        BillingFrequency.Quarterly => Math.Round(annualPrice / 4m, 2, MidpointRounding.AwayFromZero),
        BillingFrequency.Annual => Math.Round(annualPrice, 2, MidpointRounding.AwayFromZero),
        _ => Math.Round(annualPrice, 2, MidpointRounding.AwayFromZero)
    };

    public static BillingFrequency ParseBillingCycle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Billing cycle is required.");

        return value.Trim().ToUpperInvariant() switch
        {
            "M" or "MONTHLY" or "MONTH" => BillingFrequency.Monthly,
            "Q" or "QUARTERLY" or "QUARTER" => BillingFrequency.Quarterly,
            "Y" or "YEARLY" or "YEAR" or "ANNUAL" or "ANNUALLY" => BillingFrequency.Annual,
            _ => throw new FormatException($"Unknown billing cycle '{value}'. Use Monthly, Quarterly, or Annual.")
        };
    }

    public static string FormatBillingCycle(BillingFrequency term) => term switch
    {
        BillingFrequency.Monthly => "Monthly",
        BillingFrequency.Quarterly => "Quarterly",
        BillingFrequency.Annual => "Annual",
        _ => term.ToString()
    };

    public static decimal ToAnnualPrice(decimal billingPeriodAmount, BillingFrequency term) => term switch
    {
        BillingFrequency.Monthly => Math.Round(billingPeriodAmount * 12m, 2, MidpointRounding.AwayFromZero),
        BillingFrequency.Quarterly => Math.Round(billingPeriodAmount * 4m, 2, MidpointRounding.AwayFromZero),
        BillingFrequency.Annual => Math.Round(billingPeriodAmount, 2, MidpointRounding.AwayFromZero),
        _ => Math.Round(billingPeriodAmount, 2, MidpointRounding.AwayFromZero)
    };

    public static decimal BillingPeriodAmountToMonthlyRate(decimal billingPeriodAmount, BillingFrequency term) => term switch
    {
        BillingFrequency.Monthly => billingPeriodAmount,
        BillingFrequency.Quarterly => Math.Round(billingPeriodAmount / 3m, 2, MidpointRounding.AwayFromZero),
        BillingFrequency.Annual => Math.Round(billingPeriodAmount / 12m, 2, MidpointRounding.AwayFromZero),
        _ => billingPeriodAmount
    };

    public static decimal MonthlyRateToBillingPeriodAmount(decimal monthlyRate, BillingFrequency term) => term switch
    {
        BillingFrequency.Monthly => monthlyRate,
        BillingFrequency.Quarterly => Math.Round(monthlyRate * 3m, 2, MidpointRounding.AwayFromZero),
        BillingFrequency.Annual => Math.Round(monthlyRate * 12m, 2, MidpointRounding.AwayFromZero),
        _ => monthlyRate
    };

    public static decimal GetBillingAmount(CustomerContractRoute contractRoute) =>
        contractRoute.BillingAmount > 0 ? contractRoute.BillingAmount : contractRoute.Route.Price;
}
