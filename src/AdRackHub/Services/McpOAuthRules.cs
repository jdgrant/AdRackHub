namespace AdRackHub.Services;

public static class McpOAuthRules
{
    public const string DefaultScope = "customers:read customers:write";

    private static readonly HashSet<string> SupportedScopes = new(StringComparer.Ordinal)
    {
        "customers:read",
        "customers:write",
        "openid",
        "email"
    };

    public static bool RedirectAllowed(string uri, bool loopbackIssuer)
    {
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > 512 || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;
        if (!string.IsNullOrEmpty(parsed.Fragment))
            return false;

        if (parsed.Scheme == Uri.UriSchemeHttps
            && parsed.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            if (parsed.AbsolutePath.Equals("/connector_platform_oauth_redirect", StringComparison.Ordinal))
                return true;

            const string callbackPrefix = "/connector/oauth/";
            if (!parsed.AbsolutePath.StartsWith(callbackPrefix, StringComparison.Ordinal))
                return false;

            var id = parsed.AbsolutePath[callbackPrefix.Length..];
            return id.Length is > 0 and <= 128
                && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        }

        return loopbackIssuer
            && parsed.Scheme == Uri.UriSchemeHttp
            && McpPublicUrl.IsLoopback(parsed.Host)
            && parsed.Port > 0;
    }

    public static bool ClientMetadataUrlAllowed(string clientId, bool loopbackIssuer)
    {
        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var parsed))
            return false;
        if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
            return false;

        if (parsed.Scheme == Uri.UriSchemeHttps
            && (parsed.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || parsed.Host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase)))
            return true;

        return loopbackIssuer
            && parsed.Scheme == Uri.UriSchemeHttp
            && McpPublicUrl.IsLoopback(parsed.Host);
    }

    public static bool TryNormalizeScope(string? requested, out string scope, out string? error)
    {
        var parts = string.IsNullOrWhiteSpace(requested)
            ? DefaultScope.Split(' ')
            : requested.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0 || parts.Any(part => !SupportedScopes.Contains(part)))
        {
            scope = string.Empty;
            error = "scope must be customers:read, customers:write, openid, or email.";
            return false;
        }

        if (!parts.Contains("customers:read") && !parts.Contains("customers:write"))
        {
            scope = string.Empty;
            error = "scope must include customers:read or customers:write.";
            return false;
        }

        scope = string.Join(' ', parts.Distinct(StringComparer.Ordinal));
        error = null;
        return true;
    }
}
