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
    [StringLength(4000)]
    [Display(Name = "Note")]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    [Display(Name = "Logged by")]
    public string? CreatedBy { get; set; }

    public Customer Customer { get; set; } = null!;
}
