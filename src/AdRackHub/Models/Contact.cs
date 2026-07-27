using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AdRackHub.Models;

public class Contact
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Customer is required.")]
    [Display(Name = "Customer")]
    public int CustomerId { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; set; }

    [Phone]
    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(50)]
    public string? State { get; set; }

    [StringLength(20)]
    public string? Zip { get; set; }

    [StringLength(500)]
    [Display(Name = "Web URL")]
    public string? WebUrl { get; set; }

    [Required]
    [Display(Name = "Role")]
    public ContactRole Role { get; set; } = ContactRole.Primary;

    // Not posted from the Add Contact modal — only CustomerId is. Must stay optional
    // so ASP.NET does not treat the navigation as required ("The Customer field is required.").
    [ValidateNever]
    public Customer? Customer { get; set; }
}
