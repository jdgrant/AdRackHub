namespace AdRackHub.ViewModels;

public class AgentDocsPage
{
    public string Html { get; set; } = string.Empty;
    public string MarkdownUrl { get; set; } = string.Empty;
    public string OpenApiUrl { get; set; } = string.Empty;
    public string McpUrl { get; set; } = string.Empty;
    public bool KeyConfigured { get; set; }
    public bool ShowKey { get; set; }
    public bool ShowSetup { get; set; }
    public string? ApiKey { get; set; }
}
