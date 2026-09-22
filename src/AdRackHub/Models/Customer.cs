using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AdRackHub.Models;

public class Customer
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Customer Name")]
    public string CustomerName { get; set; } = string.Empty;

    [Display(Name = "Wave Customer ID")]
    [StringLength(100)]
    public string? WaveCustomerId { get; set; }

    [Required]
    public CustomerStatus Status { get; set; } = CustomerStatus.Active;

    [Required]
    [Display(Name = "Type")]
    public CustomerType Type { get; set; } = CustomerType.Customer;

    [Display(Name = "High Value Prospect")]
    public bool IsHighValueProspect { get; set; }

    [Display(Name = "At Risk")]
    public bool IsAtRisk { get; set; }

    [Display(Name = "Needs More Info")]
    public bool NeedsMoreInfo { get; set; }

    [Display(Name = "Target Route")]
    public int? ExpandedProspectRouteId { get; set; }

    [ForeignKey(nameof(ExpandedProspectRouteId))]
    [ValidateNever]
    public Route? ExpandedProspectRoute { get; set; }

    [Display(Name = "Account Manager")]
    [StringLength(450)]
    public string? AccountManagerId { get; set; }

    [ForeignKey(nameof(AccountManagerId))]
    public ApplicationUser? AccountManager { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(50)]
    public string? State { get; set; }

    [StringLength(20)]
    public string? Zip { get; set; }

    [Display(Name = "Latitude")]
    public double? Latitude { get; set; }

    [Display(Name = "Longitude")]
    public double? Longitude { get; set; }

    [Phone]
    [StringLength(50)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; set; }

    [Required]
    [Display(Name = "Invoice Receipt Method")]
    public InvoiceReceiptMethod InvoiceReceiptMethod { get; set; } = InvoiceReceiptMethod.Mail;

    [StringLength(500)]
    [Display(Name = "Web URL")]
    public string? WebUrl { get; set; }

    [StringLength(50)]
    [Display(Name = "Warehouse Rack")]
    public string? WarehouseRack { get; set; }

    [StringLength(50)]
    [Display(Name = "Warehouse Bin")]
    public string? WarehouseBin { get; set; }

    [NotMapped]
    public string? WarehouseLocationLabel => WarehouseLocation.Format(WarehouseRack, WarehouseBin);

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<CustomerRoute> CustomerRoutes { get; set; } = new List<CustomerRoute>();
    public ICollection<CustomerContract> Contracts { get; set; } = new List<CustomerContract>();
    public ICollection<CustomerBrochureScan> BrochureScans { get; set; } = new List<CustomerBrochureScan>();
    public ICollection<CustomerBrochureInventory> BrochureInventories { get; set; } = new List<CustomerBrochureInventory>();
    public ICollection<CustomerNote> Notes { get; set; } = new List<CustomerNote>();
    public ICollection<CustomerTask> Tasks { get; set; } = new List<CustomerTask>();
}
