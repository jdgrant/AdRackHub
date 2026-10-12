using System.Security.Claims;
using System.Text.Encodings.Web;
using AdRackHub.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public sealed class McpBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "McpBearer";

    private readonly McpOAuthStore _store;
    private readonly IOptions<WaveOptions> _wave;

    public McpBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        McpOAuthStore store,
        IOptions<WaveOptions> wave)
        : base(options, logger, encoder)
    {
        _store = store;
        _wave = wave;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var raw = header["Bearer ".Length..].Trim();
        if (raw.Length == 0)
            return Task.FromResult(AuthenticateResult.Fail("Missing bearer token."));

        var resource = McpPublicUrl.Resource(McpPublicUrl.Base(Request, _wave.Value.PublicBaseUrl));
        var token = _store.FindAccessToken(raw);
        if (token == null || !string.Equals(token.Resource, resource, StringComparison.Ordinal))
        {
            Logger.LogInformation("MCP bearer token was rejected.");
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.UserId),
            new(ClaimTypes.Email, token.Email),
            new(ClaimTypes.Role, AppRoles.Admin)
        };
        foreach (var scope in token.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            claims.Add(new Claim(McpPolicies.ScopeClaim, scope));

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var metadata = McpPublicUrl.ResourceMetadata(McpPublicUrl.Base(Request, _wave.Value.PublicBaseUrl));
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate =
            $"Bearer realm=\"AdRackHub\", resource_metadata=\"{metadata}\", scope=\"{McpPolicies.ReadScope} {McpPolicies.WriteScope}\"";
        return Task.CompletedTask;
    }
}
