namespace AdRackHub.Services;

public static class McpPublicUrl
{
    public static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || host.Equals("::1", StringComparison.OrdinalIgnoreCase);

    public static string Base(HttpRequest request, string? configuredPublicBase)
    {
        if (IsLoopback(request.Host.Host))
            return $"{request.Scheme}://{request.Host}".TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(configuredPublicBase))
            return configuredPublicBase.TrimEnd('/');

        return $"{request.Scheme}://{request.Host}".TrimEnd('/');
    }

    public static string Resource(string baseUrl) => $"{baseUrl.TrimEnd('/')}/mcp";

    public static string ResourceMetadata(string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}/.well-known/oauth-protected-resource/mcp";
}
