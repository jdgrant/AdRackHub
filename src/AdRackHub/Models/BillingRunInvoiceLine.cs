using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

public class BillingRunInvoiceLine
{
    public int Id { get; set; }

    [Required]
    public int BillingRunInvoiceId { get; set; }

    [Required]
    [Column("CustomerBillingId")]
    public int CustomerContractId { get; set; }

    [Required]
    [StringLength(200)]
    [Column("BillName")]
    public string ContractName { get; set; } = string.Empty;

    [Required]
    public BillingFrequency Term { get; set; }

    [Range(1, 36)]
    public int BillingMonthCount { get; set; }

    [Required]
    [StringLength(200)]
    public string RouteName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public BillingRunInvoice BillingRunInvoice { get; set; } = null!;
}
