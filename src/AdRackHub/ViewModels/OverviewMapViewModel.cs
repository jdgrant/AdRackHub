namespace AdRackHub.ViewModels;

public class OverviewMapViewModel
{
    public int? SelectedRouteId { get; set; }
    public string? SelectedRouteName { get; set; }
    public List<OverviewMapRouteOption> Routes { get; set; } = new();
    public List<OverviewMapLegendItem> Legend { get; set; } = new();
    public List<OverviewMapMarker> Markers { get; set; } = new();

    public IEnumerable<OverviewMapMarker> Stops =>
        Markers.Where(m => m.Kind != "prospect");

    public IEnumerable<OverviewMapMarker> ProspectStops =>
        Markers.Where(m => m.Kind == "prospect");
}

public class OverviewMapRouteOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class OverviewMapLegendItem
{
    public string Label { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public class OverviewMapMarker
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string CityState { get; set; } = string.Empty;
    public string ExitNumber { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string Kind { get; set; } = "stop";
    public string Color { get; set; } = "#4363d8";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool FitBounds { get; set; }
    public string DetailsUrl { get; set; } = string.Empty;
}
