using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdRackHub.Controllers;
using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace AdRackHub.Tests;

public class AgentMcpTests
{
    [Fact]
    public void ServerInstructionsStayInsideTheFirstBlockChatGptKeeps()
    {
        Assert.True(AgentMcpTools.Instructions.Length <= 512);
        Assert.Contains("Prospect", AgentMcpTools.Instructions);
        Assert.Contains("Wave", AgentMcpTools.Instructions);
        Assert.Contains("list_activities", AgentMcpTools.Instructions);
        Assert.Contains("billing_outstanding", AgentMcpTools.Instructions);
        Assert.Contains("log_stop_visit", AgentMcpTools.Instructions);
    }

    [Fact]
    public void ChatGptRedirectsAreAllowedAndOtherHostsAreNot()
    {
        Assert.True(McpOAuthRules.RedirectAllowed("https://chatgpt.com/connector_platform_oauth_redirect", false));
        Assert.True(McpOAuthRules.RedirectAllowed("https://chatgpt.com/connector/oauth/callback-1", false));
        Assert.False(McpOAuthRules.RedirectAllowed("https://evil.example/connector_platform_oauth_redirect", false));
        Assert.False(McpOAuthRules.RedirectAllowed("http://127.0.0.1:5281/callback", false));
        Assert.True(McpOAuthRules.RedirectAllowed("http://127.0.0.1:5281/callback", true));
    }

    [Fact]
    public async Task AuthorizationCodeRequiresPkceAndEchoesTheIssuer()
    {
        var store = new McpOAuthStore(null, NullLogger<McpOAuthStore>.Instance);
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton(store);
        services.AddSingleton<IMcpUserDirectory>(new DirectoryUser());
        services.AddSingleton<McpOAuthService>();
        await using var provider = services.BuildServiceProvider();
        var oauth = provider.GetRequiredService<McpOAuthService>();
        var redirect = "http://127.0.0.1:5281/callback";
        var resource = "http://127.0.0.1:5281/mcp";
        var registered = oauth.Register(JsonSerializer.Deserialize<JsonElement>("""
            {"client_name":"Test","redirect_uris":["http://127.0.0.1:5281/callback"],"token_endpoint_auth_method":"none"}
            """), loopbackIssuer: true);
        Assert.True(registered.Succeeded);

        var verifier = new string('a', 50);
        var challenge = McpOAuthStore.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var decision = await oauth.ValidateAuthorizationAsync(new McpAuthorizationRequest
        {
            ResponseType = "code",
            ClientId = registered.Client!.ClientId,
            RedirectUri = redirect,
            State = "state-1",
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            Resource = resource,
            ExpectedResource = resource
        }, true, CancellationToken.None);
        Assert.NotNull(decision.Consent);

        var code = oauth.CreateAuthorizationCode(decision.Consent!, new McpDirectoryUser("admin", "jon@ad-rack.net", true, true));
        var redirected = McpOAuthService.AppendQuery(redirect, new KeyValuePair<string, string?>[]
        {
            new("code", code),
            new("state", "state-1"),
            new("iss", "http://127.0.0.1:5281")
        });
        Assert.Contains("iss=http%3A%2F%2F127.0.0.1%3A5281", redirected, StringComparison.Ordinal);

        var rejected = await oauth.ExchangeAuthorizationCodeAsync(code, registered.Client.ClientId, redirect, new string('b', 50), resource, false, CancellationToken.None);
        Assert.False(rejected.Succeeded);
        Assert.Equal("invalid_grant", rejected.Error);

        var again = oauth.CreateAuthorizationCode(decision.Consent, new McpDirectoryUser("admin", "jon@ad-rack.net", true, true));
        var accepted = await oauth.ExchangeAuthorizationCodeAsync(again, registered.Client.ClientId, redirect, verifier, resource, false, CancellationToken.None);
        Assert.True(accepted.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(accepted.AccessToken));
    }

    [Fact]
    public async Task CreateStopsForDuplicatesAndDefaultsOmittedTypeToProspect()
    {
        var fake = new FakeAgentApi();
        fake.Duplicates = new[]
        {
            new AgentCustomerDto { Id = 9, CustomerName = "Riverside Museum", Type = "Prospect", HubUrl = "http://127.0.0.1/Customers/Details/9" }
        };
        var tools = Tools(fake);

        var blocked = await tools.CreateCustomer(Json("""{"customerName":"Riverside Museum"}"""));
        Assert.False(blocked.Created);
        Assert.Single(blocked.Duplicates);
        Assert.Equal(0, fake.CreateCalls);

        var created = await tools.CreateCustomer(Json("""{"customerName":"Riverside Museum"}"""), new[] { 9 });
        Assert.True(created.Created);
        Assert.Equal(1, fake.CreateCalls);
        Assert.Equal("Prospect", StringField(fake.LastCreate, "type"));
        Assert.Contains("Prospect", created.Customer!.Type);
    }

