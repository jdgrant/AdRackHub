using System.ComponentModel.DataAnnotations;
using AdRackHub.Models;
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
    public string InvoiceCallbackUrl => $"{BaseUrl}/api/wave-sync/invoices/callback";
}

public class WaveProofViewModel
{
    public string RedirectUri { get; set; } = string.Empty;
    public string LocalRedirectUri { get; set; } = "http://localhost:5281/Admin/WaveOAuthCallback";
    public bool OAuthConfigured { get; set; }
    public string? ClientId { get; set; }
    public bool Connected { get; set; }
    public bool NeedsBusinessReset { get; set; }
    public string? BusinessName { get; set; }
    public int? ContractId { get; set; }
    public string? CustomerName { get; set; }
    public string? ContractName { get; set; }
    public string? ExistingWaveCustomerId { get; set; }
    public List<string> LineSummaries { get; set; } = new();
    public bool CanCreateInvoice { get; set; }
    public string? ResultMessage { get; set; }
    public string? WaveInvoiceId { get; set; }
    public string? WaveInvoiceNumber { get; set; }
    public string? WaveInvoiceUrl { get; set; }
    public bool CanApproveInvoice { get; set; }
    public bool InvoiceApproved { get; set; }
    public string? WaveInvoicePdfUrl { get; set; }
    public string? WaveInvoiceDueDate { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool HasSavedInvoicePdf { get; set; }
    public List<string> InvoiceRecipients { get; set; } = new();
    public InvoiceReceiptMethod InvoiceReceiptMethod { get; set; } = InvoiceReceiptMethod.Mail;
    public bool WillEmailInvoice { get; set; }
    public bool Success { get; set; }
}

public class OptimizeBrochuresViewModel
{
    public BrochureOptimizeStats Stats { get; set; } = new();
    public BrochureOptimizeResult? LastResult { get; set; }
}

public class ProspectHotelsViewModel
{
    public ProspectHotelDiscoveryStats Stats { get; set; } = new();
    public ProspectHotelDiscoveryResult? LastResult { get; set; }
    public int? MaxSeeds { get; set; } = 25;
    public int? SkipSeeds { get; set; } = 0;
    public bool DryRun { get; set; }
    public Guid? ActiveJobId { get; set; }
}

public class ProspectStopsPageViewModel : ProspectHotelsViewModel
{
    public List<Stop> Stops { get; set; } = new();
    public int? RouteId { get; set; }
}
