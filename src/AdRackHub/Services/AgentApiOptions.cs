namespace AdRackHub.Services;

public class AgentApiOptions
{
    public const string SectionName = "AgentApi";

    /// <summary>
    /// Bearer token ChatGPT and other agents send as Authorization: Bearer {ApiKey}
    /// or X-AdRackHub-Api-Key. Leave empty to disable the data API.
    /// </summary>
    public string? ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
