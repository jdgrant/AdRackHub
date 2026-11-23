using System.Net;
using System.Text;
using System.Text.Json;
using AdRackHub.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AdRackHub.Tests;

public class McpOAuthRedirectTests
{
    [Fact]
    public void ChatGptRedirectsAreAllowed()
    {
        Assert.True(Allowed("https://chatgpt.com/connector_platform_oauth_redirect"));
        Assert.True(Allowed("https://chatgpt.com/connector/oauth/callback-1"));
        Assert.False(Allowed("https://chatgpt.com/connector/oauth/callback-1/extra"));
        Assert.False(Allowed("https://evil.example/connector_platform_oauth_redirect"));
    }

    [Fact]
    public void CursorRedirectsAreAllowed()
    {
        Assert.True(Allowed("https://cursor.com/oauth/callback"));
        Assert.True(Allowed("https://www.cursor.com/api/auth/callback"));
        Assert.True(Allowed("https://api.cursor.com/oauth/callback"));
        Assert.True(Allowed("https://agents.cursor.com/oauth/callback"));
        Assert.False(Allowed("https://cursor.com.evil.example/oauth/callback"));
        Assert.False(Allowed("https://notcursor.com/oauth/callback"));
    }

    [Fact]
    public void LocalhostRedirectsAreAllowed()
    {
        Assert.True(Allowed("http://localhost/callback"));
        Assert.True(Allowed("http://localhost:8787/oauth/callback"));
        Assert.True(Allowed("http://127.0.0.1/callback"));
        Assert.True(Allowed("http://127.0.0.1:5281/callback"));
        Assert.False(Allowed("http://localhost.evil.example/callback"));
    }

    [Fact]
    public void RandomHttpsHostIsRejected()
    {
        Assert.False(Allowed("https://evil.example/callback"));
        Assert.False(Allowed("https://example.com/connector_platform_oauth_redirect"));
    }

    [Fact]
    public void HttpNonLocalhostIsRejected()
    {
        Assert.False(Allowed("http://evil.example/callback"));
        Assert.False(Allowed("http://cursor.com/oauth/callback"));
        Assert.False(Allowed("http://chatgpt.com/connector_platform_oauth_redirect"));
        Assert.False(McpOAuthRules.RedirectAllowed(
            "http://evil.example/callback",
            ["http://evil.example/"]));
    }

    [Fact]
    public void RegistrationLogsTheRejectedRedirectAndClientNameOnly()
    {
        var logger = new WarningLog();
        var oauth = Service(logger);
        var result = oauth.Register(JsonSerializer.Deserialize<JsonElement>("""
            {"client_name":"Grok Bot","redirect_uris":["https://evil.example/callback"],"token_endpoint_auth_method":"none","access_token":"should-not-be-logged"}
            """));

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_redirect_uri", result.Error);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("https://evil.example/callback", warning, StringComparison.Ordinal);
        Assert.Contains("Grok Bot", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-be-logged", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataDocumentRedirectsUseTheSameAllowlist()
    {
        var allowedLog = new WarningLog();
        var allowed = Service(allowedLog, new JsonHandler("""
            {"client_id":"https://cursor.com/oauth/client","client_name":"Cursor","redirect_uris":["https://cursor.com/oauth/callback"],"token_endpoint_auth_method":"none"}
            """));
        var decision = await allowed.ValidateAuthorizationAsync(Request(
            "https://cursor.com/oauth/client",
            "https://cursor.com/oauth/callback"), false, CancellationToken.None);
        Assert.NotNull(decision.Consent);
        Assert.Equal("Cursor", decision.Consent!.ClientName);
        Assert.Empty(allowedLog.Warnings);

        var rejectedLog = new WarningLog();
        var rejected = Service(rejectedLog, new JsonHandler("""
            {"client_id":"https://www.cursor.com/oauth/client","client_name":"Cursor","redirect_uris":["https://evil.example/steal"],"token_endpoint_auth_method":"none","access_token":"should-not-be-logged"}
            """));
        var blocked = await rejected.ValidateAuthorizationAsync(Request(
            "https://www.cursor.com/oauth/client",
            "https://cursor.com/oauth/callback"), false, CancellationToken.None);
        Assert.Null(blocked.Consent);
        var warning = Assert.Single(rejectedLog.Warnings);
        Assert.Contains("https://evil.example/steal", warning, StringComparison.Ordinal);
        Assert.Contains("Cursor", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-be-logged", warning, StringComparison.Ordinal);

        Assert.True(McpOAuthRules.ClientMetadataUrlAllowed("https://cursor.com/oauth/client", McpOAuthRules.DefaultRedirectUriPrefixes, false));
        Assert.True(McpOAuthRules.ClientMetadataUrlAllowed("https://api.cursor.com/oauth/client", McpOAuthRules.DefaultRedirectUriPrefixes, false));
        Assert.False(McpOAuthRules.ClientMetadataUrlAllowed("https://evil.example/oauth/client", McpOAuthRules.DefaultRedirectUriPrefixes, false));
        Assert.False(McpOAuthRules.ClientMetadataUrlAllowed("http://127.0.0.1/oauth/client", McpOAuthRules.DefaultRedirectUriPrefixes, false));
        Assert.True(McpOAuthRules.ClientMetadataUrlAllowed("http://127.0.0.1/oauth/client", McpOAuthRules.DefaultRedirectUriPrefixes, true));
    }

    [Fact]
    public async Task AuthorizeRejectsAnAllowedRedirectThatWasNotRegistered()
    {
        var oauth = Service(new WarningLog());
        var registered = oauth.Register(JsonSerializer.Deserialize<JsonElement>("""
            {"client_name":"Cursor","redirect_uris":["https://cursor.com/oauth/callback"],"token_endpoint_auth_method":"none"}
            """));
        Assert.True(registered.Succeeded);

        var decision = await oauth.ValidateAuthorizationAsync(Request(
            registered.Client!.ClientId,
            "https://www.cursor.com/oauth/callback"), false, CancellationToken.None);

        Assert.Null(decision.Consent);
        Assert.Contains("not registered", decision.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Allowed(string uri) =>
        McpOAuthRules.RedirectAllowed(uri, McpOAuthRules.DefaultRedirectUriPrefixes);

    private static McpAuthorizationRequest Request(string clientId, string redirectUri)
    {
        var resource = "https://hub.ad-rack.net/mcp";
        return new McpAuthorizationRequest
        {
            ResponseType = "code",
            ClientId = clientId,
            RedirectUri = redirectUri,
            CodeChallenge = new string('a', 43),
            CodeChallengeMethod = "S256",
            Resource = resource,
            ExpectedResource = resource
        };
    }

    private static McpOAuthService Service(WarningLog logger, HttpMessageHandler? handler = null)
    {
        return new McpOAuthService(
            new McpOAuthStore(null, new WarningLog<McpOAuthStore>()),
            new StubClientFactory(handler ?? new HttpClientHandler()),
            new AllowAdmin(),
            logger,
            Options.Create(new McpOAuthOptions()));
    }

    private sealed class AllowAdmin : IMcpUserDirectory
    {
        public Task<McpDirectoryUser?> FindAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<McpDirectoryUser?>(new McpDirectoryUser(userId, "jon@ad-rack.net", true, true));
    }

    private sealed class StubClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string _body;

        public JsonHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class WarningLog : ILogger<McpOAuthService>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class WarningLog<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
