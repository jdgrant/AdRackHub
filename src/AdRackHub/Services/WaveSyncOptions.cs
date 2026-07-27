namespace AdRackHub.Services;

public class WaveSyncOptions
{
    public const string SectionName = "WaveSync";

    /// <summary>Shared secret Zapier sends in X-AdRackHub-Webhook-Secret when posting to AdRackHub.</summary>
    public string? InboundWebhookSecret { get; set; }

    /// <summary>Zapier Catch Hook URL — AdRackHub POSTs here when customers or billing contracts are created.</summary>
    public string? OutboundWebhookUrl { get; set; }

    /// <summary>When true and Wave API credentials are set, new customers are created in Wave directly (in addition to the outbound webhook).</summary>
    public bool PushCustomersToWaveApi { get; set; } = true;
}
