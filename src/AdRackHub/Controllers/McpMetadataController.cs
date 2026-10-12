using System.Text.Json;
using AdRackHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdRackHub.Controllers;

[AllowAnonymous]
public class McpMetadataController : Controller
{
    private readonly IOptions<WaveOptions> _wave;

    public McpMetadataController(IOptions<WaveOptions> wave)
    {
        _wave = wave;
    }

    [HttpGet("/.well-known/oauth-protected-resource")]
    [HttpGet("/.well-known/oauth-protected-resource/mcp")]
    public IActionResult ProtectedResource()
    {
        var issuer = McpPublicUrl.Base(Request, _wave.Value.PublicBaseUrl);
        return WriteJson(new
        {
            resource = McpPublicUrl.Resource(issuer),
            authorization_servers = new[] { issuer },
            scopes_supported = new[] { "customers:read", "customers:write", "openid", "email" },
            bearer_methods_supported = new[] { "header" },
            resource_documentation = $"{issuer}/docs/agent.html"
        });
    }

    [HttpGet("/.well-known/oauth-authorization-server")]
    public IActionResult AuthorizationServer()
    {
        var issuer = McpPublicUrl.Base(Request, _wave.Value.PublicBaseUrl);
        return WriteJson(new
        {
            issuer,
            authorization_endpoint = $"{issuer}/oauth/authorize",
            token_endpoint = $"{issuer}/oauth/token",
            registration_endpoint = $"{issuer}/oauth/register",
            userinfo_endpoint = $"{issuer}/oauth/userinfo",
            scopes_supported = new[] { "customers:read", "customers:write", "openid", "email" },
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none" },
            client_id_metadata_document_supported = true,
            authorization_response_iss_parameter_supported = true
        });
    }

    private IActionResult WriteJson(object body)
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(JsonSerializer.Serialize(body), "application/json; charset=utf-8");
    }
}
