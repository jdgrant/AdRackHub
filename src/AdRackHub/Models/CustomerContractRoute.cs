using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

[Table("CustomerBillingRoutes")]
public class CustomerContractRoute
{
    public int Id { get; set; }

    [Required]
    [Column("CustomerBillingId")]
    public int CustomerContractId { get; set; }

    [Required]
    [Display(Name = "Route")]
    public int RouteId { get; set; }

    [Range(0, double.MaxValue)]
    [Display(Name = "Billing Amount")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal BillingAmount { get; set; }

    public CustomerContract Contract { get; set; } = null!;
    public Route Route { get; set; } = null!;
}
