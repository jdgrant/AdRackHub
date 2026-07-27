using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public class CustomerRouteStop
{
    public int Id { get; set; }

    [Required]
    public int CustomerRouteId { get; set; }

    [Required]
    [Display(Name = "Stop")]
    public int StopId { get; set; }

    public CustomerRoute CustomerRoute { get; set; } = null!;
    public Stop Stop { get; set; } = null!;
}
