using System.Net.Http.Headers;
using System.Text.Json;
using AdRackHub.ViewModels;

namespace AdRackHub.Services;

public sealed class McpOAuthService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    private readonly McpOAuthStore _store;
    private readonly IHttpClientFactory _http;
    private readonly IMcpUserDirectory _users;
    private readonly ILogger<McpOAuthService> _logger;
    private readonly Dictionary<string, (DateTimeOffset Expires, McpRegisteredClient Client)> _metadataCache = new();

    public McpOAuthService(
        McpOAuthStore store,
        IHttpClientFactory http,
        IMcpUserDirectory users,
        ILogger<McpOAuthService> logger)
    {
        _store = store;
        _http = http;
        _users = users;
        _logger = logger;
    }

    public async Task<McpAuthorizationDecision> ValidateAuthorizationAsync(
        McpAuthorizationRequest request,
        bool loopbackIssuer,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
            return McpAuthorizationDecision.PageError("response_type must be code.");

        if (string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 512)
            return McpAuthorizationDecision.PageError("client_id is missing.");

        if (!McpOAuthRules.RedirectAllowed(request.RedirectUri, loopbackIssuer))
            return McpAuthorizationDecision.PageError("redirect_uri is not an allowed ChatGPT callback.");

        var client = await ResolveClientAsync(request.ClientId, loopbackIssuer, cancellationToken);
        if (client == null || !client.RedirectUris.Contains(request.RedirectUri, StringComparer.Ordinal))
            return McpAuthorizationDecision.PageError("This ChatGPT connection is not registered for that callback.");

        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.CodeChallenge)
            || request.CodeChallenge.Length is < 43 or > 128)
        {
            return McpAuthorizationDecision.RedirectError(request, "invalid_request", "PKCE S256 code_challenge is required.");
        }

        if (!string.Equals(request.Resource, request.ExpectedResource, StringComparison.Ordinal))
            return McpAuthorizationDecision.RedirectError(request, "invalid_target", "resource must be the Ad-Rack MCP URL.");

        if (request.State?.Length > 512)
            return McpAuthorizationDecision.RedirectError(request, "invalid_request", "state is too long.");

        if (!McpOAuthRules.TryNormalizeScope(request.Scope, out var scope, out var scopeError))
            return McpAuthorizationDecision.RedirectError(request, "invalid_scope", scopeError!);

        return McpAuthorizationDecision.Allow(new McpConsentViewModel
        {
            ClientName = client.ClientName,
            ClientId = request.ClientId,
            RedirectUri = request.RedirectUri,
            State = request.State,
            Scope = scope,
            CodeChallenge = request.CodeChallenge,
            Resource = request.Resource,
            CanRead = scope.Contains("customers:read", StringComparison.Ordinal),
            CanWrite = scope.Contains("customers:write", StringComparison.Ordinal)
        });
    }

    public string CreateAuthorizationCode(McpConsentViewModel consent, McpDirectoryUser user)
    {
        var raw = _store.SaveAuthorizationCode(new McpAuthorizationCode
        {
            ClientId = consent.ClientId,
            RedirectUri = consent.RedirectUri,
            CodeChallenge = consent.CodeChallenge,
            UserId = user.Id,
            Email = user.Email,
            EmailVerified = user.EmailVerified,
            Resource = consent.Resource,
            Scope = consent.Scope,
            ExpiresAt = DateTimeOffset.UtcNow.Add(CodeLifetime)
        });
        _logger.LogInformation("MCP authorization code issued for user {UserId}.", user.Id);
        return raw;
    }

    public async Task<McpTokenResult> ExchangeAuthorizationCodeAsync(
        string? code,
        string? clientId,
        string? redirectUri,
        string? codeVerifier,
        string? resource,
        bool clientAuthenticated,
        CancellationToken cancellationToken)
    {
        if (clientAuthenticated)
            return McpTokenResult.Failed("invalid_client", "Send the authorization code with PKCE. This server does not accept a client secret or private_key_jwt.");

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
            return McpTokenResult.Failed("invalid_request", "code, client_id, and redirect_uri are required.");

        var stored = _store.TakeAuthorizationCode(code);
        if (stored == null
            || !string.Equals(stored.ClientId, clientId, StringComparison.Ordinal)
            || !string.Equals(stored.RedirectUri, redirectUri, StringComparison.Ordinal)
            || !string.Equals(stored.Resource, resource, StringComparison.Ordinal))
        {
            _logger.LogInformation("MCP token request rejected: invalid_grant.");
            return McpTokenResult.Failed("invalid_grant", "The authorization code is invalid or expired.");
        }

        if (!McpOAuthStore.PkceMatches(codeVerifier ?? "", stored.CodeChallenge))
        {
            _logger.LogInformation("MCP token request rejected: PKCE verification failed.");
            return McpTokenResult.Failed("invalid_grant", "code_verifier does not match the code challenge.");
        }

        var user = await _users.FindAsync(stored.UserId, cancellationToken);
        if (user == null || !user.IsAdmin)
            return McpTokenResult.Failed("invalid_grant", "The AdRackHub admin account is no longer allowed to connect.");

        return IssueTokens(stored.ClientId, user, stored.Resource, stored.Scope);
    }

    public async Task<McpTokenResult> RefreshAsync(
        string? refreshToken,
        string? clientId,
        string? resource,
        bool clientAuthenticated,
        CancellationToken cancellationToken)
    {
        if (clientAuthenticated)
            return McpTokenResult.Failed("invalid_client", "Send the refresh token without a client secret. This server accepts PKCE public clients only.");

        if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(clientId))
            return McpTokenResult.Failed("invalid_request", "refresh_token and client_id are required.");

        var stored = _store.TakeRefreshToken(refreshToken);
        if (stored == null
            || !string.Equals(stored.ClientId, clientId, StringComparison.Ordinal)
            || !string.Equals(stored.Resource, resource, StringComparison.Ordinal))
        {
            _logger.LogInformation("MCP refresh rejected: invalid_grant.");
            return McpTokenResult.Failed("invalid_grant", "The refresh token is invalid or expired.");
        }

        var user = await _users.FindAsync(stored.UserId, cancellationToken);
        if (user == null || !user.IsAdmin)
            return McpTokenResult.Failed("invalid_grant", "The AdRackHub admin account is no longer allowed to connect.");

        return IssueTokens(clientId, user, stored.Resource, stored.Scope);
    }

    public McpAccessPrincipal? ValidateAccessToken(string rawToken, string resource)
    {
        var token = _store.FindAccessToken(rawToken);
        if (token == null || !string.Equals(token.Resource, resource, StringComparison.Ordinal))
            return null;

        return new McpAccessPrincipal(token.UserId, token.Email, token.EmailVerified, token.Scope);
    }

    public McpRegistrationResult Register(JsonElement body, bool loopbackIssuer)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return McpRegistrationResult.Failed("invalid_client_metadata", "Registration body must be a JSON object.");

        var method = StringValue(body, "token_endpoint_auth_method");
        if (!string.IsNullOrEmpty(method) && !string.Equals(method, "none", StringComparison.Ordinal))
            return McpRegistrationResult.Failed("invalid_client_metadata", "token_endpoint_auth_method must be none.");

        if (!TryStringArray(body, "redirect_uris", out var redirects) || redirects.Count == 0)
            return McpRegistrationResult.Failed("invalid_redirect_uri", "redirect_uris must list at least one callback.");

        if (redirects.Any(uri => !McpOAuthRules.RedirectAllowed(uri, loopbackIssuer)))
            return McpRegistrationResult.Failed("invalid_redirect_uri", "A redirect URI is not an allowed ChatGPT callback.");

        var client = new McpRegisteredClient
        {
            ClientId = "adrackhub-" + Guid.NewGuid().ToString("N"),
            ClientName = Trim(StringValue(body, "client_name"), 120) ?? "ChatGPT",
            RedirectUris = redirects
        };
        _store.SaveClient(client);
        _logger.LogInformation("MCP OAuth client registered.");
        return McpRegistrationResult.Created(client);
    }

    public static string AppendQuery(string redirectUri, IEnumerable<KeyValuePair<string, string?>> pairs)
    {
        var builder = new UriBuilder(redirectUri);
        var query = pairs
            .Where(pair => pair.Value != null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        var extra = string.Join("&", query);
        var existing = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existing) ? extra : existing + "&" + extra;
        return builder.Uri.AbsoluteUri;
    }

    private McpTokenResult IssueTokens(string clientId, McpDirectoryUser user, string resource, string scope)
    {
        var access = _store.SaveAccessToken(new McpAccessToken
        {
            UserId = user.Id,
            Email = user.Email,
            EmailVerified = user.EmailVerified,
            Resource = resource,
            Scope = scope,
            ExpiresAt = DateTimeOffset.UtcNow.Add(AccessLifetime)
        });
        var refresh = _store.SaveRefreshToken(new McpRefreshToken
        {
            ClientId = clientId,
            UserId = user.Id,
            Email = user.Email,
            EmailVerified = user.EmailVerified,
            Resource = resource,
            Scope = scope,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshLifetime)
        });
        _logger.LogInformation("MCP access token issued for user {UserId}.", user.Id);
        return McpTokenResult.Issued(access, refresh, scope, (int)AccessLifetime.TotalSeconds);
    }

    private async Task<McpRegisteredClient?> ResolveClientAsync(string clientId, bool loopbackIssuer, CancellationToken cancellationToken)
    {
        if (clientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || clientId.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return await FetchMetadataDocumentAsync(clientId, loopbackIssuer, cancellationToken);

        return _store.FindClient(clientId);
    }

    private async Task<McpRegisteredClient?> FetchMetadataDocumentAsync(
        string clientId,
        bool loopbackIssuer,
        CancellationToken cancellationToken)
    {
        if (!McpOAuthRules.ClientMetadataUrlAllowed(clientId, loopbackIssuer))
            return null;

        lock (_metadataCache)
        {
            if (_metadataCache.TryGetValue(clientId, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
                return cached.Client;
        }

        var client = _http.CreateClient("McpClientMetadata");
        using var request = new HttpRequestMessage(HttpMethod.Get, clientId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && !mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            total += read;
            if (total > 65536)
                return null;
            limited.Write(buffer, 0, read);
        }

        using var document = JsonDocument.Parse(limited.ToArray());
        var root = document.RootElement;
        if (!string.Equals(StringValue(root, "client_id"), clientId, StringComparison.Ordinal))
            return null;
        if (!AllowsPublicClient(root))
            return null;
        if (!TryStringArray(root, "redirect_uris", out var redirects))
            return null;
        if (redirects.Any(uri => !McpOAuthRules.RedirectAllowed(uri, loopbackIssuer)))
            return null;

        var registered = new McpRegisteredClient
        {
            ClientId = clientId,
            ClientName = Trim(StringValue(root, "client_name"), 120) ?? "ChatGPT",
            RedirectUris = redirects
        };
        lock (_metadataCache)
            _metadataCache[clientId] = (DateTimeOffset.UtcNow.AddMinutes(10), registered);
        return registered;
    }

    private static bool AllowsPublicClient(JsonElement body)
    {
        if (TryStringArray(body, "token_endpoint_auth_methods_supported", out var methods))
            return methods.Contains("none", StringComparer.Ordinal);

        var method = StringValue(body, "token_endpoint_auth_method");
        return string.IsNullOrEmpty(method) || string.Equals(method, "none", StringComparison.Ordinal);
    }

    private static bool TryStringArray(JsonElement body, string name, out List<string> values)
    {
        values = new List<string>();
        if (!AgentJson.Try(body, name, out var element) || element.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return false;
            values.Add(text);
        }

        return values.Count > 0;
    }

    private static string? StringValue(JsonElement body, string name)
    {
        if (!AgentJson.Try(body, name, out var element) || element.ValueKind != JsonValueKind.String)
            return null;
        return element.GetString();
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

public sealed class McpAuthorizationRequest
{
    public string? ResponseType { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string? State { get; init; }
    public string? Scope { get; init; }
    public string? CodeChallenge { get; init; }
    public string? CodeChallengeMethod { get; init; }
    public string Resource { get; init; } = string.Empty;
    public string ExpectedResource { get; init; } = string.Empty;
}

public sealed class McpAuthorizationDecision
{
    public McpConsentViewModel? Consent { get; init; }
    public bool CanRedirect { get; init; }
    public string? Error { get; init; }
    public string? Description { get; init; }
    public string? RedirectUri { get; init; }
    public string? State { get; init; }

    public static McpAuthorizationDecision Allow(McpConsentViewModel consent) => new() { Consent = consent };

    public static McpAuthorizationDecision PageError(string description) =>
        new() { Description = description };

    public static McpAuthorizationDecision RedirectError(McpAuthorizationRequest request, string error, string description) =>
        new()
        {
            CanRedirect = true,
            Error = error,
            Description = description,
            RedirectUri = request.RedirectUri,
            State = request.State
        };
}

public sealed class McpTokenResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? Description { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? Scope { get; init; }
    public int ExpiresIn { get; init; }

    public static McpTokenResult Issued(string access, string refresh, string scope, int expiresIn) =>
        new()
        {
            Succeeded = true,
            AccessToken = access,
            RefreshToken = refresh,
            Scope = scope,
            ExpiresIn = expiresIn
        };

    public static McpTokenResult Failed(string error, string description) =>
        new() { Error = error, Description = description };
}

public sealed class McpRegistrationResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? Description { get; init; }
    public McpRegisteredClient? Client { get; init; }

    public static McpRegistrationResult Created(McpRegisteredClient client) =>
        new() { Succeeded = true, Client = client };

    public static McpRegistrationResult Failed(string error, string description) =>
        new() { Error = error, Description = description };
}

public sealed record McpAccessPrincipal(string UserId, string Email, bool EmailVerified, string Scope);
