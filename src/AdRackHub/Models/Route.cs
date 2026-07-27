using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

public class Route
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Route Name")]
    public string RouteName { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    [Range(0, double.MaxValue)]
    [Display(Name = "Price")]
    public decimal Price { get; set; }

    [Required]
    [Display(Name = "Billing Frequency")]
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.Quarterly;

    [Required]
    public RouteStatus Status { get; set; } = RouteStatus.Active;

    public ICollection<Stop> Stops { get; set; } = new List<Stop>();
    public ICollection<CustomerRoute> CustomerRoutes { get; set; } = new List<CustomerRoute>();
}
