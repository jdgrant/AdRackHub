using AdRackHub.Models;

namespace AdRackHub.Services;

public static class BrochureReceiptEmail
{
    public const string CopyRecipientName = "Jon Grant";
    public const string CopyRecipientEmail = "jon@ad-rack.net";

    public static (string Subject, string Body) Compose(
        string customerName,
        string? brochureCode,
        DateOnly date,
        int quantity,
        decimal? cases,
        int? caseCount,
        string? notes,
        string? sampleNote = null)
    {
        var name = string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName.Trim();
        var subject = string.IsNullOrWhiteSpace(sampleNote)
            ? $"We received your brochures — {name}"
            : $"Sample brochure receipt — {name}";

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(sampleNote))
        {
            lines.Add(sampleNote.Trim());
            lines.Add("");
        }

        lines.Add("We received your brochures.");
        lines.Add("");
        lines.Add($"Customer: {name}");
        if (!string.IsNullOrWhiteSpace(brochureCode))
            lines.Add($"Brochure ID: {brochureCode.Trim()}");
        lines.Add($"Date: {date:MMMM d, yyyy}");
        lines.Add($"Brochures: {quantity:N0}");
        if (cases is > 0 && caseCount is > 0)
            lines.Add($"Cases: {cases.Value:0.##} cases of {caseCount.Value:N0}");
        else if (cases is > 0)
            lines.Add($"Cases: {cases.Value:0.##}");
        if (!string.IsNullOrWhiteSpace(notes))
            lines.Add($"Notes: {notes.Trim()}");
        lines.Add("");
        lines.Add("Thank you for trusting Ad-Rack");
        return (subject, string.Join('\n', lines));
    }
}
