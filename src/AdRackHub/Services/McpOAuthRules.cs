namespace AdRackHub.Services;

public static class McpOAuthRules
{
    public const string DefaultScope = "customers:read customers:write";

    public static readonly string[] DefaultRedirectUriPrefixes =
    [
        "https://chatgpt.com/connector_platform_oauth_redirect",
        "https://chatgpt.com/connector/oauth/",
        "https://cursor.com/",
        "https://www.cursor.com/",
        "https://*.cursor.com/",
        "http://localhost",
        "http://127.0.0.1"
    ];

    private static readonly HashSet<string> SupportedScopes = new(StringComparer.Ordinal)
    {
        "customers:read",
        "customers:write",
        "openid",
        "email"
    };

    public static bool RedirectAllowed(string uri, IReadOnlyList<string>? prefixes)
    {
        if (!TryParseCallback(uri, out var parsed))
            return false;
        if (!SchemeAllowed(parsed))
            return false;

        var list = prefixes is { Count: > 0 } ? prefixes : DefaultRedirectUriPrefixes;
        foreach (var prefix in list)
        {
            if (MatchesPrefix(parsed, prefix))
                return true;
        }

        return false;
    }

    public static bool ClientMetadataUrlAllowed(string clientId, IReadOnlyList<string>? prefixes, bool loopbackIssuer)
    {
        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var parsed))
            return false;
        if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment) || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        if (parsed.Scheme == Uri.UriSchemeHttps)
        {
            if (parsed.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || parsed.Host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase))
                return true;

            return HostCoveredByAllowlist(parsed.Host, prefixes);
        }

        return loopbackIssuer
            && parsed.Scheme == Uri.UriSchemeHttp
            && McpPublicUrl.IsLoopback(parsed.Host)
            && HostCoveredByAllowlist(parsed.Host, prefixes);
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

    private static bool SchemeAllowed(Uri uri)
    {
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && McpPublicUrl.IsLoopback(uri.Host);
    }

    private static bool TryParseCallback(string uri, out Uri parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > 512 || !Uri.TryCreate(uri, UriKind.Absolute, out var created))
            return false;
        if (!string.IsNullOrEmpty(created.Fragment) || !string.IsNullOrEmpty(created.UserInfo))
            return false;

        parsed = created;
        return true;
    }

    private static bool MatchesPrefix(Uri uri, string prefix)
    {
        if (!TryParsePrefix(prefix, out var pattern))
            return false;
        if (!uri.Scheme.Equals(pattern.Scheme, StringComparison.OrdinalIgnoreCase))
            return false;
        if (pattern.Port is int port && uri.Port != port)
            return false;
        if (!HostMatches(uri.Host, pattern))
            return false;
        if (!PathMatches(uri.AbsolutePath, pattern))
            return false;

        if (pattern.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
            && pattern.Path.Equals("/connector/oauth/", StringComparison.Ordinal))
        {
            var id = uri.AbsolutePath["/connector/oauth/".Length..];
            return id.Length is > 0 and <= 128
                && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        }

        return true;
    }

    private static bool PathMatches(string absolutePath, PrefixPattern pattern)
    {
        if (pattern.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
            && pattern.Path.Equals("/connector_platform_oauth_redirect", StringComparison.Ordinal))
            return absolutePath.Equals("/connector_platform_oauth_redirect", StringComparison.Ordinal);

        if (string.IsNullOrEmpty(pattern.Path) || pattern.Path == "/")
            return true;
        if (absolutePath.Equals(pattern.Path, StringComparison.Ordinal))
            return true;

        var boundary = pattern.Path.EndsWith('/') ? pattern.Path : pattern.Path + "/";
        return absolutePath.StartsWith(boundary, StringComparison.Ordinal);
    }

    private static bool HostCoveredByAllowlist(string host, IReadOnlyList<string>? prefixes)
    {
        var list = prefixes is { Count: > 0 } ? prefixes : DefaultRedirectUriPrefixes;
        foreach (var prefix in list)
        {
            if (!TryParsePrefix(prefix, out var pattern))
                continue;
            if (HostMatches(host, pattern))
                return true;
        }

        return false;
    }

    private static bool HostMatches(string host, PrefixPattern pattern)
    {
        if (pattern.WildcardHost)
        {
            var suffix = "." + pattern.Host;
            if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || host.Length <= suffix.Length)
                return false;

            var label = host[..^suffix.Length];
            return label.Length > 0
                && !label.StartsWith('.')
                && !label.EndsWith('.')
                && label.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');
        }

        return host.Equals(pattern.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParsePrefix(string prefix, out PrefixPattern pattern)
    {
        pattern = default;
        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        var wildcard = prefix.IndexOf("://*.", StringComparison.OrdinalIgnoreCase);
        if (wildcard > 0)
        {
            var scheme = prefix[..wildcard];
            var rest = prefix[(wildcard + "://*.".Length)..];
            var slash = rest.IndexOf('/');
            var host = slash >= 0 ? rest[..slash] : rest;
            var path = slash >= 0 ? rest[slash..] : "/";
            if (host.Length == 0 || host.Contains('*') || path.Contains('*'))
                return false;
            if (!scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                return false;

            pattern = new PrefixPattern(scheme, host, true, path.Length == 0 ? "/" : path, null);
            return true;
        }

        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
            return false;

        pattern = new PrefixPattern(
            parsed.Scheme,
            parsed.IdnHost,
            false,
            parsed.AbsolutePath,
            parsed.IsDefaultPort ? null : parsed.Port);
        return true;
    }

    private readonly record struct PrefixPattern(string Scheme, string Host, bool WildcardHost, string Path, int? Port);
}
