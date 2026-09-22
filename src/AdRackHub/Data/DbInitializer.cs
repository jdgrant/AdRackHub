using AdRackHub.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Data;

public static class DbInitializer
{
    public const string DefaultAdminEmail = "jon@ad-rack.net";
    public static readonly string[] StandardRoutes =
    [
        "Exit - Central OH",
        "Exit - Northern Interstate",
        "Exit - Cincinnati-NKY",
        "Exit - I-65 & 24",
        "Exit - I-75",
        "Exit - Lex-Frankfort",
        "Exit - Louisville",
        "Exit - Mid-TN",
        "Exit - Northeast OH",
        "Rest Area - I-64 East of Lexington",
        "Rest Area - I-75",
        "Rest Area - I-64 West & I-71",
        "Rest Area - I-65"
    ];

    private static readonly string[] PlaceholderRoutes =
    [
        "Route 1 - North Valley",
        "Route 2 - East Valley",
        "Route 3 - West Valley"
    ];

    public static async Task InitializeAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration)
    {
        await context.Database.MigrateAsync();
        await EnsureRolesAsync(roleManager);
        await EnsureAdminUserAsync(userManager, configuration);
        await EnsureRoutesAsync(context);

    }

    private static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private static async Task EnsureAdminUserAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        var adminEmail = configuration["Seed:AdminEmail"] ?? DefaultAdminEmail;
        var adminPassword = configuration["Seed:AdminPassword"];

        if (await userManager.FindByEmailAsync(adminEmail) != null)
            return;

        if (string.IsNullOrWhiteSpace(adminPassword))
            throw new InvalidOperationException(
                "Seed:AdminPassword must be set in configuration to create the default admin user.");

        var user = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            DisplayName = "Jon Grant"
        };

        var result = await userManager.CreateAsync(user, adminPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        foreach (var role in AppRoles.All)
            await userManager.AddToRoleAsync(user, role);
    }

    private static async Task EnsureRoutesAsync(ApplicationDbContext context)
    {
        var hasPlaceholders = await context.Routes.AnyAsync(r => PlaceholderRoutes.Contains(r.RouteName));
        if (hasPlaceholders)
        {
            context.CustomerRouteStops.RemoveRange(await context.CustomerRouteStops.ToListAsync());
            context.CustomerRoutes.RemoveRange(await context.CustomerRoutes.ToListAsync());
            context.Stops.RemoveRange(await context.Stops.ToListAsync());
            context.Routes.RemoveRange(await context.Routes.ToListAsync());
            await context.SaveChangesAsync();
        }

        var existing = await context.Routes.ToListAsync();
        foreach (var name in StandardRoutes)
        {
            var match = existing.FirstOrDefault(r => RouteNaming.NamesMatch(r.RouteName, name));
            if (match == null)
            {
                var created = new Models.Route
                {
                    RouteName = name,
                    Price = 0,
                    BillingFrequency = BillingFrequency.Quarterly,
                    Status = RouteStatus.Active
                };
                context.Routes.Add(created);
                existing.Add(created);
                continue;
            }

            // Keep the same row and all contract links. A rename is not a new route.
            if (!string.Equals(match.RouteName, name, StringComparison.Ordinal))
                match.RouteName = name;
        }

        await context.SaveChangesAsync();
    }

    public static async Task<int> DeleteInactiveRoutesAsync(ApplicationDbContext context)
    {
        var routes = (await context.Routes.ToListAsync())
            .Where(r => r.Status == RouteStatus.Inactive
                        || StandardRoutes.All(name => !RouteNaming.NamesMatch(name, r.RouteName)))
            .ToList();

        if (routes.Count == 0)
            return 0;

        var routeIds = routes.Select(r => r.Id).ToList();
        var customerRouteIds = await context.CustomerRoutes
            .Where(cr => routeIds.Contains(cr.RouteId))
            .Select(cr => cr.Id)
            .ToListAsync();
        var stopIds = await context.Stops
            .Where(s => routeIds.Contains(s.RouteId))
            .Select(s => s.Id)
            .ToListAsync();

        context.CustomerRouteStops.RemoveRange(
            await context.CustomerRouteStops
                .Where(crs => customerRouteIds.Contains(crs.CustomerRouteId) || stopIds.Contains(crs.StopId))
                .ToListAsync());

        context.CustomerRoutes.RemoveRange(
            await context.CustomerRoutes.Where(cr => routeIds.Contains(cr.RouteId)).ToListAsync());

        context.CustomerContractRoutes.RemoveRange(
            await context.CustomerContractRoutes.Where(cbr => routeIds.Contains(cbr.RouteId)).ToListAsync());

        context.Stops.RemoveRange(
            await context.Stops.Where(s => routeIds.Contains(s.RouteId)).ToListAsync());

        context.Routes.RemoveRange(routes);
        await context.SaveChangesAsync();
        return routes.Count;
    }
}
