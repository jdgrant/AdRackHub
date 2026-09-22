using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sql =>
    {
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(3), null);
        sql.CommandTimeout(60);
    }));

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
builder.Services.Configure<DataForSeoOptions>(builder.Configuration.GetSection(DataForSeoOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<WaveApiService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<WavePocTokenStore>();
builder.Services.AddScoped<WaveSessionService>();
builder.Services.AddScoped<WaveInvoiceWorkflowService>();
builder.Services.AddHttpClient<DataForSeoClient>();
builder.Services.AddScoped<WaveSyncService>();
builder.Services.AddScoped<MonthlyBillingService>();
builder.Services.AddScoped<StopImportService>();
builder.Services.AddScoped<CustomerImportService>();
builder.Services.AddScoped<StopVisitService>();
builder.Services.AddScoped<BrochureScanService>();
builder.Services.AddSingleton<InvoicePdfStorageService>();
builder.Services.AddSingleton<InvoicePdfGenerator>();
builder.Services.AddScoped<BrochureOptimizeService>();
builder.Services.AddScoped<ProspectHotelDiscoveryService>();
builder.Services.AddSingleton<ProspectHotelDiscoveryJobService>();
builder.Services.AddScoped<KyBrochureProspectImportService>();
builder.Services.AddScoped<CustomerFieldEnrichmentService>();
builder.Services.AddScoped<CustomerGeocodeService>();
builder.Services.AddScoped<StopGeocodeService>();
builder.Services.AddScoped<CustomerNeedsMoreInfoService>();
builder.Services.AddScoped<HighValueProspectProximityService>();
builder.Services.AddScoped<CustomerRouteMatrixService>();
builder.Services.AddScoped<Sept2026ContractImportService>();
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
    var needsMoreInfoService = scope.ServiceProvider.GetRequiredService<CustomerNeedsMoreInfoService>();
    await needsMoreInfoService.RefreshAllAsync();

    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var uploadRoot = BrochureScanService.GetUploadRoot(env);
    Directory.CreateDirectory(uploadRoot);
    var invoiceUploadRoot = InvoicePdfStorageService.GetUploadRoot(env);
    Directory.CreateDirectory(invoiceUploadRoot);
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation("Brochure uploads directory: {UploadRoot}", uploadRoot);
    logger.LogInformation("Invoice PDF uploads directory: {UploadRoot}", invoiceUploadRoot);

    if (args.Contains("--set-invoice-receipt-both"))
    {
        var matchedIds = await context.Contacts
            .Where(ct => ct.SendInvoice && ct.Email != null && ct.Email.Trim() != "")
            .Select(ct => ct.CustomerId)
            .Distinct()
            .ToListAsync();
        var current = await context.Customers
            .Where(c => matchedIds.Contains(c.Id))
            .Select(c => c.InvoiceReceiptMethod)
            .ToListAsync();
        var updated = await context.Customers
            .Where(c => matchedIds.Contains(c.Id) && c.InvoiceReceiptMethod != InvoiceReceiptMethod.Both)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.InvoiceReceiptMethod, InvoiceReceiptMethod.Both));
        Console.WriteLine(
            $"Customers with a Send Invoice contact that has an email: {matchedIds.Count}. " +
            $"Already Both: {current.Count(m => m == InvoiceReceiptMethod.Both)}. " +
            $"Updated to Both: {updated} " +
            $"(from Mail: {current.Count(m => m == InvoiceReceiptMethod.Mail)}, " +
            $"from Email: {current.Count(m => m == InvoiceReceiptMethod.Email)}).");
        return;
    }

    if (args.Contains("--sample-invoice-pdf"))
    {
        var generator = scope.ServiceProvider.GetRequiredService<InvoicePdfGenerator>();
        var bytes = generator.Generate(InvoicePdfGenerator.Sample());
        var samplePath = Path.Combine(invoiceUploadRoot, "AdRack-sample.pdf");
        File.WriteAllBytes(samplePath, bytes);
        Console.WriteLine(samplePath);
        return;
    }

    if (args.Contains("--cleanup-wave-test-invoices"))
    {
        var session = scope.ServiceProvider.GetRequiredService<WaveSessionService>();
        var waveApi = scope.ServiceProvider.GetRequiredService<WaveApiService>();
        var credentials = await session.GetCredentialsAsync();
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            Console.WriteLine("Connect to Wave on Admin → Wave proof first.");
            return;
        }

        var invoices = await waveApi.ListRecentInvoicesAsync(
            accessToken: credentials.AccessToken,
            businessId: credentials.BusinessId);
        var testInvoices = invoices
            .Where(i => string.Equals(i.CustomerName, "test customer", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var keep = testInvoices
            .OrderByDescending(i => int.TryParse(i.InvoiceNumber, out var n) ? n : -1)
            .FirstOrDefault();
        var toDelete = testInvoices.Where(i => keep == null || i.WaveInvoiceId != keep.WaveInvoiceId).ToList();
        Console.WriteLine($"Test customer invoices: {testInvoices.Count}. Keeping #{keep?.InvoiceNumber ?? "none"}.");
        foreach (var item in toDelete)
        {
            var deleted = await waveApi.DeleteInvoiceAsync(item.WaveInvoiceId, accessToken: credentials.AccessToken);
            Console.WriteLine(
                deleted.Success
                    ? $"Deleted Wave invoice #{item.InvoiceNumber} ({item.Status})."
                    : $"Could not delete #{item.InvoiceNumber}: {deleted.ErrorMessage}");
        }
        return;
    }

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

    var discoverArg = args.FirstOrDefault(a => a.StartsWith("--discover-prospect-hotels", StringComparison.OrdinalIgnoreCase));
    if (discoverArg != null || args.Contains("--discover-prospect-hotels"))
    {
        var discovery = scope.ServiceProvider.GetRequiredService<ProspectHotelDiscoveryService>();
        var dryRun = args.Contains("--dry-run");
        var cityArg = args.FirstOrDefault(a => a.StartsWith("--city=", StringComparison.OrdinalIgnoreCase));
        var city = cityArg?["--city=".Length..];
        var routeArg = args.FirstOrDefault(a => a.StartsWith("--route-id=", StringComparison.OrdinalIgnoreCase));
        int? routeId = int.TryParse(routeArg?["--route-id=".Length..], out var rid) ? rid : null;
        var routesArg = args.FirstOrDefault(a => a.StartsWith("--route-ids=", StringComparison.OrdinalIgnoreCase));
        int[]? routeIds = routesArg?["--route-ids=".Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray();
        var statesArg = args.FirstOrDefault(a => a.StartsWith("--states=", StringComparison.OrdinalIgnoreCase));
        string[]? states = statesArg?["--states=".Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var maxArg = args.FirstOrDefault(a => a.StartsWith("--max-seeds=", StringComparison.OrdinalIgnoreCase));
        int? maxSeeds = int.TryParse(maxArg?["--max-seeds=".Length..], out var ms) ? ms : null;
        var skipArg = args.FirstOrDefault(a => a.StartsWith("--skip-seeds=", StringComparison.OrdinalIgnoreCase));
        int? skipSeeds = int.TryParse(skipArg?["--skip-seeds=".Length..], out var sk) ? sk : 0;

        Console.WriteLine(
            $"Discovering prospect hotels (dryRun={dryRun}, city={city ?? "(any)"}, routeId={routeId?.ToString() ?? "(any)"}, routeIds={(routeIds is { Length: > 0 } ? string.Join('|', routeIds) : "(any)")}, states={(states is { Length: > 0 } ? string.Join('|', states) : "(any)")}, maxSeeds={maxSeeds?.ToString() ?? "(all)"}, skip={skipSeeds})…");
        var progress = new Progress<ProspectHotelProgress>(p =>
            Console.WriteLine($"  [{p.CurrentIndex}/{p.SeedsSelected}] {p.Phase} {p.CurrentStopName} (within1={p.CandidatesWithinMile}, dups={p.SkippedDuplicates})"));
        var result = await discovery.DiscoverAsync(
            dryRun,
            maxSeeds,
            skipSeeds,
            routeId,
            city,
            routeIds,
            states,
            progress: progress);
        Console.WriteLine($"Done. Scanned {result.SeedsScanned}/{result.SeedsSelected}. Within 1 mi: {result.CandidatesWithinMile}. Duplicates: {result.SkippedDuplicates}. {(result.DryRun ? "Would add" : "Added")}: {(result.DryRun ? result.WouldAdd : result.Added)}.");
        foreach (var c in result.Candidates.OrderBy(c => c.DistanceMiles).Take(100))
            Console.WriteLine($"  + {c.StopName} ({c.DistanceMiles:0.00} mi near {c.NearStopName}) — {c.Address}");
        if (result.Candidates.Count > 100)
            Console.WriteLine($"  … and {result.Candidates.Count - 100} more");
        foreach (var msg in result.Messages.Take(30))
            Console.WriteLine($"  ! {msg}");
        return;
    }

    if (args.Contains("--repair-exit-contract-routes"))
    {
        var importService = scope.ServiceProvider.GetRequiredService<Sept2026ContractImportService>();
        var dryRun = args.Contains("--dry-run");
        var result = await importService.RepairDisconnectedExitRoutesAsync(dryRun);
        Console.WriteLine(
            $"Repair Exit contract routes ({(result.DryRun ? "dry-run" : "apply")}): " +
            $"{(result.DryRun ? "wouldRepair" : "repaired")}={(result.DryRun ? result.WouldRepair.Count : result.Repaired.Count)}, " +
            $"skipped={result.Skipped.Count}, errors={result.Errors.Count}.");
        foreach (var line in result.DryRun ? result.WouldRepair : result.Repaired)
            Console.WriteLine($"  + {line}");
        foreach (var line in result.Skipped)
            Console.WriteLine($"  skip {line}");
        foreach (var line in result.Errors)
            Console.WriteLine($"  ! {line}");
        return;
    }

    if (args.Contains("--import-sept-2026-contracts") || args.Contains("--verify-sept-2026-contracts"))
    {
        var importService = scope.ServiceProvider.GetRequiredService<Sept2026ContractImportService>();
        if (args.Contains("--verify-sept-2026-contracts") && !args.Contains("--import-sept-2026-contracts"))
        {
            var verifications = await importService.VerifyAsync();
            PrintSept2026ContractVerification(verifications);
            return;
        }

        var dryRun = args.Contains("--dry-run");
        var result = await importService.ImportAsync(dryRun);
        Console.WriteLine(
            $"Sept 2026 contracts ({(result.DryRun ? "dry-run" : "apply")}): seed={result.SeedCount}, " +
            $"{(result.DryRun ? "wouldCreate" : "created")}={(result.DryRun ? result.WouldCreate.Count : result.Created.Count)}, " +
            $"skipped={result.Skipped.Count}, errors={result.Errors.Count}.");
        foreach (var line in result.DryRun ? result.WouldCreate : result.Created)
            Console.WriteLine($"  + {line}");
        foreach (var line in result.Skipped)
            Console.WriteLine($"  skip {line}");
        foreach (var line in result.Errors)
            Console.WriteLine($"  ! {line}");
        if (result.Verifications.Count > 0)
            PrintSept2026ContractVerification(result.Verifications);
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

    // Named pipeline: Brochure Prospect Batch
    // Prefer: --import-scan-batch=20260825  (or Batch-20260825)
    // Legacy aliases: --import-scan-batch-20260824 / --import-scan-batch-20260825
    var scanBatchArg = args.FirstOrDefault(a =>
        a.StartsWith("--import-scan-batch=", StringComparison.OrdinalIgnoreCase));
    string? scanBatchId = null;
    if (!string.IsNullOrWhiteSpace(scanBatchArg))
        scanBatchId = scanBatchArg["--import-scan-batch=".Length..].Trim();
    else
    {
        var legacy = args.FirstOrDefault(a =>
            a.StartsWith("--import-scan-batch-", StringComparison.OrdinalIgnoreCase)
            && !a.Equals("--import-scan-batch-", StringComparison.OrdinalIgnoreCase));
        if (legacy != null)
            scanBatchId = legacy["--import-scan-batch-".Length..].Trim();
    }

    if (!string.IsNullOrWhiteSpace(scanBatchId))
    {
        var batchKey = scanBatchId.StartsWith("Batch-", StringComparison.OrdinalIgnoreCase)
            ? scanBatchId["Batch-".Length..]
            : scanBatchId;
        batchKey = batchKey.Replace("-", "", StringComparison.Ordinal);
        var importService = scope.ServiceProvider.GetRequiredService<KyBrochureProspectImportService>();
        var jsonPath = Path.Combine(env.ContentRootPath, "Data", "Imports", $"ScanBatch{batchKey}.json");
        var scansDir = Path.Combine(env.ContentRootPath, "Data", "Scans", $"Batch-{batchKey}");
        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"Missing import JSON: {jsonPath}");
            return;
        }

        if (!Directory.Exists(scansDir))
        {
            Console.WriteLine($"Missing scans folder: {scansDir}");
            return;
        }

        var attachOnly = args.Contains("--attach-only");
        var result = await importService.ImportAsync(jsonPath, scansDir, createMissing: !attachOnly);
        Console.WriteLine(
            $"Brochure Prospect Batch {batchKey}: {result.Created} created, {result.SkippedExisting} skipped (already exist), {result.BrochuresAttached} brochures attached.");
        if (result.MissingScanFiles.Count > 0)
            Console.WriteLine($"Missing scan files: {string.Join(", ", result.MissingScanFiles)}");
        if (result.BrochureErrors.Count > 0)
            Console.WriteLine($"Brochure errors: {string.Join(" | ", result.BrochureErrors)}");
        if (result.UnmatchedNames.Count > 0)
            Console.WriteLine($"Unmatched attractions: {string.Join(" | ", result.UnmatchedNames)}");
        foreach (var name in result.CreatedNames)
            Console.WriteLine($"  + {name}");
        foreach (var name in result.SkippedNames)
            Console.WriteLine($"  skip {name}");
        return;
    }

    if (args.Contains("--dump-customer-names"))
    {
        var rows = await context.Customers.AsNoTracking()
            .OrderBy(c => c.CustomerName)
            .Select(c => new { c.Id, c.Type, c.CustomerName, c.City, c.State })
            .ToListAsync();
        foreach (var row in rows)
            Console.WriteLine($"{row.Id}\t{row.Type}\t{row.CustomerName}\t{row.City}\t{row.State}");
        Console.WriteLine($"COUNT={rows.Count}");
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

    if (args.Contains("--geocode-customers"))
    {
        var geocodeService = scope.ServiceProvider.GetRequiredService<CustomerGeocodeService>();
        var force = args.Contains("--force");
        var limitArg = args.FirstOrDefault(a => a.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase));
        int? limit = int.TryParse(limitArg?["--limit=".Length..], out var lim) ? lim : null;
        Console.WriteLine($"Geocoding customers and prospects (force={force}, limit={limit?.ToString() ?? "all"})…");
        var result = await geocodeService.GeocodeMissingAsync(force, limit);
        Console.WriteLine($"Geocode: {result.Updated} updated, {result.NotFound} not found, {result.SkippedNoQuery} no address, {result.Failed} failed of {result.Eligible} eligible.");
        foreach (var line in result.Updates)
            Console.WriteLine($"  OK {line}");
        foreach (var line in result.Failures)
            Console.WriteLine($"  ! {line}");
        return;
    }

    if (args.Contains("--geocode-stops"))
    {
        var stopGeocode = scope.ServiceProvider.GetRequiredService<StopGeocodeService>();
        var limitArg = args.FirstOrDefault(a => a.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase));
        int? limit = int.TryParse(limitArg?["--limit=".Length..], out var lim) ? lim : null;
        var fixedRows = await stopGeocode.FixKnownBadMidTnRowsAsync();
        if (fixedRows > 0)
            Console.WriteLine($"Fixed {fixedRows} mangled Mid-TN stop row(s).");
        Console.WriteLine($"Geocoding stops missing coordinates (limit={limit?.ToString() ?? "all"})…");
        var result = await stopGeocode.GeocodeMissingAsync(limit);
        Console.WriteLine($"Stop geocode: {result.Updated} updated, {result.NotFound} not found, {result.SkippedNoQuery} no address, {result.Failed} failed of {result.Eligible} eligible.");
        foreach (var line in result.Updates)
            Console.WriteLine($"  OK {line}");
        foreach (var line in result.Failures)
            Console.WriteLine($"  ! {line}");
        return;
    }

    if (args.Contains("--mark-high-value-proximity"))
    {
        var proximity = scope.ServiceProvider.GetRequiredService<HighValueProspectProximityService>();
        var dryRun = args.Contains("--dry-run");
        var replace = args.Contains("--replace");
        var milesArg = args.FirstOrDefault(a => a.StartsWith("--miles=", StringComparison.OrdinalIgnoreCase));
        var miles = double.TryParse(milesArg?["--miles=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMiles)
            ? parsedMiles
            : HighValueProspectProximityService.DefaultRadiusMiles;
        Console.WriteLine($"Marking high-value prospects within {miles:0.##} miles of a customer or exit stop (dryRun={dryRun}, replace={replace})…");
        var result = await proximity.ApplyAsync(miles, dryRun, replace);
        Console.WriteLine($"Anchors: {result.CustomerAnchorCount} customers, {result.ExitStopAnchorCount} exit stops. Prospects: {result.ProspectsConsidered} ({result.SkippedNoCoords} skipped, no coords).");
        Console.WriteLine($"Within {result.RadiusMiles:0.##} mi: {result.Matches.Count} ({result.Marked} newly marked, {result.Matches.Count - result.Marked} already high value). Unmarked: {result.Unmarked}.");
        foreach (var match in result.Matches.OrderBy(m => m.Hit.Miles))
        {
            var flag = match.AlreadyHighValue ? "keep" : "mark";
            Console.WriteLine($"  {flag} #{match.ProspectId} {match.ProspectName}: {match.Hit.Summary}");
        }
        foreach (var line in result.Cleared)
            Console.WriteLine($"  clear {line}");
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

    if (args.Contains("--route-map-status"))
    {
        var routes = await context.Routes
            .OrderBy(r => r.RouteName)
            .Select(r => new
            {
                r.RouteName,
                Stops = r.Stops.Count(s => s.Status == StopStatus.Active),
                Mapped = r.Stops.Count(s => s.Status == StopStatus.Active && s.Latitude != null && s.Longitude != null)
            })
            .ToListAsync();
        foreach (var row in routes)
            Console.WriteLine($"{row.RouteName}: {row.Mapped}/{row.Stops} stops have coordinates");
        var totalStops = routes.Sum(r => r.Stops);
        var totalMapped = routes.Sum(r => r.Mapped);
        Console.WriteLine($"Total: {totalMapped}/{totalStops} active stops have coordinates.");
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

    var seededRoutes = await context.Routes.ToListAsync();
    foreach (var (routeKey, routeName) in RouteImportMap.KeyToRouteName)
    {
        var route = RouteSheetMap.FindImportRoute(seededRoutes, routeName);
        if (route == null || await context.Stops.AnyAsync(s => s.RouteId == route.Id))
            continue;

        var csvPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Imports", $"{routeKey}.csv");
        if (!File.Exists(csvPath))
            continue;

        await using var stream = File.OpenRead(csvPath);
        await stopImportService.ImportAsync(route.Id, stream, replaceExisting: false);
        Console.WriteLine($"Imported missing stops for {route.RouteName} from {routeKey}.csv.");
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

static void PrintSept2026ContractVerification(List<Sept2026ContractVerifyItem> verifications)
{
    var passed = verifications.Count(v => v.Passed);
    Console.WriteLine($"Verification: {passed}/{verifications.Count} passed.");
    foreach (var item in verifications)
    {
        var mark = item.Passed ? "PASS" : "FAIL";
        Console.WriteLine(
            $"  [{mark}] {item.Product} · {item.BillName} · #{item.CustomerId} {item.CustomerName} · " +
            $"contract #{item.ContractId} {item.ContractName} · ${item.ActualTotal:0.00} (expected ${item.ExpectedTotal:0.00})");
        foreach (var failure in item.Failures)
            Console.WriteLine($"      - {failure}");
    }
}

static async Task ImportRouteAsync(
    ApplicationDbContext context,
    StopImportService stopImportService,
    string contentRoot,
    string routeKey)
{
    if (!RouteImportMap.KeyToRouteName.TryGetValue(routeKey, out var routeName))
        throw new InvalidOperationException($"Unknown route key '{routeKey}'.");

    var allRoutes = await context.Routes.ToListAsync();
    var route = RouteSheetMap.FindImportRoute(allRoutes, routeName)
        ?? throw new InvalidOperationException($"{routeName} route not found.");

    var csvPath = Path.Combine(contentRoot, "Data", "Imports", $"{routeKey}.csv");
    if (!File.Exists(csvPath))
        throw new FileNotFoundException($"{routeName} CSV not found.", csvPath);

    await using var stream = File.OpenRead(csvPath);
    var result = await stopImportService.ImportAsync(route.Id, stream, replaceExisting: true);
    Console.WriteLine($"Imported {result.Imported} stops for {routeName}.");
}