    [Fact]
    public async Task UpdatePassesOnlySuppliedFieldsIncludingExplicitNull()
    {
        var fake = new FakeAgentApi();
        var tools = Tools(fake);
        var updated = await tools.UpdateCustomer(5, Json("""{"phone":null,"city":"Lexington"}"""));

        Assert.Equal(5, updated.Id);
        var names = fake.LastUpdate!.Value.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("phone", names);
        Assert.Contains("city", names);
        Assert.DoesNotContain("address", names);
        Assert.Equal(JsonValueKind.Null, fake.LastUpdate.Value.GetProperty("phone").ValueKind);
        Assert.Equal("Lexington", fake.LastUpdate.Value.GetProperty("city").GetString());
    }

    [Fact]
    public async Task UnsignedToolCallIsRejected()
    {
        var tools = Tools(new FakeAgentApi(), signedIn: false);
        var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => tools.GetCustomer(1));
        Assert.Contains("admin", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task McpRejectsAnonymousCallsAndListsAuthenticatedTools()
    {
        var fake = new FakeAgentApi();
        await using var server = await McpServer.StartAsync(fake);
        using var http = new HttpClient();

        var anonymous = await http.PostAsync(server.McpUrl, new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
            Encoding.UTF8,
            "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.True(anonymous.Headers.TryGetValues("WWW-Authenticate", out var challenges));
        Assert.Contains("resource_metadata=", string.Join(" ", challenges), StringComparison.Ordinal);

        var metadata = await http.GetFromJsonAsync<JsonElement>(server.BaseUrl + "/.well-known/oauth-authorization-server");
        Assert.True(metadata.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        Assert.Contains("S256", metadata.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("none", metadata.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(item => item.GetString()));

        var resource = await http.GetFromJsonAsync<JsonElement>(server.BaseUrl + "/.well-known/oauth-protected-resource/mcp");
        Assert.Equal(server.McpUrl, resource.GetProperty("resource").GetString());

        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.McpUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            OwnsSession = false,
            Name = "test",
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + server.Token }
        });
        await using var client = await McpClient.CreateAsync(transport);
        Assert.Contains("Prospect", client.ServerInstructions);

        var tools = await client.ListToolsAsync();
        Assert.Equal(
            new[]
            {
                "add_contact", "billing_due", "billing_forecast", "billing_outstanding", "create_customer",
                "get_customer", "invoice_reminder_template", "list_activities", "list_visits", "log_stop_visit",
                "search_customers", "search_stops", "update_contact", "update_customer"
            },
            tools.Select(tool => tool.Name).OrderBy(name => name).ToArray());
        Assert.True(tools.Single(tool => tool.Name == "search_customers").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(tool => tool.Name == "list_activities").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(tool => tool.Name == "billing_due").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(tool => tool.Name == "billing_outstanding").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(tool => tool.Name == "billing_forecast").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(tool => tool.Name == "invoice_reminder_template").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(tools.Single(tool => tool.Name == "log_stop_visit").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(tool => tool.Name == "get_customer").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(tools.Single(tool => tool.Name == "create_customer").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Contains("Wave", tools.Single(tool => tool.Name == "create_customer").Description);

        var search = await client.CallToolAsync("search_customers", new Dictionary<string, object?>
        {
            ["type"] = "Prospect",
            ["status"] = "Active",
            ["search"] = "river",
            ["page"] = 2,
            ["pageSize"] = 25
        }, cancellationToken: CancellationToken.None);
        var body = search.Content.OfType<TextContentBlock>().Single().Text;
        Assert.Contains("\"page\":2", body.Replace(" ", ""));
        Assert.Contains("\"hasMore\":true", body.Replace(" ", ""));
        Assert.Equal(2, fake.LastPage);
        Assert.Equal(25, fake.LastPageSize);
    }

    [Fact]
    public async Task AgentGuideIsHtmlAndMarkdownUrlRedirects()
    {
        var contentRoot = FindWebProject();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            ApplicationName = typeof(AgentDocsController).Assembly.GetName().Name
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("agent-docs"));
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AgentDocsController).Assembly);
        builder.Services.Configure<AgentApiOptions>(_ => { });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.ToString());
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var html = await http.GetAsync(baseUrl + "/docs/agent.html");
        var htmlBody = await html.Content.ReadAsStringAsync();
        Assert.True(html.StatusCode == HttpStatusCode.OK, htmlBody.Length > 1500 ? htmlBody[..1500] : htmlBody);
        Assert.Equal("text/html", html.Content.Headers.ContentType?.MediaType);
        var page = htmlBody;
        Assert.Contains("<h2", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Add custom MCP server", page);
        Assert.Contains("/mcp", page);
        Assert.DoesNotContain("/docs/agent.md", page);

        var alias = await http.GetAsync(baseUrl + "/docs/agent");
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        Assert.Equal("text/html", alias.Content.Headers.ContentType?.MediaType);

        var markdown = await http.GetAsync(baseUrl + "/docs/agent.md");
        Assert.Equal(HttpStatusCode.Redirect, markdown.StatusCode);
        Assert.Equal("/docs/agent.html", markdown.Headers.Location?.OriginalString);

        var openApi = AgentApiGuide.OpenApiJson("https://hub.ad-rack.net");
        Assert.Contains("/docs/agent.html", openApi);
        Assert.DoesNotContain("\"delete\":", openApi, StringComparison.OrdinalIgnoreCase);

        await app.StopAsync();
    }

    private static AgentMcpTools Tools(FakeAgentApi fake, bool signedIn = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("127.0.0.1", 5281);
        if (signedIn)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(McpPolicies.ScopeClaim, McpPolicies.ReadScope),
                new Claim(McpPolicies.ScopeClaim, McpPolicies.WriteScope)
            ], "test"));
        }

        return new AgentMcpTools(
            fake,
            new FakeActivityApi(),
            new FakeVisitApi(),
            new FakeBillingApi(),
            new FakeReminderReader(),
            new HttpContextAccessor { HttpContext = context },
            Options.Create(new WaveOptions()),
            NullLogger<AgentMcpTools>.Instance);
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static string? StringField(JsonElement? body, string name)
    {
        Assert.True(body.HasValue);
        foreach (var property in body!.Value.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private static string FindWebProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AdRackHub", "AdRackHub.csproj");
            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate)!;
            if (File.Exists(Path.Combine(dir.FullName, "AdRackHub.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("AdRackHub project was not found.");
    }

    private sealed class FakeReminderReader : IInvoiceReminderReader
    {
        public Task<AgentCallResult<AgentInvoiceReminderReadDto>> ReadAsync(int? invoiceId, bool canUpdate, string hubBase, CancellationToken cancellationToken) =>
            Task.FromResult(AgentCallResult<AgentInvoiceReminderReadDto>.Ok(new AgentInvoiceReminderReadDto
            {
                Subject = InvoiceReminderMerge.DefaultSubject,
                Body = InvoiceReminderMerge.DefaultBody,
                CanUpdate = canUpdate,
                HubUrl = hubBase
            }));
    }

    private sealed class FakeBillingApi : IAgentBillingApi
    {
        public Task<AgentCallResult<AgentBillingDueDto>> DueAsync(int? year, int? month, string? search, int page, int pageSize, string hubBase, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCallResult<AgentBillingOutstandingDto>> OutstandingAsync(string? search, string? age, int? weeks, int page, int pageSize, string hubBase, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCallResult<AgentBillingForecastDto>> ForecastAsync(int? year, int? month, int? months, bool ignoreEndDate, int page, int pageSize, string hubBase, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeActivityApi : IAgentActivityApi
    {
        public Task<AgentCallResult<AgentActivityListDto>> ListAsync(string? status, string? kind, int? customerId, string? search, int page, int pageSize, string hubBase, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeVisitApi : IAgentVisitApi
    {
        public Task<AgentCallResult<AgentStopListDto>> SearchStopsAsync(string? search, int? routeId, int page, int pageSize, string hubBase, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCallResult<AgentVisitListDto>> ListVisitsAsync(int? stopId, int? routeId, int page, int pageSize, string hubBase, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCallResult<AgentVisitDto>> LogAsync(AgentVisitUpload upload, string hubBase, string? userId, string? userName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DirectoryUser : IMcpUserDirectory
    {
        public Task<McpDirectoryUser?> FindAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<McpDirectoryUser?>(new McpDirectoryUser(userId, "jon@ad-rack.net", true, true));
    }

    private sealed class McpServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public required string BaseUrl { get; init; }
        public required string McpUrl { get; init; }
        public required string Token { get; init; }

        private McpServer(WebApplication app) => _app = app;

        public static async Task<McpServer> StartAsync(FakeAgentApi fake)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(McpMetadataController).Assembly);
            builder.Services.AddSingleton<IAgentCustomerApi>(fake);
            builder.Services.AddSingleton<IAgentActivityApi>(new FakeActivityApi());
            builder.Services.AddSingleton<IAgentVisitApi>(new FakeVisitApi());
            builder.Services.AddSingleton<IAgentBillingApi>(new FakeBillingApi());
            builder.Services.AddSingleton<IInvoiceReminderReader>(new FakeReminderReader());
            builder.Services.AddSingleton<IMcpUserDirectory>(new DirectoryUser());
            builder.Services.Configure<WaveOptions>(_ => { });
            builder.Services.AddAdRackMcp(null);
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            app.MapMcp("/mcp").RequireAuthorization(McpPolicies.Read);
            await app.StartAsync();
            var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');
            var token = app.Services.GetRequiredService<McpOAuthStore>().SaveAccessToken(new McpAccessToken
            {
                UserId = "admin",
                Email = "jon@ad-rack.net",
                EmailVerified = true,
                Resource = baseUrl + "/mcp",
                Scope = "customers:read customers:write",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            return new McpServer(app) { BaseUrl = baseUrl, McpUrl = baseUrl + "/mcp", Token = token };
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class FakeAgentApi : IAgentCustomerApi
    {
        public IReadOnlyList<AgentCustomerDto> Duplicates { get; set; } = Array.Empty<AgentCustomerDto>();
        public int CreateCalls { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public JsonElement? LastCreate { get; private set; }
        public JsonElement? LastUpdate { get; private set; }

        public Task<AgentCallResult<AgentCustomerListDto>> ListAsync(string? type, string? status, string? search, int page, int pageSize, string hubBase, CancellationToken cancellationToken)
        {
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(AgentCallResult<AgentCustomerListDto>.Ok(new AgentCustomerListDto
            {
                Page = page,
                PageSize = pageSize,
                Total = 120,
                TotalPages = 5,
                HasMore = true,
                Items = [new AgentCustomerDto { Id = 4, CustomerName = "Riverside Museum", Type = "Prospect", HubUrl = hubBase + "/Customers/Details/4" }]
            }));
        }

        public Task<AgentCallResult<AgentCustomerDto>> GetAsync(int id, string hubBase, CancellationToken cancellationToken) =>
            Task.FromResult(AgentCallResult<AgentCustomerDto>.Ok(Sample(id, hubBase)));

        public Task<AgentCallResult<AgentCustomerDto>> CreateAsync(JsonElement body, string hubBase, CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastCreate = body.Clone();
            return Task.FromResult(AgentCallResult<AgentCustomerDto>.Created(Sample(8, hubBase), hubBase + "/api/agent/customers/8"));
        }

        public Task<AgentCallResult<AgentCustomerDto>> UpdateAsync(int id, JsonElement body, string hubBase, CancellationToken cancellationToken)
        {
            LastUpdate = body.Clone();
            return Task.FromResult(AgentCallResult<AgentCustomerDto>.Ok(Sample(id, hubBase)));
        }

        public Task<AgentCallResult<AgentCustomerDto>> AddContactAsync(int customerId, JsonElement body, string hubBase, CancellationToken cancellationToken) =>
            Task.FromResult(AgentCallResult<AgentCustomerDto>.Created(Sample(customerId, hubBase), hubBase));

        public Task<AgentCallResult<AgentCustomerDto>> UpdateContactAsync(int customerId, int contactId, JsonElement body, string hubBase, CancellationToken cancellationToken) =>
            Task.FromResult(AgentCallResult<AgentCustomerDto>.Ok(Sample(customerId, hubBase)));

        public Task<IReadOnlyList<AgentCustomerDto>> FindPotentialDuplicatesAsync(string? customerName, string? email, string? phone, string hubBase, CancellationToken cancellationToken) =>
            Task.FromResult(Duplicates);

        private static AgentCustomerDto Sample(int id, string hubBase) => new()
        {
            Id = id,
            CustomerName = "Riverside Museum",
            Type = "Prospect",
            Status = "Active",
            HubUrl = hubBase + "/Customers/Details/" + id,
            Contacts = [new AgentContactDto { Id = 3, CustomerId = id, Name = "Riverside Museum", DisplayName = "Ada Lovelace", Email = "ada@example.com" }]
        };
    }
}
