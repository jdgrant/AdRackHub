using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdRackHub.Controllers;

[AllowAnonymous]
public class AgentDocsController : Controller
{
    private readonly AgentApiOptions _options;

    public AgentDocsController(IOptions<AgentApiOptions> options)
    {
        _options = options.Value;
    }

    [HttpGet("/docs/agent")]
    public IActionResult Index()
    {
        Response.Headers.CacheControl = "no-store";
        var baseUrl = AgentApiGuide.BaseUrl(Request);
        var key = _options.ApiKey?.Trim();
        var isAdmin = User.IsInRole(AppRoles.Admin);
        var markdown = AgentApiGuide.Markdown(baseUrl);
        return View(new AgentDocsPage
        {
            Html = AgentApiGuide.ToHtml(markdown),
            MarkdownUrl = $"{baseUrl}/docs/agent.html",
            OpenApiUrl = $"{baseUrl}/api/agent/openapi.json",
            KeyConfigured = !string.IsNullOrWhiteSpace(key),
            ShowKey = isAdmin && !string.IsNullOrWhiteSpace(key),
            ShowSetup = isAdmin && string.IsNullOrWhiteSpace(key),
            ApiKey = isAdmin ? key : null
        });
    }

    [HttpGet("/docs/agent.html")]
    public IActionResult Html() => Index();

    [HttpGet("/docs/agent.md")]
    public IActionResult Markdown() => Redirect("/docs/agent.html");
}
