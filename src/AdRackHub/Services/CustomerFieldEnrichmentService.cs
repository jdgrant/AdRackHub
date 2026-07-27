using System.Text.Json;
using System.Text.RegularExpressions;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class CustomerFieldEnrichmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex WebsiteNoteRegex = new(
        @"^\s*Website:\s*(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CustomerFieldEnrichmentService> _logger;

    public CustomerFieldEnrichmentService(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        ILogger<CustomerFieldEnrichmentService> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    public async Task<CustomerFieldEnrichmentResult> EnrichAsync(CancellationToken cancellationToken = default)
    {
        var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "Imports", "KYBrochures.json");
        var kyByName = new Dictionary<string, KyBrochureRow>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(jsonPath))
        {
            var rows = JsonSerializer.Deserialize<List<KyBrochureRow>>(
                await File.ReadAllTextAsync(jsonPath, cancellationToken),
                JsonOptions) ?? new List<KyBrochureRow>();

            foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.Attraction)))
            {
                var key = NormalizeName(row.Attraction!);
                if (!kyByName.ContainsKey(key))
                    kyByName[key] = row;
            }
        }

        var customers = await _context.Customers
            .Include(c => c.Contacts)
            .Include(c => c.Notes)
            .Include(c => c.BrochureScans)
            .ToListAsync(cancellationToken);

        var result = new CustomerFieldEnrichmentResult { TotalCustomers = customers.Count };

        foreach (var customer in customers)
        {
            var before = Snapshot(customer);
            var sources = new List<string>();

            var primary = customer.Contacts
                .OrderBy(c => c.Role == ContactRole.Primary ? 0 : 1)
                .ThenBy(c => c.Id)
                .FirstOrDefault();

            if (primary != null)
            {
                if (ApplyIfEmpty(customer, primary.Address, primary.City, primary.State, primary.Zip, primary.Phone, primary.Email, primary.WebUrl))
                    sources.Add("primary contact");
            }

            var websiteNote = customer.Notes
                .Select(n => WebsiteNoteRegex.Match(n.Body ?? ""))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value.Trim())
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

            if (string.IsNullOrWhiteSpace(customer.WebUrl) && !string.IsNullOrWhiteSpace(websiteNote))
            {
                customer.WebUrl = websiteNote;
                sources.Add("website note");
            }

            if (kyByName.TryGetValue(NormalizeName(customer.CustomerName), out var ky))
            {
                if (ApplyIfEmpty(
                        customer,
                        ky.Address,
                        ky.City,
                        ky.State,
                        ky.Zip,
                        phone: null,
                        email: null,
                        webUrl: ky.WebURL))
                    sources.Add("KYBrochures.json");
            }

            // Also match by brochure original filename stem against KY image names.
            if (sources.Count == 0 || StillMissingCore(customer))
            {
                foreach (var scan in customer.BrochureScans)
                {
                    var stem = Path.GetFileNameWithoutExtension(scan.OriginalFileName);
                    var kyByImage = kyByName.Values.FirstOrDefault(r =>
                        string.Equals(
                            Path.GetFileNameWithoutExtension(r.ImageName),
                            stem,
                            StringComparison.OrdinalIgnoreCase));
                    if (kyByImage == null)
                        continue;

                    if (ApplyIfEmpty(
                            customer,
                            kyByImage.Address,
                            kyByImage.City,
                            kyByImage.State,
                            kyByImage.Zip,
                            phone: null,
                            email: null,
                            webUrl: kyByImage.WebURL))
                        sources.Add($"KY image {stem}");
                    break;
                }
            }

            var after = Snapshot(customer);
            if (before != after)
            {
                result.Updated++;
                result.Updates.Add($"{customer.Id}|{customer.CustomerName}|{string.Join("+", sources.Distinct())}|{DescribeDelta(before, after)}");
            }

            if (customer.BrochureScans.Any())
            {
                result.WithBrochure++;
                if (string.IsNullOrWhiteSpace(customer.Phone))
                    result.MissingPhone.Add($"{customer.Id}|{customer.CustomerName}|{customer.WebUrl}|{customer.City}|{customer.State}");
                if (string.IsNullOrWhiteSpace(customer.WebUrl))
                    result.MissingWeb.Add($"{customer.Id}|{customer.CustomerName}");
                if (string.IsNullOrWhiteSpace(customer.Address) && string.IsNullOrWhiteSpace(customer.City))
                    result.MissingAddress.Add($"{customer.Id}|{customer.CustomerName}");
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<int> ApplyPhoneLookupAsync(
        IReadOnlyDictionary<int, string> phonesByCustomerId,
        CancellationToken cancellationToken = default)
    {
        var ids = phonesByCustomerId.Keys.ToList();
        var customers = await _context.Customers
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var updated = 0;
        foreach (var customer in customers)
        {
            if (!phonesByCustomerId.TryGetValue(customer.Id, out var phone))
                continue;
            phone = NormalizePhone(phone);
            if (string.IsNullOrWhiteSpace(phone))
                continue;
            if (!string.IsNullOrWhiteSpace(customer.Phone))
                continue;

            customer.Phone = phone;
            updated++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<int> ApplyFieldEnrichmentAsync(
        IReadOnlyDictionary<int, CustomerFieldPatch> patches,
        CancellationToken cancellationToken = default)
    {
        var ids = patches.Keys.ToList();
        var customers = await _context.Customers
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var updated = 0;
        foreach (var customer in customers)
        {
            if (!patches.TryGetValue(customer.Id, out var patch))
                continue;

            if (ApplyIfEmpty(
                    customer,
                    patch.Address,
                    patch.City,
                    patch.State,
                    patch.Zip,
                    patch.Phone,
                    patch.Email,
                    patch.WebUrl))
                updated++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private static bool StillMissingCore(Customer customer) =>
        string.IsNullOrWhiteSpace(customer.Address)
        || string.IsNullOrWhiteSpace(customer.City)
        || string.IsNullOrWhiteSpace(customer.WebUrl);

    private static bool ApplyIfEmpty(
        Customer customer,
        string? address,
        string? city,
        string? state,
        string? zip,
        string? phone,
        string? email,
        string? webUrl)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(customer.Address) && !string.IsNullOrWhiteSpace(address))
        {
            customer.Address = address.Trim();
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(customer.City) && !string.IsNullOrWhiteSpace(city))
        {
            customer.City = city.Trim();
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(customer.State) && !string.IsNullOrWhiteSpace(state))
        {
            customer.State = state.Trim();
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(customer.Zip) && !string.IsNullOrWhiteSpace(zip))
        {
            customer.Zip = zip.Trim();
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(customer.Phone) && !string.IsNullOrWhiteSpace(phone))
        {
            customer.Phone = NormalizePhone(phone);
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(customer.Email) && !string.IsNullOrWhiteSpace(email))
        {
            customer.Email = email.Trim();
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(customer.WebUrl) && !string.IsNullOrWhiteSpace(webUrl))
        {
            customer.WebUrl = webUrl.Trim();
            changed = true;
        }
        return changed;
    }

    private static string Snapshot(Customer c) =>
        $"{c.Address}|{c.City}|{c.State}|{c.Zip}|{c.Phone}|{c.Email}|{c.WebUrl}";

    private static string DescribeDelta(string before, string after)
    {
        var b = before.Split('|');
        var a = after.Split('|');
        var labels = new[] { "Address", "City", "State", "Zip", "Phone", "Email", "WebUrl" };
        var parts = new List<string>();
        for (var i = 0; i < labels.Length; i++)
        {
            if (!string.Equals(b[i], a[i], StringComparison.Ordinal))
                parts.Add($"{labels[i]}={a[i]}");
        }
        return string.Join("; ", parts);
    }

    private static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;
        var trimmed = phone.Trim();
        var digits = Regex.Replace(trimmed, @"[^\d+]", "");
        if (digits.Length < 7)
            return null;
        return trimmed;
    }

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

public class CustomerFieldEnrichmentResult
{
    public int TotalCustomers { get; set; }
    public int Updated { get; set; }
    public int WithBrochure { get; set; }
    public List<string> Updates { get; } = new();
    public List<string> MissingPhone { get; } = new();
    public List<string> MissingWeb { get; } = new();
    public List<string> MissingAddress { get; } = new();
}

public class CustomerFieldPatch
{
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? WebUrl { get; set; }
}
