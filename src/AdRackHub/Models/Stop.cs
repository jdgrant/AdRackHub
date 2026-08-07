using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AdRackHub.Models;

public class Stop
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Route")]
    public int RouteId { get; set; }

    [Display(Name = "Step #")]
    public int? StepNumber { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Business Name")]
    public string StopName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Stop Type")]
    public StopType StopType { get; set; }

    [StringLength(300)]
    [Display(Name = "Rack Placement")]
    public string? RackPlacement { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(50)]
    public string? State { get; set; }

    [StringLength(20)]
    public string? Zip { get; set; }

    [StringLength(200)]
    [Display(Name = "Highway / Exit")]
    public string? HighwayExit { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Required]
    public StopStatus Status { get; set; } = StopStatus.Active;

    [Display(Name = "Last Visited")]
    public DateTime? LastVisitedAt { get; set; }

    // Not posted from Create/Edit forms — only RouteId is. Must stay optional for validation
    // so ASP.NET does not reject the save ("The Route field is required.").
    [ValidateNever]
    public Route? Route { get; set; }

    [ValidateNever]
    public ICollection<CustomerRouteStop> CustomerRouteStops { get; set; } = new List<CustomerRouteStop>();

    [ValidateNever]
    public ICollection<StopVisit> Visits { get; set; } = new List<StopVisit>();
}
