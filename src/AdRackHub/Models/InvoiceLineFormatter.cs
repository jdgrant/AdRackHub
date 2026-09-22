namespace AdRackHub.Models;

public sealed class InvoiceLineContent
{
    public string Description { get; init; } = string.Empty;
    public DateOnly ServiceStartDate { get; init; }
    public DateOnly ServiceEndDate { get; init; }
    public int SpaceCount { get; init; }
    public IReadOnlyList<string> LocationNames { get; init; } = Array.Empty<string>();
    public string Locations { get; init; } = string.Empty;
    public int PeriodMonthCount { get; init; }
    public decimal PeriodAmount { get; init; }
    public decimal MonthlyRate { get; init; }
    public bool PriceIsPeriodTotal { get; init; } = true;
    public string PriceLabel { get; init; } = string.Empty;
}

public static class InvoiceLineFormatter
{
    public static DateOnly ServicePeriodEnd(DateOnly start, int monthCount) =>
        start.AddMonths(Math.Max(monthCount, 1)).AddDays(-1);

    public static InvoiceLineContent Build(
        string routeName,
        DateOnly serviceStart,
        int monthCount,
        decimal periodAmount,
        Customer customer,
        Route route) =>
        Build(
            routeName,
            serviceStart,
            monthCount,
            periodAmount,
            customer.CustomerRoutes?.FirstOrDefault(cr => cr.RouteId == route.Id),
            route);

    public static InvoiceLineContent Build(
        string routeName,
        DateOnly serviceStart,
        int monthCount,
        decimal periodAmount,
        CustomerRoute? assignment,
        Route? route)
    {
        var names = AssignedLocationNames(assignment, route);
        var allLocations = assignment == null || assignment.AllStops;
        return Build(routeName, serviceStart, monthCount, periodAmount, names, allLocations);
    }

    public static InvoiceLineContent Build(
        string routeName,
        DateOnly serviceStart,
        int monthCount,
        decimal periodAmount,
        IReadOnlyList<string>? locationNames = null,
        bool allLocations = true)
    {
        monthCount = Math.Max(monthCount, 1);
        var end = ServicePeriodEnd(serviceStart, monthCount);
        var names = (locationNames ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var spaces = names.Count;
        var monthly = AnnualBillingHelper.BillingPeriodAmountToMonthlyRate(periodAmount, monthCount);
        var amountText = periodAmount == decimal.Truncate(periodAmount)
            ? periodAmount.ToString("C0")
            : periodAmount.ToString("C");
        var priceLabel = $"Total service fee: {amountText}";
        var locations = names.Count == 0 ? routeName : string.Join("; ", names);
        var description =
            $"{RouteNaming.DistributionLabel(routeName)} — {serviceStart:MMMM d, yyyy} through {end:MMMM d, yyyy}";
        if (RouteProductHelper.FromRouteName(routeName) == RouteProduct.RestArea
            && !allLocations
            && names.Count > 0)
        {
            description += $" — {string.Join(", ", names)}";
        }

        return new InvoiceLineContent
        {
            Description = description,
            ServiceStartDate = serviceStart,
            ServiceEndDate = end,
            SpaceCount = spaces,
            LocationNames = names,
            Locations = locations,
            PeriodMonthCount = monthCount,
            PeriodAmount = periodAmount,
            MonthlyRate = monthly,
            PriceIsPeriodTotal = true,
            PriceLabel = priceLabel
        };
    }

    public static List<string> AssignedLocationNames(CustomerRoute? assignment, Route? route)
    {
        IEnumerable<Stop> stops;
        if (route == null)
            return new List<string>();

        if (assignment == null || assignment.AllStops)
            stops = route.Stops ?? Enumerable.Empty<Stop>();
        else
            stops = assignment.CustomerRouteStops?
                .Select(crs => crs.Stop)
                .Where(s => s != null)
                .Cast<Stop>()
                ?? Enumerable.Empty<Stop>();

        return stops
            .Where(s => s.Status == StopStatus.Active)
            .OrderBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .Select(FormatStopLocation)
            .ToList();
    }

    public static string FormatStopLocation(Stop stop)
    {
        if (!string.IsNullOrWhiteSpace(stop.HighwayExit)
            && stop.StopName.IndexOf(stop.HighwayExit, StringComparison.OrdinalIgnoreCase) < 0)
            return $"{stop.StopName} ({stop.HighwayExit})";
        return stop.StopName;
    }
}
