using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class CustomerNote
{
    public int Id { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [Required]
    [Display(Name = "Type")]
    public CustomerNoteKind Kind { get; set; } = CustomerNoteKind.Note;

    [Required]
    [Display(Name = "Status")]
    public CustomerNoteStatus Status { get; set; } = CustomerNoteStatus.New;

    [DataType(DataType.Date)]
    [Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    [Required]
    [StringLength(4000)]
    [Display(Name = "Note")]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsOverdue =>
        Kind != CustomerNoteKind.BrochuresNeeded
        && Status != CustomerNoteStatus.Done
        && DueDate.HasValue
        && DueDate.Value < DateOnly.FromDateTime(DateTime.Today);

    [StringLength(100)]
    [Display(Name = "Logged by")]
    public string? CreatedBy { get; set; }

    public Customer Customer { get; set; } = null!;

    public ICollection<CustomerNoteSubNote> SubNotes { get; set; } = new List<CustomerNoteSubNote>();
}
