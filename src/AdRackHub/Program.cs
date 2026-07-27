using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppRoles.Dashboard, policy =>
        policy.RequireRole(AppRoles.Dashboard, AppRoles.Admin));
    options.AddPolicy(AppRoles.Customers, policy =>
        policy.RequireRole(AppRoles.Customers, AppRoles.Admin));
    options.AddPolicy(AppRoles.RoutesStops, policy =>
        policy.RequireRole(AppRoles.RoutesStops, AppRoles.Admin));
    options.AddPolicy(AppRoles.Admin, policy =>
        policy.RequireRole(AppRoles.Admin));
});

builder.Services.Configure<GoogleSheetsOptions>(builder.Configuration.GetSection(GoogleSheetsOptions.SectionName));
builder.Services.AddSingleton<GoogleSheetsService>();
builder.Services.AddScoped<RouteSheetSyncService>();
builder.Services.Configure<WaveOptions>(builder.Configuration.GetSection(WaveOptions.SectionName));
builder.Services.Configure<WaveSyncOptions>(builder.Configuration.GetSection(WaveSyncOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<WaveApiService>();
builder.Services.AddScoped<WaveSyncService>();
builder.Services.AddScoped<MonthlyBillingService>();
builder.Services.AddScoped<StopImportService>();
builder.Services.AddScoped<CustomerImportService>();
builder.Services.AddScoped<StopVisitService>();
builder.Services.AddScoped<BrochureScanService>();
builder.Services.AddScoped<BrochureOptimizeService>();
builder.Services.AddScoped<KyBrochureProspectImportService>();
builder.Services.AddScoped<CustomerFieldEnrichmentService>();
builder.Services.AddScoped<CustomerRouteMatrixService>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueCountLimit = 16384;
    options.MultipartBodyLengthLimit = BrochureScanService.MaxFileSizeBytes;
});
builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var stopImportService = scope.ServiceProvider.GetRequiredService<StopImportService>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await DbInitializer.InitializeAsync(context, userManager, roleManager, app.Configuration);

    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var uploadRoot = BrochureScanService.GetUploadRoot(env);
    Directory.CreateDirectory(uploadRoot);
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation("Brochure uploads directory: {UploadRoot}", uploadRoot);

    if (args.Contains("--run-billing"))
    {
        var billingService = scope.ServiceProvider.GetRequiredService<MonthlyBillingService>();
        var now = DateTime.Today;
        var year = now.Year;
        var month = now.Month;
        var run = await billingService.PrepareRunAsync(year, month);
        run = await billingService.SubmitRunAsync(run.Id);
        Console.WriteLine($"Billing run for {BillingDueCalculator.PeriodLabel(year, month)}: {run.Status}");
        return;
    }

    if (args.Contains("--import-ky-brochure-prospects"))
    {
        var importService = scope.ServiceProvider.GetRequiredService<KyBrochureProspectImportService>();
        var result = await importService.ImportAsync();
        Console.WriteLine($"KY brochure prospects: {result.Created} created, {result.SkippedExisting} skipped (already exist), {result.BrochuresAttached} brochures attached.");
        if (result.MissingScanFiles.Count > 0)
            Console.WriteLine($"Missing scan files: {string.Join(", ", result.MissingScanFiles)}");
        if (result.BrochureErrors.Count > 0)
            Console.WriteLine($"Brochure errors: {string.Join(" | ", result.BrochureErrors)}");
        foreach (var name in result.CreatedNames)
            Console.WriteLine($"  + {name}");
        return;
    }

    if (args.Contains("--replace-ky-brochure-pngs"))
    {
        var importService = scope.ServiceProvider.GetRequiredService<KyBrochureProspectImportService>();
        var result = await importService.ReplaceScansWithPngsAsync();
        Console.WriteLine($"KY brochure PNG replace: {result.Replaced} attached across {result.CustomerIds.Count} prospects.");
        if (result.MissingPngs.Count > 0)
            Console.WriteLine($"Missing PNGs: {string.Join(", ", result.MissingPngs)}");
        if (result.UnmatchedAttractions.Count > 0)
            Console.WriteLine($"Unmatched attractions: {string.Join(", ", result.UnmatchedAttractions)}");
        if (result.OrphanPngs.Count > 0)
            Console.WriteLine($"Orphan PNGs: {string.Join(", ", result.OrphanPngs)}");
        foreach (var map in result.Mappings)
            Console.WriteLine($"  {map}");
        Console.WriteLine("CUSTOMER_IDS=" + string.Join(",", result.CustomerIds));
        return;
    }

    if (args.Contains("--enrich-customer-fields"))
    {
        var enrichService = scope.ServiceProvider.GetRequiredService<CustomerFieldEnrichmentService>();
        var result = await enrichService.EnrichAsync();
        Console.WriteLine($"Customer field enrichment: {result.Updated} updated of {result.TotalCustomers} ({result.WithBrochure} with brochures).");
        foreach (var line in result.Updates)
            Console.WriteLine($"  UPD {line}");
        Console.WriteLine($"MISSING_PHONE={result.MissingPhone.Count}");
        foreach (var line in result.MissingPhone)
            Console.WriteLine($"  PHONE {line}");
        Console.WriteLine($"MISSING_WEB={result.MissingWeb.Count}");
        foreach (var line in result.MissingWeb)
            Console.WriteLine($"  WEB {line}");
        Console.WriteLine($"MISSING_ADDRESS={result.MissingAddress.Count}");
        foreach (var line in result.MissingAddress)
            Console.WriteLine($"  ADDR {line}");
        return;
    }

    if (args.Contains("--apply-phone-enrichment"))
    {
        var enrichService = scope.ServiceProvider.GetRequiredService<CustomerFieldEnrichmentService>();
        var phonePath = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", "phone-enrichment.json");
        if (!File.Exists(phonePath))
        {
            Console.WriteLine($"Missing {phonePath}");
            return;
        }

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            await File.ReadAllTextAsync(phonePath)) ?? new();
        var phones = map
            .Where(kv => int.TryParse(kv.Key, out _))
            .ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        var updated = await enrichService.ApplyPhoneLookupAsync(phones);
        Console.WriteLine($"Applied phones to {updated} customers from {phones.Count} lookups.");
        return;
    }

    if (args.Contains("--apply-customer-enrichment"))
    {
        var enrichService = scope.ServiceProvider.GetRequiredService<CustomerFieldEnrichmentService>();
        var path = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", "customer-enrichment.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"Missing {path}");
            return;
        }

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CustomerFieldPatch>>(
            await File.ReadAllTextAsync(path),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        var patches = map
            .Where(kv => int.TryParse(kv.Key, out _))
            .ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        var updated = await enrichService.ApplyFieldEnrichmentAsync(patches);
        Console.WriteLine($"Applied field enrichment to {updated} customers from {patches.Count} patches.");
        return;
    }

    if (args.Contains("--purge-inactive-stops"))
    {
        var inactiveStops = await context.Stops.Where(s => s.Status == StopStatus.Inactive).ToListAsync();
        var stopIds = inactiveStops.Select(s => s.Id).ToList();
        var routeStops = await context.CustomerRouteStops
            .Where(crs => stopIds.Contains(crs.StopId))
            .ToListAsync();
        context.CustomerRouteStops.RemoveRange(routeStops);
        context.Stops.RemoveRange(inactiveStops);
        await context.SaveChangesAsync();
        Console.WriteLine($"Removed {inactiveStops.Count} inactive stops.");
        return;
    }

    if (args.Contains("--purge-inactive-routes"))
    {
        var removed = await DbInitializer.DeleteInactiveRoutesAsync(context);
        Console.WriteLine($"Removed {removed} inactive/non-standard routes.");
        return;
    }

    var syncArg = args.FirstOrDefault(a => a.StartsWith("--sync-route=", StringComparison.OrdinalIgnoreCase));
    if (syncArg != null)
    {
        var routeKey = syncArg["--sync-route=".Length..];
        var sheetSync = scope.ServiceProvider.GetRequiredService<RouteSheetSyncService>();
        var result = await sheetSync.SyncRouteAsync(routeKey);
        Console.WriteLine($"Synced {result.RouteName} from Google Sheets ({result.Imported} stops).");
        return;
    }

    if (args.Contains("--sync-all-routes"))
    {
        var sheetSync = scope.ServiceProvider.GetRequiredService<RouteSheetSyncService>();
        var results = await sheetSync.SyncAllRoutesAsync();
        var imported = results.Sum(r => r.Imported);
        Console.WriteLine($"Synced {results.Count} route sheets ({imported} stops imported).");
        return;
    }

    var importArg = args.FirstOrDefault(a => a.StartsWith("--import-route=", StringComparison.OrdinalIgnoreCase));
    if (importArg != null)
    {
        var routeKey = importArg["--import-route=".Length..];
        await ImportRouteAsync(context, stopImportService, app.Environment.ContentRootPath, routeKey);
        return;
    }

    if (args.Contains("--import-central-oh"))
    {
        await ImportRouteAsync(context, stopImportService, app.Environment.ContentRootPath, "central-oh");
        return;
    }

    if (args.Contains("--import-customers"))
    {
        var customerImportService = scope.ServiceProvider.GetRequiredService<CustomerImportService>();
        var csvPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", "customers.csv");
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("Customers CSV not found.", csvPath);

        await using var stream = File.OpenRead(csvPath);
        var result = await customerImportService.ReplaceAllFromCsvAsync(stream);
        Console.WriteLine($"Imported {result.Imported} customers.");
        return;
    }

    if (args.Contains("--export-customer-route-matrix"))
    {
        var matrixService = scope.ServiceProvider.GetRequiredService<CustomerRouteMatrixService>();
        var xlsxPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", CustomerRouteMatrixService.TemplateFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(xlsxPath)!);
        await using (var stream = File.Create(xlsxPath))
            await matrixService.ExportTemplateAsync(stream, app.Environment.ContentRootPath);
        Console.WriteLine($"Exported template to {xlsxPath}");
        return;
    }

    if (args.Contains("--export-customer-route-matrix-sample"))
    {
        var matrixService = scope.ServiceProvider.GetRequiredService<CustomerRouteMatrixService>();
        var xlsxPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", CustomerRouteMatrixService.SampleFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(xlsxPath)!);
        await using (var stream = File.Create(xlsxPath))
            await matrixService.ExportSampleAsync(stream, app.Environment.ContentRootPath);
        Console.WriteLine($"Exported sample spreadsheet to {xlsxPath}");
        return;
    }

    if (args.Contains("--import-customer-route-matrix"))
    {
        var matrixService = scope.ServiceProvider.GetRequiredService<CustomerRouteMatrixService>();
        var xlsxPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", CustomerRouteMatrixService.TemplateFileName);
        if (!File.Exists(xlsxPath))
            throw new FileNotFoundException("Customer route matrix Excel file not found.", xlsxPath);

        await using var stream = File.OpenRead(xlsxPath);
        var result = await matrixService.ImportAsync(stream);
        Console.WriteLine(
            $"Imported matrix: {result.CustomersAdded} customers added, {result.CustomersUpdated} updated, " +
            $"{result.AssignmentsAdded} assignments added, {result.AssignmentsUpdated} updated, " +
            $"{result.ContractsConfigured} bills configured.");
        return;
    }

    foreach (var (routeKey, routeName) in RouteImportMap.KeyToRouteName)
    {
        var route = await context.Routes.FirstOrDefaultAsync(r => r.RouteName == routeName);
        if (route == null || await context.Stops.AnyAsync(s => s.RouteId == route.Id))
            continue;

        var csvPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", $"{routeKey}.csv");
        if (!File.Exists(csvPath))
            continue;

        await using var stream = File.OpenRead(csvPath);
        await stopImportService.ImportAsync(route.Id, stream, replaceExisting: false);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static async Task ImportRouteAsync(
    ApplicationDbContext context,
    StopImportService stopImportService,
    string contentRoot,
    string routeKey)
{
    if (!RouteImportMap.KeyToRouteName.TryGetValue(routeKey, out var routeName))
        throw new InvalidOperationException($"Unknown route key '{routeKey}'.");

    var route = await context.Routes.FirstOrDefaultAsync(r => r.RouteName == routeName)
        ?? throw new InvalidOperationException($"{routeName} route not found.");

    var csvPath = Path.Combine(contentRoot, "Data", "Imports", $"{routeKey}.csv");
    if (!File.Exists(csvPath))
        throw new FileNotFoundException($"{routeName} CSV not found.", csvPath);

    await using var stream = File.OpenRead(csvPath);
    var result = await stopImportService.ImportAsync(route.Id, stream, replaceExisting: true);
    Console.WriteLine($"Imported {result.Imported} stops for {routeName}.");
}
