using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class CustomerBrochureInventory
{
    public int Id { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [Required]
    [Display(Name = "Count")]
    [Range(0, int.MaxValue, ErrorMessage = "Count must be zero or greater.")]
    public int Quantity { get; set; }

    [Required]
    [Display(Name = "Inventory Date")]
    [DataType(DataType.Date)]
    public DateOnly InventoryDate { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    [Display(Name = "Logged by")]
    public string? CreatedBy { get; set; }

    public Customer Customer { get; set; } = null!;
}
