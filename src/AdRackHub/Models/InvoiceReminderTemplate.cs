using System.ComponentModel.DataAnnotations;

namespace AdRackHub.Models;

public static class InvoiceReminderDefaults
{
    public const string Subject =
        "Reminder: Invoice #{{Invoice number}} from Ad-Rack Services";

    public const string Body =
        """
        Hi {{Client company name}},

        This is a reminder that Invoice #{{Invoice number}} for {{Invoice amount}} is still open. It is {{Days late}} days past the start of the {{Billing period}} billing period.

        View and pay this invoice: {{Invoice link}}

        Please note the payment address:
        Ad-Rack Services LLC
        7608 W HWY 146
        STE 104
        Pewee Valley, KY 40056

        If you have already sent payment, thank you. You can disregard this note.

        Thank you,

        {{Your business name}}
        """;
}

public class InvoiceReminderTemplate
{
    public int Id { get; set; }

    [Required]
    [StringLength(300)]
    public string Subject { get; set; } = InvoiceReminderDefaults.Subject;

    [Required]
    [StringLength(8000)]
    public string Body { get; set; } = InvoiceReminderDefaults.Body;

    public DateTime UpdatedAt { get; set; }

    [StringLength(200)]
    public string? UpdatedBy { get; set; }
}

public class InvoiceReminderTemplateRevision
{
    public int Id { get; set; }

    [Required]
    [StringLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(8000)]
    public string Body { get; set; } = string.Empty;

    [Required]
    [StringLength(40)]
    public string ChangeKind { get; set; } = "Saved";

    public DateTime ChangedAt { get; set; }

    [StringLength(200)]
    public string? ChangedBy { get; set; }
}
