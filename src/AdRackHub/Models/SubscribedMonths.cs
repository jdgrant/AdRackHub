namespace AdRackHub.Models;

public static class SubscribedMonths
{
    public const int AllMonthsMask = (1 << 12) - 1;

    public static int BuildMask(IEnumerable<int> months) =>
        months.Where(m => m is >= 1 and <= 12).Distinct().Aggregate(0, (mask, month) => mask | (1 << (month - 1)));

    public static IEnumerable<int> GetMonths(int mask) =>
        Enumerable.Range(1, 12).Where(month => (mask & (1 << (month - 1))) != 0);

    public static int Count(int mask) => GetMonths(mask).Count();

    public static string Format(int mask)
    {
        var months = GetMonths(mask).ToList();
        if (months.Count == 0)
            return "None";

        if (months.Count == 12)
            return "All months";

        return string.Join(", ", months.Select(m => new DateOnly(2000, m, 1).ToString("MMM")));
    }

    public static decimal DefaultRatePerMonth(Route route) => route.BillingFrequency switch
    {
        BillingFrequency.Quarterly => route.Price / 3m,
        BillingFrequency.EveryFourMonths => route.Price / 4m,
        BillingFrequency.Annual => route.Price / 12m,
        _ => route.Price
    };
}
