using System.ComponentModel.DataAnnotations;
using AdRackHub.Services;

namespace AdRackHub.ViewModels;

public class AdminIndexViewModel
{
    public int UserCount { get; set; }
    public int CustomerCount { get; set; }
    public int RouteCount { get; set; }
    public int StopCount { get; set; }
    public List<AdminUserRow> Users { get; set; } = new();
}

public class AdminUserRow
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public IList<string> Roles { get; set; } = new List<string>();
    public bool IsLockedOut { get; set; }
}

public class CreateAdminUserViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Roles")]
    public List<string> SelectedRoles { get; set; } = new();
}

public class EditAdminUserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Display(Name = "Roles")]
    public List<string> SelectedRoles { get; set; } = new();

    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string? ConfirmNewPassword { get; set; }

    public bool IsLockedOut { get; set; }
}

public class WaveSyncSetupViewModel
{
    public string BaseUrl { get; set; } = string.Empty;
    public bool InboundConfigured { get; set; }
    public bool OutboundConfigured { get; set; }
    public bool WaveApiConfigured { get; set; }
    public bool PushCustomersToWaveApi { get; set; }

    public string HealthUrl => $"{BaseUrl}/api/wave-sync";
    public string ImportCustomerUrl => $"{BaseUrl}/api/wave-sync/customers";
    public string BillingCallbackUrl => $"{BaseUrl}/api/wave-sync/billing/callback";
}

public class OptimizeBrochuresViewModel
{
    public BrochureOptimizeStats Stats { get; set; } = new();
    public BrochureOptimizeResult? LastResult { get; set; }
}
