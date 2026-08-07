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

    [StringLength(200)]
    [Display(Name = "Wave Product ID")]
    public string? WaveProductId { get; set; }

    /// <summary>
    /// Derived product: names containing "Rest Area" (including "Rest Area - …") map to Rest Area; everything else is Exits.
    /// </summary>
    [NotMapped]
    [Display(Name = "Product")]
    public RouteProduct Product => RouteProductHelper.FromRouteName(RouteName);

    public ICollection<Stop> Stops { get; set; } = new List<Stop>();
    public ICollection<CustomerRoute> CustomerRoutes { get; set; } = new List<CustomerRoute>();
}

public static class RouteProductHelper
{
    public static RouteProduct FromRouteName(string? routeName) =>
        !string.IsNullOrWhiteSpace(routeName)
        && routeName.Contains("Rest Area", StringComparison.OrdinalIgnoreCase)
            ? RouteProduct.RestArea
            : RouteProduct.Exits;

    public static string Label(RouteProduct product) => product switch
    {
        RouteProduct.RestArea => "Rest Area",
        RouteProduct.Exits => "Exits",
        _ => product.ToString()
    };

    public static string LabelForRouteName(string? routeName) => Label(FromRouteName(routeName));
}
