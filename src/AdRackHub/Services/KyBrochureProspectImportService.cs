using System.Text.Json;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class KyBrochureProspectImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ApplicationDbContext _context;
    private readonly BrochureScanService _brochureScanService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<KyBrochureProspectImportService> _logger;

    public KyBrochureProspectImportService(
        ApplicationDbContext context,
        BrochureScanService brochureScanService,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment environment,
        ILogger<KyBrochureProspectImportService> logger)
    {
        _context = context;
        _brochureScanService = brochureScanService;
        _userManager = userManager;
        _environment = environment;
        _logger = logger;
    }

    public async Task<KyBrochureProspectImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "Imports", "KYBrochures.json");
        var scansDir = Path.Combine(_environment.ContentRootPath, "Data", "Scans");

        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Missing import file: {jsonPath}");

        var rows = JsonSerializer.Deserialize<List<KyBrochureRow>>(
            await File.ReadAllTextAsync(jsonPath, cancellationToken),
            JsonOptions) ?? new List<KyBrochureRow>();

        if (rows.Count == 0)
            throw new InvalidOperationException("KYBrochures.json has no rows.");

        var accountManagerId = await ResolveAccountManagerIdAsync(cancellationToken);

        var existingNames = await _context.Customers
            .Select(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        var existingLookup = new HashSet<string>(
            existingNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(NormalizeName),
            StringComparer.OrdinalIgnoreCase);

        var groups = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Attraction))
            .GroupBy(r => NormalizeName(r.Attraction!))
            .ToList();

        var result = new KyBrochureProspectImportResult
        {
            SourceRows = rows.Count,
            UniqueAttractions = groups.Count
        };

        foreach (var group in groups)
        {
            var primary = group.First();
            var attractionName = primary.Attraction!.Trim();
            var key = NormalizeName(attractionName);

            if (existingLookup.Contains(key))
            {
                result.SkippedExisting++;
                result.SkippedNames.Add(attractionName);
                continue;
            }

            var customer = new Customer
            {
                CustomerName = attractionName,
                Status = CustomerStatus.Active,
                Type = CustomerType.Prospect,
                AccountManagerId = accountManagerId,
                Address = NullIfEmpty(primary.Address),
                City = NullIfEmpty(primary.City),
                State = NullIfEmpty(primary.State),
                Zip = NullIfEmpty(primary.Zip),
                WebUrl = NullIfEmpty(primary.WebURL)
            };
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync(cancellationToken);

            existingLookup.Add(key);
            result.Created++;

            var hasAddress = !string.IsNullOrWhiteSpace(primary.Address)
                || !string.IsNullOrWhiteSpace(primary.City)
                || !string.IsNullOrWhiteSpace(primary.State)
                || !string.IsNullOrWhiteSpace(primary.Zip);

            if (hasAddress || !string.IsNullOrWhiteSpace(primary.WebURL))
            {
                _context.Contacts.Add(new Contact
                {
                    CustomerId = customer.Id,
                    Name = attractionName,
                    Address = NullIfEmpty(primary.Address),
                    City = NullIfEmpty(primary.City),
                    State = NullIfEmpty(primary.State),
                    Zip = NullIfEmpty(primary.Zip),
                    WebUrl = NullIfEmpty(primary.WebURL),
                    Role = ContactRole.Primary
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            foreach (var row in group)
            {
                if (string.IsNullOrWhiteSpace(row.ImageName))
                    continue;

                var sourcePath = Path.Combine(scansDir, row.ImageName.Trim());
                if (!File.Exists(sourcePath))
                {
                    result.MissingScanFiles.Add(row.ImageName);
                    _logger.LogWarning("Scan file missing for {Attraction}: {File}", attractionName, row.ImageName);
                    continue;
                }

                try
                {
                    await _brochureScanService.SaveFromPathAsync(
                        customer.Id,
                        sourcePath,
                        notes: $"Imported from Data/Scans/{row.ImageName}",
                        cancellationToken);
                    result.BrochuresAttached++;
                }
                catch (Exception ex)
                {
                    result.BrochureErrors.Add($"{row.ImageName}: {ex.Message}");
                    _logger.LogError(ex, "Failed attaching brochure {File} to {Attraction}", row.ImageName, attractionName);
                }
            }

            result.CreatedNames.Add(attractionName);
        }

        return result;
    }

    public async Task<KyBrochurePngReplaceResult> ReplaceScansWithPngsAsync(CancellationToken cancellationToken = default)
    {
        var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "Imports", "KYBrochures.json");
        var scansDir = Path.Combine(_environment.ContentRootPath, "Data", "Scans");

        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Missing import file: {jsonPath}");

        var rows = JsonSerializer.Deserialize<List<KyBrochureRow>>(
            await File.ReadAllTextAsync(jsonPath, cancellationToken),
            JsonOptions) ?? new List<KyBrochureRow>();

        var customers = await _context.Customers.AsNoTracking().ToListAsync(cancellationToken);
        var byName = customers
            .GroupBy(c => NormalizeName(c.CustomerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).First(), StringComparer.OrdinalIgnoreCase);

        var result = new KyBrochurePngReplaceResult();
        var touchedCustomerIds = new HashSet<int>();
        var processedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Prefer PNG paths for each JSON row (dedupe by stem).
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Attraction) || string.IsNullOrWhiteSpace(row.ImageName))
                continue;

            var stem = Path.GetFileNameWithoutExtension(row.ImageName.Trim());
            if (!processedStems.Add(stem))
                continue;

            var pngPath = Path.Combine(scansDir, stem + ".png");
            if (!File.Exists(pngPath))
            {
                result.MissingPngs.Add(stem + ".png");
                continue;
            }

            var key = NormalizeName(row.Attraction);
            if (!byName.TryGetValue(key, out var customer))
            {
                result.UnmatchedAttractions.Add(row.Attraction);
                continue;
            }

            await ReplaceStemScansAsync(customer.Id, stem, pngPath, cancellationToken);
            touchedCustomerIds.Add(customer.Id);
            result.Replaced++;
            result.Mappings.Add($"{stem}.png -> #{customer.Id} {customer.CustomerName}");
        }

        // Extra PNGs in Scans not listed in JSON (e.g. AlbanyKy.png)
        foreach (var png in Directory.EnumerateFiles(scansDir, "*.png"))
        {
            var stem = Path.GetFileNameWithoutExtension(png);
            if (!processedStems.Add(stem))
                continue;

            // Prefer an existing scan whose original filename stem matches this PNG.
            var scanMatchId = await _context.CustomerBrochureScans
                .AsNoTracking()
                .Where(s => s.OriginalFileName.StartsWith(stem + "."))
                .OrderByDescending(s => s.Id)
                .Select(s => s.CustomerId)
                .FirstOrDefaultAsync(cancellationToken);

            var match = scanMatchId > 0 ? customers.FirstOrDefault(c => c.Id == scanMatchId) : null;
            if (match == null)
            {
                result.OrphanPngs.Add(Path.GetFileName(png));
                continue;
            }

            await ReplaceStemScansAsync(match.Id, stem, png, cancellationToken);
            touchedCustomerIds.Add(match.Id);
            result.Replaced++;
            result.Mappings.Add($"{stem}.png -> #{match.Id} {match.CustomerName} (by existing scan)");
        }

        result.CustomerIds.AddRange(touchedCustomerIds.OrderBy(id => id));
        return result;
    }

    private async Task ReplaceStemScansAsync(
        int customerId,
        string stem,
        string pngPath,
        CancellationToken cancellationToken)
    {
        var existing = await _context.CustomerBrochureScans
            .Where(s => s.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        foreach (var scan in existing)
        {
            var scanStem = Path.GetFileNameWithoutExtension(scan.OriginalFileName);
            if (!string.Equals(scanStem, stem, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                await _brochureScanService.DeleteAsync(scan.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete old scan {ScanId} for customer {CustomerId}", scan.Id, customerId);
            }
        }

        // Avoid duplicate PNG if already present with same original name
        var alreadyPng = await _context.CustomerBrochureScans.AnyAsync(
            s => s.CustomerId == customerId
                 && s.OriginalFileName == Path.GetFileName(pngPath),
            cancellationToken);
        if (alreadyPng)
            return;

        await _brochureScanService.SaveFromPathAsync(
            customerId,
            pngPath,
            notes: $"Imported from Data/Scans/{Path.GetFileName(pngPath)}",
            cancellationToken);
    }

    private async Task<string?> ResolveAccountManagerIdAsync(CancellationToken cancellationToken)
    {
        var jon = await _userManager.Users
            .FirstOrDefaultAsync(u => u.Email == "jon@ad-rack.net" || u.DisplayName == "Jon Grant", cancellationToken);
        return jon?.Id;
    }

    private static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class KyBrochureRow
    {
        public string? Attraction { get; set; }
        public string? ImageName { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? WebURL { get; set; }
    }
}

public class KyBrochureProspectImportResult
{
    public int SourceRows { get; set; }
    public int UniqueAttractions { get; set; }
    public int Created { get; set; }
    public int SkippedExisting { get; set; }
    public int BrochuresAttached { get; set; }
    public List<string> CreatedNames { get; } = new();
    public List<string> SkippedNames { get; } = new();
    public List<string> MissingScanFiles { get; } = new();
    public List<string> BrochureErrors { get; } = new();
}

public class KyBrochurePngReplaceResult
{
    public int Replaced { get; set; }
    public List<int> CustomerIds { get; } = new();
    public List<string> Mappings { get; } = new();
    public List<string> MissingPngs { get; } = new();
    public List<string> UnmatchedAttractions { get; } = new();
    public List<string> OrphanPngs { get; } = new();
}
