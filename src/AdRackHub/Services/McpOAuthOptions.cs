namespace AdRackHub.Services;

public sealed class McpOAuthOptions
{
    public const string SectionName = "McpOAuth";

    /// <summary>
    /// Redirect URIs that may be registered or used at the authorize and token endpoints.
    /// Each entry is a prefix. https://*.cursor.com/ matches any cursor.com subdomain.
    /// Non-loopback hosts still have to use https, even if a prefix says otherwise.
    /// </summary>
    public string[] AllowedRedirectUriPrefixes { get; set; } = McpOAuthRules.DefaultRedirectUriPrefixes.ToArray();
}
