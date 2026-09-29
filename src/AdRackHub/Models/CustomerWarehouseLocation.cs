using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

public class CustomerWarehouseLocation
{
    public int Id { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [Display(Name = "Warehouse")]
    public BrochureWarehouse? Warehouse { get; set; }

    [StringLength(50)]
    [Display(Name = "Rack")]
    public string? Rack { get; set; }

    [StringLength(50)]
    [Display(Name = "Bin")]
    public string? Bin { get; set; }

    [Display(Name = "Top/Bottom")]
    public BrochureShelf? Shelf { get; set; }

    public int SortOrder { get; set; }

    public Customer Customer { get; set; } = null!;

    [NotMapped]
    public string? Label => WarehouseLocation.Format(Warehouse, Rack, Bin, Shelf);

    [NotMapped]
    public string? CompactLabel => WarehouseLocation.FormatCompact(Warehouse, Rack, Bin, Shelf);
}
