using System.ComponentModel.DataAnnotations;
using AdRackHub.Services;

namespace AdRackHub.ViewModels;

public class InvoiceReminderTemplatePageViewModel
{
    [Required(ErrorMessage = "Enter a subject.")]
    [StringLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an email body.")]
    [StringLength(8000)]
    public string Body { get; set; } = string.Empty;

    public int? PreviewInvoiceId { get; set; }
    public string? PreviewSubject { get; set; }
    public string? PreviewBody { get; set; }
    public string PreviewSourceLabel { get; set; } = "Sample customer";
    public bool HasPreview { get; set; }
    public bool MatchesSaved { get; set; } = true;
    public bool IsSystemDefault { get; set; } = true;
    public bool MailgunConfigured { get; set; }
    public string TestRecipient { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? UpdatedLabel { get; set; }
    public IReadOnlyList<InvoiceReminderMergeField> MergeFields { get; set; } = [];
    public IReadOnlyList<InvoiceReminderPreviewChoice> PreviewChoices { get; set; } = [];
    public IReadOnlyList<InvoiceReminderRevisionRow> History { get; set; } = [];
}

public class InvoiceReminderPreviewChoice
{
    public int InvoiceId { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class InvoiceReminderRevisionRow
{
    public string When { get; set; } = string.Empty;
    public string Who { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class InvoiceReminderComposeViewModel
{
    public int InvoiceId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public string? WaveInvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    public string Recipients { get; set; } = string.Empty;
    public bool CanSend { get; set; }
    public string? SendBlockedReason { get; set; }
    public bool HasPdf { get; set; }
    public bool MailgunConfigured { get; set; }

    [Required(ErrorMessage = "Enter a subject.")]
    [StringLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an email body.")]
    [StringLength(8000)]
    public string Body { get; set; } = string.Empty;

    public string? PreviewSubject { get; set; }
    public string? PreviewBody { get; set; }
    public bool HasPreview { get; set; }
    public string? LogoUrl { get; set; }
    public string? Search { get; set; }
    public int? Weeks { get; set; }
    public string? Age { get; set; }
}
