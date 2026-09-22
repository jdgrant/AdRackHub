using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AdRackHub.Models;

[Table("CustomerBillings")]
public class CustomerContract
{
    public int Id { get; set; }

    [BindNever]
    public int CustomerId { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Contract Name")]
    [Column("BillName")]
    public string ContractName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Billing Term")]
    public BillingFrequency Term { get; set; } = BillingFrequency.Quarterly;

    [Range(1, 36)]
    [Display(Name = "Number of Months")]
    public int BillingMonthCount { get; set; } = 3;

    [Range(1, 12)]
    [Display(Name = "Billing Start Month")]
    public int BillingAnchorMonth { get; set; } = 1;

    [Display(Name = "Months of Service")]
    public int ServiceMonthMask { get; set; } = SubscribedMonths.AllMonthsMask;

    [Display(Name = "Contract End Date")]
    public DateOnly? ContractEndDate { get; set; }

    [Required]
    [Display(Name = "Next Bill Date")]
    public DateOnly NextBillDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Display(Name = "Wave Recurring Invoice ID")]
    [StringLength(100)]
    [BindNever]
    public string? WaveRecurringInvoiceId { get; set; }

    [Display(Name = "Wave Invoice Number")]
    [StringLength(50)]
    [BindNever]
    public string? WaveInvoiceNumber { get; set; }

    [StringLength(200)]
    [BindNever]
    public string? WaveInvoiceId { get; set; }

    [StringLength(255)]
    [BindNever]
    public string? WaveInvoicePdfPath { get; set; }

    public bool HasWaveInvoicePdf => !string.IsNullOrWhiteSpace(WaveInvoicePdfPath);

    [ValidateNever]
    public Customer Customer { get; set; } = null!;
    [ValidateNever]
    public ICollection<CustomerContractRoute> ContractRoutes { get; set; } = new List<CustomerContractRoute>();
}
