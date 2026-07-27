using AdRackHub.Services;

namespace AdRackHub.ViewModels;

public class GoogleSheetsIndexViewModel
{
    public GoogleSheetsConnectionInfo Connection { get; set; } = new();
    public List<GoogleSheetsRouteRow> Routes { get; set; } = [];
    public bool CanSync => Connection.IsConfigured && Connection.IsConnected;
}

public class GoogleSheetsRouteRow
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? SheetName { get; set; }
    public int StopCount { get; set; }
    public bool SheetFound { get; set; }
    public bool CanSync => !string.IsNullOrWhiteSpace(Slug);
}
