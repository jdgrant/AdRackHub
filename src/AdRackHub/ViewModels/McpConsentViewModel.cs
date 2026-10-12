namespace AdRackHub.ViewModels;

public class McpConsentViewModel
{
    public string ClientName { get; set; } = "ChatGPT";
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
}
