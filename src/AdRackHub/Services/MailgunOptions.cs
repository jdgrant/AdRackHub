namespace AdRackHub.Services;

public class MailgunOptions
{
    public const string SectionName = "Mailgun";

    public string? ApiKey { get; set; }
    public string Domain { get; set; } = "ad-rack.net";
    public string BaseUrl { get; set; } = "https://api.mailgun.net";
    public string From { get; set; } = "billing@ad-rack.net";
    public string FromName { get; set; } = "Ad-Rack Services LLC";
    public string SmtpHost { get; set; } = "smtp.mailgun.org";
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string LogoUrl { get; set; } = "https://ad-rack.com/wp-content/uploads/2020/10/Ad-Rack-logo-lg.png";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
