using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public enum StopVisitPhotoKind
{
    Before,
    After
}

public class StopVisitPhoto
{
    public int Id { get; set; }

    [Required]
    public int StopVisitId { get; set; }

    [Required]
    public StopVisitPhotoKind Kind { get; set; }

    [Required]
    [StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public StopVisit StopVisit { get; set; } = null!;
}
