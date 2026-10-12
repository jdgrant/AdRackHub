using System.Text.Json;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdRackHub.Controllers;

[AllowAnonymous]
public class McpOAuthController : Controller
{
    private readonly McpOAuthService _oauth;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IOptions<WaveOptions> _wave;

    public McpOAuthController(McpOAuthService oauth, UserManager<ApplicationUser> users, IOptions<WaveOptions> wave)
    {
        _oauth = oauth;
        _users = users;
        _wave = wave;
    }

    [HttpGet("/oauth/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var decision = await CheckAsync(cancellationToken);
        if (decision.Consent == null)
            return decision.CanRedirect
                ? Redirect(ErrorRedirect(decision))
                : View("Error", decision.Description ?? "The connection request is invalid.");

        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account", new { returnUrl = Request.Path + Request.QueryString });

        if (!User.IsInRole(AppRoles.Admin))
            return View("Denied");

        return View(decision.Consent);
    }

    [HttpPost("/oauth/authorize")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AuthorizeDecision(McpConsentViewModel consent, string decision, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account", new { returnUrl = "/oauth/authorize" });

        var user = await _users.GetUserAsync(User);
        if (user == null || string.IsNullOrWhiteSpace(user.Email) || !await _users.IsInRoleAsync(user, AppRoles.Admin))
            return View("Denied");

        var check = await _oauth.ValidateAuthorizationAsync(new McpAuthorizationRequest
        {
            ResponseType = "code",
            ClientId = consent.ClientId,
            RedirectUri = consent.RedirectUri,
            State = consent.State,
            Scope = consent.Scope,
            CodeChallenge = consent.CodeChallenge,
            CodeChallengeMethod = "S256",
            Resource = consent.Resource,
            ExpectedResource = Urls().Resource
        }, Loopback(), cancellationToken);

        if (check.Consent == null)
            return check.CanRedirect ? Redirect(ErrorRedirect(check)) : View("Error", check.Description ?? "The connection request is invalid.");

        var issuer = Urls().Issuer;
        if (!string.Equals(decision, "allow", StringComparison.OrdinalIgnoreCase))
        {
            return Redirect(McpOAuthService.AppendQuery(check.Consent.RedirectUri, new KeyValuePair<string, string?>[]
            {
                new("error", "access_denied"),
                new("state", check.Consent.State),
                new("iss", issuer)
            }));
        }

        var code = _oauth.CreateAuthorizationCode(check.Consent, new McpDirectoryUser(user.Id, user.Email, true, true));
        return Redirect(McpOAuthService.AppendQuery(check.Consent.RedirectUri, new KeyValuePair<string, string?>[]
        {
            new("code", code),
            new("state", check.Consent.State),
            new("iss", issuer)
        }));
    }

    [HttpPost("/oauth/token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
            return OAuthError("invalid_request", "Send the token request as application/x-www-form-urlencoded.");

        var form = await Request.ReadFormAsync(cancellationToken);
        var clientAuthenticated = Request.Headers.ContainsKey("Authorization")
            || !string.IsNullOrEmpty(form["client_secret"])
            || !string.IsNullOrEmpty(form["client_assertion"]);
        var grant = form["grant_type"].ToString();
        McpTokenResult result;
        if (grant == "authorization_code")
        {
            result = await _oauth.ExchangeAuthorizationCodeAsync(
                form["code"], form["client_id"], form["redirect_uri"], form["code_verifier"], form["resource"],
                clientAuthenticated, cancellationToken);
        }
        else if (grant == "refresh_token")
        {
            result = await _oauth.RefreshAsync(
                form["refresh_token"], form["client_id"], form["resource"], clientAuthenticated, cancellationToken);
        }
        else
        {
            result = McpTokenResult.Failed("unsupported_grant_type", "grant_type must be authorization_code or refresh_token.");
        }

        if (!result.Succeeded)
            return OAuthError(result.Error!, result.Description!);

        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            access_token = result.AccessToken,
            token_type = "Bearer",
            expires_in = result.ExpiresIn,
            refresh_token = result.RefreshToken,
            scope = result.Scope
        });
    }

    [HttpPost("/oauth/register")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Register(CancellationToken cancellationToken)
    {
        JsonDocument? document = null;
        try
        {
            document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            var result = _oauth.Register(document.RootElement, Loopback());
            if (!result.Succeeded || result.Client == null)
                return OAuthError(result.Error ?? "invalid_client_metadata", result.Description ?? "Registration failed.");

            return StatusCode(StatusCodes.Status201Created, new
            {
                client_id = result.Client.ClientId,
                client_name = result.Client.ClientName,
                redirect_uris = result.Client.RedirectUris,
                token_endpoint_auth_method = "none",
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" }
            });
        }
        catch (JsonException)
        {
            return OAuthError("invalid_client_metadata", "Registration body must be JSON.");
        }
        finally
        {
            document?.Dispose();
        }
    }

    [Authorize(Policy = McpPolicies.Read)]
    [HttpGet("/oauth/userinfo")]
    public IActionResult UserInfo()
    {
        return Json(new
        {
            sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            email_verified = true
        });
    }

    private async Task<McpAuthorizationDecision> CheckAsync(CancellationToken cancellationToken)
    {
        var urls = Urls();
        return await _oauth.ValidateAuthorizationAsync(new McpAuthorizationRequest
        {
            ResponseType = Request.Query["response_type"],
            ClientId = Request.Query["client_id"].ToString(),
            RedirectUri = Request.Query["redirect_uri"].ToString(),
            State = Request.Query["state"],
            Scope = Request.Query["scope"],
            CodeChallenge = Request.Query["code_challenge"],
            CodeChallengeMethod = Request.Query["code_challenge_method"],
            Resource = Request.Query["resource"].ToString(),
            ExpectedResource = urls.Resource
        }, Loopback(), cancellationToken);
    }

    private (string Issuer, string Resource) Urls()
    {
        var issuer = McpPublicUrl.Base(Request, _wave.Value.PublicBaseUrl);
        return (issuer, McpPublicUrl.Resource(issuer));
    }

    private bool Loopback() => McpPublicUrl.IsLoopback(Request.Host.Host);

    private string ErrorRedirect(McpAuthorizationDecision decision) =>
        McpOAuthService.AppendQuery(decision.RedirectUri!, new KeyValuePair<string, string?>[]
        {
            new("error", decision.Error),
            new("error_description", decision.Description),
            new("state", decision.State),
            new("iss", Urls().Issuer)
        });

    private IActionResult OAuthError(string error, string description)
    {
        Response.Headers.CacheControl = "no-store";
        return StatusCode(error == "invalid_client" ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest, new
        {
            error,
            error_description = description
        });
    }
}
