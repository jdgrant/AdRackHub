using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class CustomerBrochureScan
{
    public int Id { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [Required]
    [StringLength(255)]
    [Display(Name = "File Name")]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Customer Customer { get; set; } = null!;

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
