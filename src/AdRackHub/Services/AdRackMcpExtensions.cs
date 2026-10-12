using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdRackHub.Services;

public static class AdRackMcpExtensions
{
    public static IServiceCollection AddAdRackMcp(this IServiceCollection services, string? storePath)
    {
        services.AddSingleton(sp => new McpOAuthStore(storePath, sp.GetRequiredService<ILogger<McpOAuthStore>>()));
        services.AddScoped<McpOAuthService>();
        services.AddHttpClient("McpClientMetadata", client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddAuthentication()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, McpBearerHandler>(McpBearerHandler.SchemeName, null);

        services.Configure<AuthorizationOptions>(options =>
        {
            options.AddPolicy(McpPolicies.Read, policy => policy
                .AddAuthenticationSchemes(McpBearerHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(McpPolicies.ScopeClaim, McpPolicies.ReadScope));
            options.AddPolicy(McpPolicies.Write, policy => policy
                .AddAuthenticationSchemes(McpBearerHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(McpPolicies.ScopeClaim, McpPolicies.WriteScope));
        });

        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "Ad-Rack", Title = "Ad-Rack", Version = "1.0.0" };
                options.ServerInstructions = AgentMcpTools.Instructions;
            })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<AgentMcpTools>()
            .AddAuthorizationFilters();

        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        return services;
    }
}
