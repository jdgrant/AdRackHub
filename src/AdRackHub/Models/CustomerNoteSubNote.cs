using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class CustomerNoteSubNote
{
    public int Id { get; set; }

    [Required]
    public int CustomerNoteId { get; set; }

    [Required]
    [StringLength(2000)]
    [Display(Name = "Sub-note")]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    [Display(Name = "Logged by")]
    public string? CreatedBy { get; set; }

    public CustomerNote CustomerNote { get; set; } = null!;
}
