using System.ComponentModel.DataAnnotations;
using AdRackHub.Models;

namespace AdRackHub.ViewModels;

public class RouteLoadEstimatePageViewModel
{
    public List<int> SelectedRouteIds { get; set; } = new();

    [Display(Name = "Brochures per client")]
    [Range(1, 100000)]
    public int BrochuresPerClient { get; set; } = 100;

    [Display(Name = "Trucks")]
    [Range(1, 20)]
    public int TruckCount { get; set; } = 4;

    [Display(Name = "Truck capacity")]
    [Range(1, 1000000)]
    public int? VanCapacity { get; set; }

    public List<RouteLoadRouteOption> AvailableRoutes { get; set; } = new();
    public RouteLoadEstimateResult? Result { get; set; }
}

public class RouteLoadRouteOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public RouteProduct Product { get; set; }
    public int ActiveCustomers { get; set; }
    public bool Selected { get; set; }
}

public class RouteLoadEstimateResult
{
    public int DistinctCustomers { get; set; }
    public int CombinedBrochures { get; set; }
    public int SeparateBrochures { get; set; }
    public int SharedCustomers { get; set; }
    public int UniqueStops { get; set; }
    public int RemainderStops { get; set; }
    public int SharedStops { get; set; }
    public int BrochureSavings { get; set; }
    public int? CombinedVans { get; set; }
    public int? SeparateVans { get; set; }
    public double? FillPercent { get; set; }
    public bool CombinedFits { get; set; }
    public int RequestedTrucks { get; set; }
    public int SplitCustomers { get; set; }
    public int CapturedSharedCustomers { get; set; }
    public int TruckBrochures { get; set; }
    public List<RouteLoadRouteSummary> Routes { get; set; } = new();
    public List<RouteLoadCustomerRow> Customers { get; set; } = new();
    public List<RouteLoadVanPack> SuggestedVans { get; set; } = new();
}

public class RouteLoadRouteSummary
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public int Customers { get; set; }
    public int ExclusiveCustomers { get; set; }
    public int SharedCustomers { get; set; }
    public int UniqueStops { get; set; }
    public int RemainderStops { get; set; }
    public int SharedStops { get; set; }
    public int BrochuresIfAlone { get; set; }
}

public class RouteLoadCustomerRow
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? BrochureCode { get; set; }
    public string? Warehouse { get; set; }
    public int Brochures { get; set; }
    public List<int> RouteIds { get; set; } = new();
    public List<string> RouteNames { get; set; } = new();
    public List<int> TruckNumbers { get; set; } = new();
}

public class RouteLoadVanPack
{
    public int VanNumber { get; set; }
    public int DistinctCustomers { get; set; }
    public int SharedCustomers { get; set; }
    public int UniqueStops { get; set; }
    public int RemainderStops { get; set; }
    public int SharedStops { get; set; }
    public int Brochures { get; set; }
    public double? FillPercent { get; set; }
    public bool OverCapacity { get; set; }
    public List<int> RouteIds { get; set; } = new();
    public List<string> RouteNames { get; set; } = new();
}
