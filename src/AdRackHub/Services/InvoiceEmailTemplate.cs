using System.Globalization;

namespace AdRackHub.Services;

public static class InvoiceEmailTemplate
{
    public static string Apply(string template, WaveInvoiceResult invoice, string? fallbackCustomerName)
    {
        var businessName = FirstNonEmpty(invoice.BusinessName, "Ad-Rack Services LLC") ?? "Ad-Rack Services LLC";
        var customerName = FirstNonEmpty(invoice.CustomerName, fallbackCustomerName) ?? string.Empty;
        var invoiceNumber = invoice.InvoiceNumber ?? string.Empty;
        var amount = FormatAmount(invoice.Amount);
        var link = invoice.WaveInvoiceUrl?.Trim() ?? string.Empty;
        var text = template
            .Replace("{{Invoice number}}", invoiceNumber, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Your business name}}", businessName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Client company name}}", customerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Invoice amount}}", amount, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Invoice balance}}", amount, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Invoice link}}", link, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Invoice URL}}", link, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Payment URL}}", link, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(link))
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^\s*View and pay this invoice:\s*\r?\n?",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return text;
    }

    public static string FormatAmount(string? raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || decimal.TryParse(raw, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out amount))
            return amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        return raw ?? string.Empty;
    }

    static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
