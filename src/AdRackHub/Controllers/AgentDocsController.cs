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
            MarkdownUrl = $"{baseUrl}/docs/agent.md",
            OpenApiUrl = $"{baseUrl}/api/agent/openapi.json",
            KeyConfigured = !string.IsNullOrWhiteSpace(key),
            ShowKey = isAdmin && !string.IsNullOrWhiteSpace(key),
            ShowSetup = isAdmin && string.IsNullOrWhiteSpace(key),
            ApiKey = isAdmin ? key : null
        });
    }

    [HttpGet("/docs/agent.md")]
    public IActionResult Markdown()
    {
        Response.Headers.CacheControl = "no-store";
        var markdown = AgentApiGuide.Markdown(AgentApiGuide.BaseUrl(Request));
        return Content(markdown, "text/plain; charset=utf-8");
    }
}
