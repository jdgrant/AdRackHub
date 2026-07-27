using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class StopVisit
{
    public int Id { get; set; }

    [Required]
    public int StopId { get; set; }

    [Required]
    [Display(Name = "Visited At")]
    public DateTime VisitedAt { get; set; }

    [StringLength(450)]
    public string? VisitedByUserId { get; set; }

    [StringLength(200)]
    [Display(Name = "Visited By")]
    public string? VisitedByName { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public Stop Stop { get; set; } = null!;
}
