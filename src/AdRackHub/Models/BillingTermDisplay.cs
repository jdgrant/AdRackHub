namespace AdRackHub.Models;

public static class BillingTermDisplay
{
    public static string Label(BillingFrequency term) => term switch
    {
        BillingFrequency.Annual => "Yearly",
        _ => term.ToString()
    };
}
