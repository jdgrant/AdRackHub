namespace AdRackHub.Models;

public static class BillingTermDisplay
{
    public static string Label(int months) => months switch
    {
        1 => "Monthly",
        3 => "Quarterly",
        4 => "Every 4 Months",
        12 => "Yearly",
        _ => $"{Math.Max(months, 1)} months"
    };

    public static string Label(BillingFrequency term) => Label(AnnualBillingHelper.MonthsInTerm(term));

    public static string Label(CustomerContract contract) => Label(AnnualBillingHelper.BillingMonths(contract));

    public static string Label(CustomerRoute route) => Label(AnnualBillingHelper.BillingMonths(route));
}
