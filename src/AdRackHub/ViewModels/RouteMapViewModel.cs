namespace AdRackHub.ViewModels;

public class RouteMapViewModel
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public List<RouteMapStopItem> Stops { get; set; } = new();
}

public class RouteMapStopItem
{
    public int Id { get; set; }
    public int? StepNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
