using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AdRackHub.Models;

public class ApplicationUser : IdentityUser
{
    [PersonalData]
    [StringLength(100)]
    public string? DisplayName { get; set; }
}
