using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

public class BillingRunInvoice
{
    public int Id { get; set; }

    [Required]
    public int BillingRunId { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [StringLength(100)]
    public string? WaveCustomerId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Required]
    public BillingRunInvoiceStatus Status { get; set; } = BillingRunInvoiceStatus.Pending;

    [StringLength(100)]
    public string? WaveInvoiceId { get; set; }

    /// <summary>Human-readable Wave invoice number (e.g. from Wave or Make callback).</summary>
    [StringLength(50)]
    public string? WaveInvoiceNumber { get; set; }

    [StringLength(500)]
    public string? WaveInvoiceUrl { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Date payment/invoice was marked Received.</summary>
    public DateOnly? ReceivedDate { get; set; }

    public bool HasWaveInvoiceNumber => !string.IsNullOrWhiteSpace(WaveInvoiceNumber);

    public BillingRun BillingRun { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public ICollection<BillingRunInvoiceLine> Lines { get; set; } = new List<BillingRunInvoiceLine>();
}
