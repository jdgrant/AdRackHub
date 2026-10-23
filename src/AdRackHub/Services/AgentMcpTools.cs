using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdRackHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AdRackHub.Services;

[McpServerToolType]
public sealed class AgentMcpTools
{
    public const string Instructions =
        "Use search_customers before create_customer. New records default to Prospect. Creating a Customer, or changing a Prospect to Customer, assigns a brochure ID and sends it to Wave. Omit unchanged fields; null or \"\" clears a field. No delete. list_activities reads open tasks and email follow-ups. log_stop_visit saves the driver and before and after photos from an email.";

    private readonly IAgentCustomerApi _api;
    private readonly IAgentActivityApi _activities;
    private readonly IAgentVisitApi _visits;
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<WaveOptions> _wave;
    private readonly ILogger<AgentMcpTools> _logger;

    public AgentMcpTools(
        IAgentCustomerApi api,
        IAgentActivityApi activities,
        IAgentVisitApi visits,
        IHttpContextAccessor http,
        IOptions<WaveOptions> wave,
        ILogger<AgentMcpTools> logger)
    {
        _api = api;
        _activities = activities;
        _visits = visits;
        _http = http;
        _wave = wave;
        _logger = logger;
    }

    [Authorize(Policy = McpPolicies.Read)]
    [McpServerTool(Name = "search_customers", Title = "Search customers", ReadOnly = true, OpenWorld = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Search and filter customers and prospects, including contacts. Use this before creating a record. type Customer, Prospect, ExpandedProspect, or all. Prospect includes expanded prospects. status Active, Inactive, or all. page starts at 1. pageSize defaults to 50 and cannot exceed 100.")]
    public Task<AgentCustomerListDto> SearchCustomers(
        [Description("Customer, Prospect, ExpandedProspect, or all. Omit to return every type.")] string? type,
        [Description("Active, Inactive, or all. Omit to return every status.")] string? status,
        [Description("Matches id, business name, address, city, state, zip, phone, email, web URL, and contact name, title, email, or phone.")] string? search,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Page size. Default 50. Maximum 100.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.ReadScope);
        return Complete(_api.ListAsync(type, status, search, page, pageSize, HubBase(), cancellationToken), "search_customers");
    }

    [Authorize(Policy = McpPolicies.Read)]
    [McpServerTool(Name = "get_customer", Title = "Get customer", ReadOnly = true, OpenWorld = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Get one customer or prospect by id, including every contact. Use this when the id is already known.")]
    public Task<AgentCustomerDto> GetCustomer(
        [Description("AdRackHub customer or prospect id.")] int id,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.ReadScope);
        return Complete(_api.GetAsync(id, HubBase(), cancellationToken), "get_customer");
    }

    [Authorize(Policy = McpPolicies.Write)]
    [McpServerTool(Name = "create_customer", Title = "Create customer", ReadOnly = false, OpenWorld = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Create a customer or prospect and optional contacts. type defaults to Prospect when omitted. Creating a Customer assigns a brochure ID and sends the record to Wave. Use Prospect unless the business is a paying customer. If possible duplicates exist, this does not create a record; review the matches and call again with acknowledgeDuplicateIds set to every returned id. Do not send contacts on a later update.")]
    public async Task<AgentCreateToolResult> CreateCustomer(
        [Description("Customer object. customerName is required. type is Customer or Prospect and defaults to Prospect. Optional contacts is an array. Omit a field to leave the default. A contact name defaults to the business name. LinkedIn must be a linkedin.com/in or /pub profile. sendInvoice true plus an email sets invoice receipt to Both.")] JsonElement customer,
        [Description("Ids from an earlier duplicate check. Creation continues only when every current match id is listed.")] int[]? acknowledgeDuplicateIds = null,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.WriteScope);
        if (customer.ValueKind != JsonValueKind.Object)
            throw new McpException("customer must be a JSON object.");

        var body = WithDefaultProspectType(customer);
        var hub = HubBase();
        var duplicates = await _api.FindPotentialDuplicatesAsync(
            Text(body, "customerName"),
            Text(body, "email") ?? FirstContactText(body, "email"),
            Text(body, "phone") ?? FirstContactText(body, "phone") ?? FirstContactText(body, "cellPhone"),
            hub,
            cancellationToken);
        var acknowledged = new HashSet<int>(acknowledgeDuplicateIds ?? Array.Empty<int>());
        if (duplicates.Count > 0 && duplicates.Any(match => !acknowledged.Contains(match.Id)))
        {
            return new AgentCreateToolResult
            {
                Created = false,
                Message = "Possible duplicates exist. Ask which record to use, or call create_customer again with acknowledgeDuplicateIds set to every returned id.",
                Duplicates = duplicates.ToList()
            };
        }

        var created = await Complete(_api.CreateAsync(body, hub, cancellationToken), "create_customer");
        var waveNote = string.Equals(created.Type, "Customer", StringComparison.OrdinalIgnoreCase)
            ? " A brochure ID was assigned and the customer was sent to Wave."
            : "";
        return new AgentCreateToolResult
        {
            Created = true,
            Message = "Created." + waveNote,
            Customer = created
        };
    }

    [Authorize(Policy = McpPolicies.Write)]
    [McpServerTool(Name = "update_customer", Title = "Update customer", ReadOnly = false, OpenWorld = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Update only the supplied customer or prospect fields. Omit a field to leave it unchanged. Send null or an empty string to clear an optional field. Do not send contacts here. Changing type from Prospect to Customer assigns a brochure ID and sends the record to Wave. This cannot assign ExpandedProspect and cannot delete.")]
    public Task<AgentCustomerDto> UpdateCustomer(
        [Description("AdRackHub customer or prospect id.")] int id,
        [Description("Object containing only the fields to change. Omit unchanged fields. Null or \"\" clears an optional string.")] JsonElement fields,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.WriteScope);
        if (fields.ValueKind != JsonValueKind.Object)
            throw new McpException("fields must be a JSON object.");
        return Complete(_api.UpdateAsync(id, fields, HubBase(), cancellationToken), "update_customer");
    }

    [Authorize(Policy = McpPolicies.Write)]
    [McpServerTool(Name = "add_contact", Title = "Add contact", ReadOnly = false, OpenWorld = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Add one contact to an existing customer or prospect. name defaults to the business name. LinkedIn must be a linkedin.com/in or /pub profile. sendInvoice true plus an email sets the record's invoice receipt to Both. Returns the customer with all contacts.")]
    public Task<AgentCustomerDto> AddContact(
        [Description("AdRackHub customer or prospect id.")] int customerId,
        [Description("Contact object. Omit a field to leave its default. Null or \"\" clears an optional string on later updates.")] JsonElement contact,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.WriteScope);
        if (contact.ValueKind != JsonValueKind.Object)
            throw new McpException("contact must be a JSON object.");
        return Complete(_api.AddContactAsync(customerId, contact, HubBase(), cancellationToken), "add_contact");
    }

    [Authorize(Policy = McpPolicies.Write)]
    [McpServerTool(Name = "update_contact", Title = "Update contact", ReadOnly = false, OpenWorld = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Update only the supplied fields on one contact. Omit a field to leave it unchanged. Send null or an empty string to clear an optional field. LinkedIn must be a linkedin.com/in or /pub profile. sendInvoice true plus an email sets invoice receipt to Both. Returns the customer with all contacts.")]
    public Task<AgentCustomerDto> UpdateContact(
        [Description("AdRackHub customer or prospect id.")] int customerId,
        [Description("Contact id on that record.")] int contactId,
        [Description("Object containing only the contact fields to change. Omit unchanged fields. Null or \"\" clears an optional string.")] JsonElement fields,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.WriteScope);
        if (fields.ValueKind != JsonValueKind.Object)
            throw new McpException("fields must be a JSON object.");
        return Complete(_api.UpdateContactAsync(customerId, contactId, fields, HubBase(), cancellationToken), "update_contact");
    }

    [Authorize(Policy = McpPolicies.Read)]
    [McpServerTool(Name = "list_activities", Title = "List activities", ReadOnly = true, OpenWorld = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Read Hub activities for a twice-daily review. The default status is open, which is New and Working, and includes tasks and email follow-ups. kind can be Task, Email, Note, Call, Meeting, Action, BrochuresNeeded, or all. status can be open, all, New, Working, or Done.")]
    public Task<AgentActivityListDto> ListActivities(
        [Description("open, all, New, Working, or Done. Omit for open.")] string? status,
        [Description("Task, Email, Note, Call, Meeting, Action, BrochuresNeeded, or all.")] string? kind,
        [Description("Limit to one customer or prospect id.")] int? customerId,
        [Description("Matches the note, customer name, or who logged it.")] string? search,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Page size. Default 50. Maximum 100.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.ReadScope);
        return Complete(_activities.ListAsync(status, kind, customerId, search, page, pageSize, HubBase(), cancellationToken), "list_activities");
    }

    [Authorize(Policy = McpPolicies.Read)]
    [McpServerTool(Name = "search_stops", Title = "Search stops", ReadOnly = true, OpenWorld = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Find a route stop before logging a visit. Search matches the stop name, route name, city, or highway exit. Use the returned id as stopId on log_stop_visit.")]
    public Task<AgentStopListDto> SearchStops(
        [Description("Stop name, route name, city, or highway exit.")] string? search,
        [Description("Limit to one route id.")] int? routeId,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Page size. Default 50. Maximum 100.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.ReadScope);
        return Complete(_visits.SearchStopsAsync(search, routeId, page, pageSize, HubBase(), cancellationToken), "search_stops");
    }

    [Authorize(Policy = McpPolicies.Read)]
    [McpServerTool(Name = "list_visits", Title = "List visits", ReadOnly = true, OpenWorld = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("List logged stop visits, including the driver and before and after photo links.")]
    public Task<AgentVisitListDto> ListVisits(
        [Description("Limit to one stop id.")] int? stopId,
        [Description("Limit to one route id.")] int? routeId,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Page size. Default 50. Maximum 100.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.ReadScope);
        return Complete(_visits.ListVisitsAsync(stopId, routeId, page, pageSize, HubBase(), cancellationToken), "list_visits");
    }

    [Authorize(Policy = McpPolicies.Write)]
    [McpServerTool(Name = "log_stop_visit", Title = "Log stop visit", ReadOnly = false, OpenWorld = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Log one stop visit with the driver and before and after photos. Pass email image attachments as base64 or data URLs in beforeImageBase64 and afterImageBase64. JPG, PNG, GIF, or WEBP, 8 MB or smaller each. Send stopId from search_stops, or stopName plus routeName when that pair matches one stop.")]
    public Task<AgentVisitDto> LogStopVisit(
        [Description("Driver who made the visit.")] string driverName,
        [Description("Before photo from the email, as base64 or a data URL.")] string beforeImageBase64,
        [Description("After photo from the email, as base64 or a data URL.")] string afterImageBase64,
        [Description("Stop id from search_stops. Preferred when the email does not name a unique stop.")] int? stopId = null,
        [Description("Route name used with stopName when stopId is unknown.")] string? routeName = null,
        [Description("Stop name used when stopId is unknown.")] string? stopName = null,
        [Description("Original before file name, such as before.jpg.")] string? beforeFileName = null,
        [Description("Original after file name, such as after.jpg.")] string? afterFileName = null,
        [Description("Optional. Image type is detected from the bytes.")] string? beforeContentType = null,
        [Description("Optional. Image type is detected from the bytes.")] string? afterContentType = null,
        [Description("When the visit happened, as an ISO-8601 date and time. Omit to use the current time.")] string? visitedAt = null,
        [Description("Optional note, up to 1000 characters.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        RequireScope(McpPolicies.WriteScope);
        var user = _http.HttpContext?.User;
        return Complete(_visits.LogAsync(new AgentVisitUpload
        {
            StopId = stopId,
            RouteName = routeName,
            StopName = stopName,
            DriverName = driverName,
            VisitedAt = visitedAt,
            Notes = notes,
            BeforeImageBase64 = beforeImageBase64,
            BeforeFileName = beforeFileName,
            BeforeContentType = beforeContentType,
            AfterImageBase64 = afterImageBase64,
            AfterFileName = afterFileName,
            AfterContentType = afterContentType
        }, HubBase(), user?.FindFirstValue(ClaimTypes.NameIdentifier), user?.FindFirstValue(ClaimTypes.Email) ?? user?.Identity?.Name, cancellationToken), "log_stop_visit");
    }

    private async Task<T> Complete<T>(Task<AgentCallResult<T>> call, string tool)
    {
        try
        {
            var result = await call;
            if (result.Error != null)
                throw new McpException(Format(result.Error));
            return result.Body!;
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool {Tool} failed.", tool);
            throw new McpException("The request could not be completed.");
        }
    }

    private void RequireScope(string scope)
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true || !user.HasClaim(McpPolicies.ScopeClaim, scope))
            throw new McpException("Sign in with an AdRackHub admin account is required.");
    }

    private string HubBase()
    {
        var request = _http.HttpContext?.Request ?? throw new McpException("Sign in with an AdRackHub admin account is required.");
        return McpPublicUrl.Base(request, _wave.Value.PublicBaseUrl);
    }

    private static JsonElement WithDefaultProspectType(JsonElement body)
    {
        if (AgentJson.Has(body, "type"))
            return body;
        var node = JsonNode.Parse(body.GetRawText()) as JsonObject ?? new JsonObject();
        node["type"] = "Prospect";
        return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
    }

    private static string? Text(JsonElement body, string name)
    {
        if (!AgentJson.Try(body, name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static string? FirstContactText(JsonElement body, string name)
    {
        if (!AgentJson.Try(body, "contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in contacts.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && AgentJson.Try(item, name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private static string Format(AgentErrorDto error)
    {
        if (error.Errors is { Count: > 0 })
            return string.Join(" ", error.Errors);
        return string.IsNullOrWhiteSpace(error.Error) ? "The request is invalid." : error.Error;
    }
}
