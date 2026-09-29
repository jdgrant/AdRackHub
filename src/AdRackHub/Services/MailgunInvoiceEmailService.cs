using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public class MailgunInvoiceEmailService
{
    private const string LogoContentId = "ad-rack-logo.png";

    private readonly HttpClient _http;
    private readonly MailgunOptions _options;
    private readonly WaveOptions _wave;
    private readonly string _logoPath;
    private readonly ILogger<MailgunInvoiceEmailService> _logger;

    public MailgunInvoiceEmailService(
        HttpClient http,
        IOptions<MailgunOptions> options,
        IOptions<WaveOptions> wave,
        IWebHostEnvironment environment,
        ILogger<MailgunInvoiceEmailService> logger)
    {
        _http = http;
        _options = options.Value;
        _wave = wave.Value;
        _logoPath = Path.Combine(environment.WebRootPath, "images", "ad-rack-logo.png");
        _logger = logger;
    }

    public async Task<WaveSendResult> SendInvoiceAsync(
        IReadOnlyList<string> toEmails,
        WaveInvoiceResult invoice,
        string? customerName,
        byte[] pdfBytes,
        string pdfFileName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            return WaveSendResult.Failed("Mailgun is not configured.");

        var emails = toEmails
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (emails.Count == 0)
            return WaveSendResult.Failed("No contacts have an email address.");

        if (pdfBytes is not { Length: > 0 })
            return WaveSendResult.Failed("Ad-Rack invoice PDF was not generated.");

        var subject = InvoiceEmailTemplate.Apply(InvoiceEmailSubject(), invoice, customerName);
        var message = InvoiceEmailTemplate.Apply(InvoiceEmailMessage(), invoice, customerName);
        var attachmentName = string.IsNullOrWhiteSpace(pdfFileName)
            ? InvoicePdfStorageService.BuildFileName(invoice.InvoiceNumber)
            : Path.GetFileName(pdfFileName);

        var logoBytes = await ReadLogoBytesAsync(cancellationToken);
        var smtp = await SendViaSmtpAsync(emails, subject, message, pdfBytes, attachmentName, logoBytes, cancellationToken);
        if (smtp.Success)
            return smtp;

        var domain = ResolveDomain();
        var http = await PostMessageAsync(
            _options.BaseUrl,
            domain,
            emails,
            subject,
            message,
            pdfBytes,
            attachmentName,
            logoBytes,
            cancellationToken);
        if (http.Success)
            return http;

        if (LooksLikeAuthFailure(http.ErrorMessage)
            && !string.Equals(_options.BaseUrl, "https://api.eu.mailgun.net", StringComparison.OrdinalIgnoreCase))
        {
            http = await PostMessageAsync(
                "https://api.eu.mailgun.net",
                domain,
                emails,
                subject,
                message,
                pdfBytes,
                attachmentName,
                logoBytes,
                cancellationToken);
            if (http.Success)
                return http;
        }

        var mgDomain = domain.StartsWith("mg.", StringComparison.OrdinalIgnoreCase) ? domain : "mg." + domain;
        if (!string.Equals(mgDomain, domain, StringComparison.OrdinalIgnoreCase))
        {
            http = await PostMessageAsync(
                _options.BaseUrl,
                mgDomain,
                emails,
                subject,
                message,
                pdfBytes,
                attachmentName,
                logoBytes,
                cancellationToken);
            if (http.Success)
                return http;
        }

        return WaveSendResult.Failed(
            FirstNonEmpty(smtp.ErrorMessage, http.ErrorMessage) ?? "Mailgun invoice email failed.");
    }

    private async Task<WaveSendResult> SendViaSmtpAsync(
        IReadOnlyList<string> emails,
        string subject,
        string message,
        byte[] pdfBytes,
        string attachmentName,
        byte[]? logoBytes,
        CancellationToken cancellationToken)
    {
        var host = string.IsNullOrWhiteSpace(_options.SmtpHost) ? "smtp.mailgun.org" : _options.SmtpHost.Trim();
        var port = _options.SmtpPort > 0 ? _options.SmtpPort : 587;
        var user = string.IsNullOrWhiteSpace(_options.SmtpUser) ? FromAddress() : _options.SmtpUser.Trim();

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(FromAddress(), FromName()),
                Subject = subject,
                Body = message,
                IsBodyHtml = false
            };
            foreach (var email in emails)
                mail.To.Add(email);

            await using var pdfStream = new MemoryStream(pdfBytes);
            using var attachment = new Attachment(pdfStream, attachmentName, "application/pdf");
            mail.Attachments.Add(attachment);

            using var htmlView = AlternateView.CreateAlternateViewFromString(
                HtmlBody(message, logoBytes is { Length: > 0 }),
                Encoding.UTF8,
                MediaTypeNames.Text.Html);
            MemoryStream? logoStream = null;
            LinkedResource? logo = null;
            if (logoBytes is { Length: > 0 })
            {
                logoStream = new MemoryStream(logoBytes);
                logo = new LinkedResource(logoStream, new ContentType("image/png"))
                {
                    ContentId = LogoContentId,
                    TransferEncoding = TransferEncoding.Base64
                };
                htmlView.LinkedResources.Add(logo);
            }

            mail.AlternateViews.Add(htmlView);

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, _options.ApiKey!.Trim()),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            await client.SendMailAsync(mail, cancellationToken);
            logo?.Dispose();
            if (logoStream != null)
                await logoStream.DisposeAsync();
            _logger.LogInformation(
                "Mailed Ad-Rack invoice PDF {FileName} via Mailgun SMTP to {Recipients}.",
                attachmentName,
                string.Join(", ", emails));
            return WaveSendResult.Succeeded(emails);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Mailgun SMTP invoice email failed.");
            return WaveSendResult.Failed(ex.Message);
        }
    }

    private async Task<WaveSendResult> PostMessageAsync(
        string? baseUrl,
        string domain,
        IReadOnlyList<string> emails,
        string subject,
        string message,
        byte[] pdfBytes,
        string attachmentName,
        byte[]? logoBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl(baseUrl, domain));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes("api:" + _options.ApiKey!.Trim())));

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(FromHeader()), "from");
        foreach (var email in emails)
            content.Add(new StringContent(email), "to");
        content.Add(new StringContent(subject), "subject");
        content.Add(new StringContent(message), "text");
        content.Add(new StringContent(HtmlBody(message, logoBytes is { Length: > 0 })), "html");

        if (logoBytes is { Length: > 0 })
        {
            var logo = new ByteArrayContent(logoBytes);
            logo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(logo, "inline", LogoContentId);
        }

        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "attachment", attachmentName);
        request.Content = content;

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = ParseMessage(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
                _logger.LogWarning(
                    "Mailgun HTTP invoice email failed: {Status} {Error}",
                    (int)response.StatusCode,
                    error);
                return WaveSendResult.Failed(error);
            }

            _logger.LogInformation(
                "Mailed Ad-Rack invoice PDF {FileName} via Mailgun HTTP to {Recipients}.",
                attachmentName,
                string.Join(", ", emails));
            return WaveSendResult.Succeeded(emails);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Mailgun HTTP invoice email request failed.");
            return WaveSendResult.Failed(ex.Message);
        }
    }

    private static Uri MessagesUrl(string? baseUrl, string domain)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.mailgun.net" : baseUrl.Trim().TrimEnd('/');
        return new Uri($"{root}/v3/{domain}/messages");
    }

    private string ResolveDomain()
    {
        if (!string.IsNullOrWhiteSpace(_options.Domain))
            return _options.Domain.Trim();

        var from = FromAddress();
        var at = from.LastIndexOf('@');
        if (at >= 0 && at < from.Length - 1)
            return from[(at + 1)..].TrimEnd('>');

        return "ad-rack.net";
    }

    private string FromAddress()
    {
        var from = string.IsNullOrWhiteSpace(_options.From) ? "billing@ad-rack.net" : _options.From.Trim();
        var start = from.IndexOf('<');
        var end = from.IndexOf('>');
        if (start >= 0 && end > start)
            return from[(start + 1)..end].Trim();
        return from;
    }

    private string FromName() =>
        string.IsNullOrWhiteSpace(_options.FromName) ? "Ad-Rack Services LLC" : _options.FromName.Trim();

    private string FromHeader()
    {
        var from = FromAddress();
        return $"{FromName()} <{from}>";
    }

    private string InvoiceEmailSubject() =>
        string.IsNullOrWhiteSpace(_wave.InvoiceEmailSubject)
            ? WaveOptions.DefaultInvoiceEmailSubject
            : _wave.InvoiceEmailSubject;

    private string InvoiceEmailMessage() =>
        string.IsNullOrWhiteSpace(_wave.InvoiceEmailMessage)
            ? WaveOptions.DefaultInvoiceEmailMessage
            : _wave.InvoiceEmailMessage;

    private static bool LooksLikeAuthFailure(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && (error.Contains("401", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase));

    private async Task<byte[]?> ReadLogoBytesAsync(CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(_options.LogoUrl)
            ? "https://ad-rack.com/wp-content/uploads/2020/10/Ad-Rack-logo-lg.png"
            : _options.LogoUrl.Trim();
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes is { Length: > 0 })
                    return bytes;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Could not download invoice email logo from {LogoUrl}.", url);
        }

        if (File.Exists(_logoPath))
            return await File.ReadAllBytesAsync(_logoPath, cancellationToken);

        return null;
    }

    private static string HtmlBody(string text, bool includeLogo)
    {
        var encoded = Linkify(System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>\n", StringComparison.Ordinal));
        var logo = includeLogo
            ? $"<img src=\"cid:{LogoContentId}\" alt=\"Ad-Rack\" width=\"220\" style=\"display:block;margin:0 0 20px 0;border:0;outline:none;text-decoration:none;\">\n"
            : string.Empty;

        return $"""
            <div style="font-family:Arial, Helvetica, sans-serif; font-size:15px; line-height:1.45; color:#111;">
            {logo}<div>{encoded}</div>
            </div>
            """;
    }

    private static string Linkify(string encoded)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            encoded,
            @"https://[^\s<]+",
            match =>
            {
                var href = match.Value.TrimEnd('.', ',', ';');
                return $"<a href=\"{href}\" style=\"color:#2563EB;text-decoration:underline;\">{href}</a>";
            });
    }

    private static string? ParseMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch (JsonException)
        {
        }

        return body.Trim();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
