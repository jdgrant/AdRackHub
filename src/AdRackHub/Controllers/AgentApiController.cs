using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdRackHub.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/agent")]
public class AgentApiController : ControllerBase
{
    private readonly AgentCustomerApiService _api;
    private readonly IAgentActivityApi _activities;
    private readonly IAgentVisitApi _visits;
    private readonly AgentApiOptions _options;
    private static readonly JsonSerializerOptions VisitJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<AgentApiController> _logger;

    public AgentApiController(
        AgentCustomerApiService api,
        IAgentActivityApi activities,
        IAgentVisitApi visits,
        IOptions<AgentApiOptions> options,
        ILogger<AgentApiController> logger)
    {
        _api = api;
        _activities = activities;
        _visits = visits;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var baseUrl = AgentApiGuide.BaseUrl(Request);
        return Ok(new
        {
            name = "AdRackHub Agent API",
            docs = $"{baseUrl}/docs/agent.html",
            openApi = $"{baseUrl}/api/agent/openapi.json",
            authentication = "Authorization: Bearer <api key>",
            canDelete = false
        });
    }

    [HttpGet("openapi.json")]
    public IActionResult OpenApi()
    {
        return Content(AgentApiGuide.OpenApiJson(AgentApiGuide.BaseUrl(Request)), "application/json; charset=utf-8");
    }

    [HttpGet("customers")]
    public Task<IActionResult> List(
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AgentCustomerApiService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Execute(() => _api.ListAsync(type, status, search, page, pageSize, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpGet("customers/{id:int}")]
    public Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        return Execute(() => _api.GetAsync(id, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpPost("customers")]
    public Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        return Execute(() => _api.CreateAsync(body, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpPatch("customers/{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        return Execute(() => _api.UpdateAsync(id, body, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpPost("customers/{id:int}/contacts")]
    public Task<IActionResult> AddContact(int id, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        return Execute(() => _api.AddContactAsync(id, body, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpPatch("customers/{id:int}/contacts/{contactId:int}")]
    public Task<IActionResult> UpdateContact(int id, int contactId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        return Execute(() => _api.UpdateContactAsync(id, contactId, body, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpGet("activities")]
    public Task<IActionResult> Activities(
        [FromQuery] string? status,
        [FromQuery] string? kind,
        [FromQuery] int? customerId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AgentCustomerApiService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Execute(() => _activities.ListAsync(status, kind, customerId, search, page, pageSize, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpGet("stops")]
    public Task<IActionResult> Stops(
        [FromQuery] string? search,
        [FromQuery] int? routeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AgentCustomerApiService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Execute(() => _visits.SearchStopsAsync(search, routeId, page, pageSize, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpGet("visits")]
    public Task<IActionResult> Visits(
        [FromQuery] int? stopId,
        [FromQuery] int? routeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AgentCustomerApiService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Execute(() => _visits.ListVisitsAsync(stopId, routeId, page, pageSize, AgentApiGuide.BaseUrl(Request), cancellationToken));
    }

    [HttpPost("visits")]
    [RequestSizeLimit(28 * 1024 * 1024)]
    public Task<IActionResult> LogVisit([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var denied = Guard();
        if (denied != null)
            return Task.FromResult(denied);

        AgentVisitUpload? upload;
        try
        {
            upload = body.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<AgentVisitUpload>(body.GetRawText(), VisitJson)
                : null;
        }
        catch (JsonException)
        {
            upload = null;
        }

        if (upload == null)
            return Task.FromResult<IActionResult>(BadRequest(new AgentErrorDto("Send a JSON object with the stop, driver, and before and after photos.")));

        return Execute(() => _visits.LogAsync(upload, AgentApiGuide.BaseUrl(Request), null, "Agent API", cancellationToken));
    }

    [HttpPut("customers/{id:int}")]
    [HttpPut("customers/{id:int}/contacts/{contactId:int}")]
    public IActionResult PutNotAllowed()
    {
        var denied = Guard();
        if (denied != null)
            return denied;
        return StatusCode(StatusCodes.Status405MethodNotAllowed, new AgentErrorDto(
            "Use PATCH to update. Send only the fields that should change."));
    }

    [HttpDelete("customers/{id:int}")]
    [HttpDelete("customers/{id:int}/contacts/{contactId:int}")]
    public IActionResult DeleteNotAllowed()
    {
        var denied = Guard();
        if (denied != null)
            return denied;
        return StatusCode(StatusCodes.Status405MethodNotAllowed, new AgentErrorDto(
            "Delete is not available. This API can list, create, and update customers, prospects, and contacts, list activities, and log stop visits."));
    }

    private async Task<IActionResult> Execute<T>(Func<Task<AgentCallResult<T>>> action)
    {
        var denied = Guard();
        if (denied != null)
            return denied;

        try
        {
            return From(await action());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent API request failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new AgentErrorDto("The request could not be completed."));
        }
    }

    private IActionResult From<T>(AgentCallResult<T> result)
    {
        if (result.Error != null)
            return StatusCode(result.StatusCode, result.Error);
        if (result.StatusCode == StatusCodes.Status201Created && !string.IsNullOrWhiteSpace(result.Location))
            return Created(result.Location, result.Body);
        return StatusCode(result.StatusCode, result.Body);
    }

    private IActionResult? Guard()
    {
        var expected = _options.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new AgentErrorDto(
                "The agent API is not configured. Set AgentApi:ApiKey in appsettings.Production.json on the server, or set AgentApi__ApiKey or ADRACKHUB_AGENT_API_KEY. Deploys do not upload that file."));
        }

        if (!KeysMatch(expected, ReadProvidedKey()))
        {
            _logger.LogInformation("Agent API rejected a request with a missing or unknown key.");
            return Unauthorized(new AgentErrorDto(
                "Invalid or missing API key. Send Authorization: Bearer <key> or the X-AdRackHub-Api-Key header."));
        }

        return null;
    }

    private string? ReadProvidedKey()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            if (!string.IsNullOrEmpty(token))
                return token;
        }

        var header = Request.Headers["X-AdRackHub-Api-Key"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }

    private static bool KeysMatch(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(provided);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
