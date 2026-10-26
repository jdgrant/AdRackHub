namespace AdRackHub.Services;

public class AgentApiOptions
{
    public const string SectionName = "AgentApi";

    /// <summary>
    /// Bearer token ChatGPT and other agents send as Authorization: Bearer {ApiKey}
    /// or X-AdRackHub-Api-Key. Leave empty to disable the data API.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// When true, the agent API may replace the saved invoice reminder template.
    /// Leave false so agents can only read and render it. Set this in
    /// appsettings.Production.json on the server. A deploy does not upload that file.
    /// </summary>
    public bool CanManageInvoiceReminderTemplate { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
