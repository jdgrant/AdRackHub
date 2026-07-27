using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class BillingRun
{
    public int Id { get; set; }

    [Range(2000, 2100)]
    public int Year { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    [Required]
    public BillingRunStatus Status { get; set; } = BillingRunStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SubmittedAt { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public ICollection<BillingRunInvoice> Invoices { get; set; } = new List<BillingRunInvoice>();
}
