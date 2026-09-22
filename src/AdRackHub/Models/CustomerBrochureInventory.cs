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

    [StringLength(50)]
    [Display(Name = "Rack")]
    public string? Rack { get; set; }

    [StringLength(50)]
    [Display(Name = "Bin")]
    public string? Bin { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    [Display(Name = "Logged by")]
    public string? CreatedBy { get; set; }

    public Customer Customer { get; set; } = null!;

    public string? LocationLabel => WarehouseLocation.Format(Rack, Bin);
}

public static class WarehouseLocation
{
    public static string? Format(string? rack, string? bin)
    {
        rack = NullIfEmpty(rack);
        bin = NullIfEmpty(bin);
        if (rack == null && bin == null)
            return null;
        if (rack != null && bin != null)
            return $"Rack {rack} · Bin {bin}";
        return rack != null ? $"Rack {rack}" : $"Bin {bin}";
    }

    public static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
