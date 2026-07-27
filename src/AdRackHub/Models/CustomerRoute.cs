using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

public class CustomerRoute
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Customer")]
    public int CustomerId { get; set; }

    [Required]
    [Display(Name = "Route")]
    public int RouteId { get; set; }

    [Display(Name = "All Stops")]
    public bool AllStops { get; set; } = true;

    [Required]
    public CustomerRouteStatus Status { get; set; } = CustomerRouteStatus.Active;

    [Range(0, double.MaxValue)]
    [Display(Name = "Rate per Month")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal RatePerMonth { get; set; }

    [Display(Name = "Months Subscribed")]
    public int SubscribedMonthMask { get; set; } = SubscribedMonths.AllMonthsMask;

    [Required]
    [Display(Name = "Billing Cycle")]
    public BillingFrequency BillingTerm { get; set; } = BillingFrequency.Quarterly;

    public Customer Customer { get; set; } = null!;
    public Route Route { get; set; } = null!;
    public ICollection<CustomerRouteStop> CustomerRouteStops { get; set; } = new List<CustomerRouteStop>();
}
