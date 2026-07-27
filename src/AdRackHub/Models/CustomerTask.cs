using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class CustomerTask
{
    public int Id { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Task")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    [Display(Name = "Details")]
    public string? Description { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    [Required]
    public CustomerTaskStatus Status { get; set; } = CustomerTaskStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    [StringLength(100)]
    [Display(Name = "Created by")]
    public string? CreatedBy { get; set; }

    public Customer Customer { get; set; } = null!;

    public bool IsOpen => Status == CustomerTaskStatus.Open;

    public bool IsOverdue =>
        IsOpen && DueDate.HasValue && DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
}
