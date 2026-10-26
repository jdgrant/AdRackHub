using System.Globalization;
using System.Text.RegularExpressions;
using AdRackHub.Models;

namespace AdRackHub.Services;

public sealed record InvoiceReminderMergeField(string Token, string Description);

public sealed record InvoiceReminderMergeData(
    string CustomerName,
    string InvoiceNumber,
    string Amount,
    string InvoiceLink,
    string BusinessName,
    string BillingPeriod,
    string InvoiceDate,
    string DaysLate,
    string WeeksLate,
    string SourceLabel);

public static class InvoiceReminderMerge
{
    public const string TestRecipient = WaveOptions.PaperlessEmail;
    public const string SavedChange = "Saved";
    public const string RestoredChange = "Restored default";
    public const string AgentEditor = "Agent API";

    public const string UpdateDeniedMessage =
        "The agent API can read and render the invoice reminder template. It cannot change the saved template unless AgentApi:CanManageInvoiceReminderTemplate is set to true.";

    public const string DefaultSubject = InvoiceReminderDefaults.Subject;

    public const string DefaultBody = InvoiceReminderDefaults.Body;

    public static readonly IReadOnlyList<InvoiceReminderMergeField> Fields =
    [
        new("{{Client company name}}", "Customer name"),
        new("{{Invoice number}}", "Wave invoice number, or the Hub invoice id when Wave has no number"),
        new("{{Invoice amount}}", "Invoice total"),
        new("{{Invoice balance}}", "Invoice total still outstanding"),
        new("{{Invoice link}}", "Wave link for the invoice"),
        new("{{Invoice URL}}", "Same as the invoice link"),
        new("{{Payment URL}}", "Same as the invoice link"),
        new("{{Your business name}}", "Ad-Rack Services LLC"),
        new("{{Billing period}}", "Billing month, such as March 2026"),
        new("{{Invoice date}}", "First day of the billing month"),
        new("{{Days late}}", "Days since the billing month started"),
        new("{{Weeks late}}", "Whole weeks since the billing month started")
    ];

    public static InvoiceReminderMergeData Sample { get; } = new(
        CustomerName: "Sample Customer",
        InvoiceNumber: "1042",
        Amount: 250m.ToString("C", CultureInfo.GetCultureInfo("en-US")),
        InvoiceLink: "https://hub.ad-rack.net",
        BusinessName: WaveOptions.DefaultCompanyName,
        BillingPeriod: "March 2026",
        InvoiceDate: "March 1, 2026",
        DaysLate: "45",
        WeeksLate: "6",
        SourceLabel: "Sample customer");

    public static string Normalize(string? value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    public static IReadOnlyList<string> Validate(string? subject, string? body)
    {
        var errors = new List<string>();
        var cleanSubject = Normalize(subject);
        var cleanBody = Normalize(body);
        if (cleanSubject.Length == 0)
            errors.Add("Enter a subject.");
        else if (cleanSubject.Length > 300)
            errors.Add("Subject must be 300 characters or less.");
        if (cleanBody.Length == 0)
            errors.Add("Enter an email body.");
        else if (cleanBody.Length > 8000)
            errors.Add("Email body must be 8000 characters or less.");
        return errors;
    }

    public static InvoiceReminderMergeData FromInvoice(BillingRunInvoice invoice, DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var invoiceDate = new DateOnly(invoice.BillingRun.Year, invoice.BillingRun.Month, 1);
        var daysLate = Math.Max(0, today.DayNumber - invoiceDate.DayNumber);
        var culture = CultureInfo.GetCultureInfo("en-US");
        var number = string.IsNullOrWhiteSpace(invoice.WaveInvoiceNumber)
            ? invoice.Id.ToString(culture)
            : invoice.WaveInvoiceNumber.Trim();
        var period = BillingDueCalculator.PeriodLabel(invoice.BillingRun.Year, invoice.BillingRun.Month);
        var customer = invoice.Customer?.CustomerName?.Trim() ?? string.Empty;
        return new InvoiceReminderMergeData(
            customer,
            number,
            invoice.TotalAmount.ToString("C", culture),
            invoice.WaveInvoiceUrl?.Trim() ?? string.Empty,
            WaveOptions.DefaultCompanyName,
            period,
            invoiceDate.ToString("MMMM d, yyyy", culture),
            daysLate.ToString(culture),
            (daysLate / 7).ToString(culture),
            string.IsNullOrWhiteSpace(customer) ? period : $"{customer} · {period} · #{number}");
    }

    public static string Apply(string? template, InvoiceReminderMergeData data)
    {
        var text = template ?? string.Empty;
        foreach (var (token, value) in Replacements(data))
            text = text.Replace(token, value, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(data.InvoiceLink))
        {
            text = Regex.Replace(
                text,
                @"^\s*View and pay this invoice:\s*\r?\n?",
                string.Empty,
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        return text;
    }

    public static string FormatStamp(DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return local.ToString("MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US")) + " ET";
    }

    private static IEnumerable<(string Token, string Value)> Replacements(InvoiceReminderMergeData data)
    {
        yield return ("{{Client company name}}", data.CustomerName);
        yield return ("{{Invoice number}}", data.InvoiceNumber);
        yield return ("{{Invoice amount}}", data.Amount);
        yield return ("{{Invoice balance}}", data.Amount);
        yield return ("{{Invoice link}}", data.InvoiceLink);
        yield return ("{{Invoice URL}}", data.InvoiceLink);
        yield return ("{{Payment URL}}", data.InvoiceLink);
        yield return ("{{Your business name}}", data.BusinessName);
        yield return ("{{Billing period}}", data.BillingPeriod);
        yield return ("{{Invoice date}}", data.InvoiceDate);
        yield return ("{{Days late}}", data.DaysLate);
        yield return ("{{Weeks late}}", data.WeeksLate);
    }
}
