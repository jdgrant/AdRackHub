using System.Text.Json;
using System.Text.RegularExpressions;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class DriverNotesSheetImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, string> NameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BUTLER COUNTY VB MAGAZINE"] = "BUTLER COUNTY VISITORS BUREAU",
        ["CINCY REGION VISITORS GUIDE"] = "MEET NKY/CINCY REGION MAG",
        ["MEDINA COUNTY CVB"] = "MEDINA COUNTY CVB",
        ["OHIO AMISH COUNTRY"] = "HOLMES CO/OH AMISH COUNTRY",
        ["OHIO'S AMISH COUNTRY"] = "OHIO'S AMISH COUNTRY",
        ["STEPHEN FOSTER"] = "STEPHEN FOSTER STORY",
        ["CAMBRIDGE/GUERNSEY"] = "CAMBRIDGE/GUERNSEY COUNTY VISITORS",
        ["CANTON TRAVEL GUIDE"] = "VISIT CANTON",
        ["DARKE COUNTY CVB"] = "DARKE COUNTY VISITORS BUREAU",
        ["HIDDEN RIVER"] = "AMERICAN CAVE MUSEUM/HIDDEN RIVER",
        ["LOST RIVER CAVE"] = "LOST RIVER CAVE",
        ["HARRISON CO"] = "HARRISON CO/HISTORIC CORYDON",
        ["LOUISVILLE Z"] = "LOUISVILLE ZOO",
        ["SIP OHIO WINE"] = "OHIO GRAPE - SIP MAG",
        ["GALLIA COUNTY"] = "GALLIA COUNTY CVB",
        ["MILLER FERRIES"] = "MILLER BOAT LINE",
        ["SENECA CAVERNS"] = "SENECA CAVERNS",
        ["RABBIT RUN"] = "RABBIT RUN THEATER",
        ["TUSCARAWAS"] = "TUSCARAWAS CO CVB",
        ["ZANESVILLE"] = "ZANESVILLE CVB",
        ["LOGAN COUNTY"] = "LOGAN CO OHIO TOURISM",
        ["NOBLE COUNTY"] = "NOBLE COUNTY CVB",
        ["SPRINGFIELD OH"] = "GREATER SPRINGFIELD CVB",
        ["MIAMI CO CVB"] = "MIAMI CO CVB",
        ["GREAT MIAMI RIVERWAY"] = "MIAMI CONSERVANCY DISTRICT",
        ["CLINTON CO"] = "CLINTON COUNTY TOURISM",
        ["LICKING VALLEY"] = "LICKING VALLEY ADVENTURES",
        ["MARKET AMERICA"] = "MARKETAMERICA(REDCOUPONMAG)",
        ["KENTUCKY'S CAVELAND"] = "KENTUCKY'S CAVELAND",
        ["HOLIDAY WORLD"] = "HOLIDAY WORLD",
        ["KENTUCKY KINGDOM"] = "KENTUCKY KINGDOM",
        ["SQUIRE BOONE"] = "SQUIRE BOONE CAVERNS",
        ["FRANKFORT"] = "FRANKFORT TOURISM",
    };

    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public DriverNotesSheetImportService(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    public async Task<DriverNotesSheetImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_environment.ContentRootPath, "Data", "Imports", "DriverNotesFromSheets.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing {path}");

        var rows = JsonSerializer.Deserialize<List<DriverNotesSheetRow>>(
            await File.ReadAllTextAsync(path, cancellationToken),
            JsonOptions) ?? new List<DriverNotesSheetRow>();

        var result = new DriverNotesSheetImportResult { SourceRows = rows.Count };
        var today = DateOnly.FromDateTime(DateTime.Today);

        var customers = await _context.Customers
            .AsNoTracking()
            .Select(c => new CustomerHit(c.Id, c.CustomerName, c.Type))
            .ToListAsync(cancellationToken);

        var routes = await _context.Routes
            .AsNoTracking()
            .Select(r => new RouteHit(r.Id, r.RouteName))
            .ToListAsync(cancellationToken);

        var stops = await _context.Stops
            .AsNoTracking()
            .Where(s => s.Status == StopStatus.Active)
            .Select(s => new StopHit(s.Id, s.RouteId, s.StopName))
            .ToListAsync(cancellationToken);

        var contracts = await _context.CustomerContracts
            .Include(c => c.ContractRoutes)
            .ToListAsync(cancellationToken);

        var pending = new Dictionary<int, string>();

        foreach (var row in rows)
        {
            var notes = CleanNotes(row.Notes);
            if (string.IsNullOrEmpty(notes))
            {
                result.Skipped.Add($"{row.BrochureName}: empty notes");
                continue;
            }

            var routeId = ResolveRouteId(row, routes, stops);
            if (routeId == null)
            {
                result.UnmatchedRoutes.Add($"{row.Title} / {row.BrochureName}");
                continue;
            }

            var customer = MatchCustomer(row.BrochureName, customers);
            if (customer == null)
            {
                result.UnmatchedCustomers.Add($"{row.BrochureName} ({row.Title})");
                continue;
            }

            var matches = contracts
                .Where(c => c.CustomerId == customer.Id)
                .Where(c => BillingDueCalculator.IsActiveContract(c, today))
                .Where(c => c.ContractRoutes.Any(cr => cr.RouteId == routeId.Value))
                .ToList();

            if (matches.Count == 0)
            {
                result.NoContract.Add($"{customer.CustomerName} on route {routeId}");
                continue;
            }

            foreach (var contract in matches)
                MergeNotes(pending, contract.Id, notes);
        }

        foreach (var (contractId, notes) in pending)
        {
            var contract = contracts.First(c => c.Id == contractId);
            if (string.Equals(contract.DriversNotes, notes, StringComparison.Ordinal))
            {
                result.Unchanged++;
                continue;
            }

            contract.DriversNotes = Truncate(notes, 200);
            result.Updated++;
            result.UpdatedNames.Add($"{contract.CustomerId}:{contract.ContractName} => {contract.DriversNotes}");
        }

        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static void MergeNotes(Dictionary<int, string> pending, int contractId, string notes)
    {
        if (!pending.TryGetValue(contractId, out var existing) || string.IsNullOrWhiteSpace(existing))
        {
            pending[contractId] = notes;
            return;
        }

        if (existing.Contains(notes, StringComparison.OrdinalIgnoreCase))
            return;
        if (notes.Contains(existing, StringComparison.OrdinalIgnoreCase))
        {
            pending[contractId] = notes;
            return;
        }

        pending[contractId] = Truncate($"{existing}; {notes}", 200);
    }

    private static int? ResolveRouteId(
        DriverNotesSheetRow row,
        IReadOnlyList<RouteHit> routes,
        IReadOnlyList<StopHit> stops)
    {
        var title = row.Title ?? string.Empty;
        if (string.Equals(row.Sheet, "rest", StringComparison.OrdinalIgnoreCase))
        {
            var number = Regex.Match(title, @"#\s*(\d+)");
            if (!number.Success)
                return null;
            var prefix = number.Groups[1].Value;
            var stop = stops.FirstOrDefault(s => Regex.IsMatch(s.StopName, $@"^{Regex.Escape(prefix)}\s*[-–]"));
            return stop?.RouteId;
        }

        var needle = HotelRouteNeedle(title);
        if (needle == null)
            return null;

        var route = routes.FirstOrDefault(r =>
            RouteProductHelper.FromRouteName(r.RouteName) == RouteProduct.Exits
            && (RouteNaming.DisplayName(r.RouteName).Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.RouteName.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        return route?.Id;
    }

    private static string? HotelRouteNeedle(string title)
    {
        var t = title.ToUpperInvariant();
        if (t.Contains("CINCINNATI")) return "CINCINNATI";
        if (t.Contains("LOUISVILLE")) return "LOUISVILLE";
        if (t.Contains("LEXINGTON")) return "LEX";
        if (t.Contains("NORTHERN INTERSTATE")) return "NORTHERN INTERSTATE";
        if (t.Contains("NORTHEAST")) return "NORTHEAST";
        if (t.Contains("CENTRAL")) return "CENTRAL OH";
        if (t.Contains("I-75")) return "I-75";
        if (t.Contains("I-65")) return "I-65";
        if (t.Contains("MID-TN") || t.Contains("MID TN")) return "MID-TN";
        return null;
    }

    private static CustomerHit? MatchCustomer(string brochureName, IReadOnlyList<CustomerHit> customers)
    {
        var cleaned = CleanBrochureName(brochureName);
        var key = Normalize(cleaned);

        foreach (var alias in NameAliases.OrderByDescending(a => a.Key.Length))
        {
            if (cleaned.StartsWith(alias.Key, StringComparison.OrdinalIgnoreCase)
                || Normalize(cleaned).StartsWith(Normalize(alias.Key), StringComparison.OrdinalIgnoreCase))
            {
                var aliased = customers
                    .Where(c =>
                    {
                        var name = Normalize(c.CustomerName);
                        var aliasKey = Normalize(alias.Value);
                        return name.StartsWith(aliasKey, StringComparison.OrdinalIgnoreCase)
                            || name.Contains(aliasKey, StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderBy(c => c.Type == CustomerType.Customer ? 0 : 1)
                    .ThenByDescending(c => c.Id)
                    .FirstOrDefault();
                if (aliased != null)
                    return aliased;
            }
        }

        var exact = customers
            .Where(c => Normalize(c.CustomerName) == key)
            .OrderBy(c => c.Type == CustomerType.Customer ? 0 : 1)
            .FirstOrDefault();
        if (exact != null)
            return exact;

        return customers
            .Select(c => new { Row = c, Name = Normalize(c.CustomerName) })
            .Where(x => x.Name.Length >= 8 && (key.Contains(x.Name) || x.Name.Contains(key)))
            .OrderBy(x => x.Row.Type == CustomerType.Customer ? 0 : 1)
            .ThenBy(x => x.Name.Length)
            .Select(x => x.Row)
            .FirstOrDefault();
    }

    private static string CleanBrochureName(string name)
    {
        var cleaned = Regex.Replace(name ?? string.Empty, @"\*\*[^*]+\*\*", " ");
        cleaned = Regex.Replace(cleaned, @"\*{1,3}[^*]*\*{1,3}", " ");
        cleaned = cleaned.Replace("*", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(" -/".ToCharArray());
        return cleaned;
    }

    private static string Normalize(string name)
    {
        var chars = name.ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray();
        return Regex.Replace(new string(chars), @"\s+", " ").Trim();
    }

    private static string? CleanNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;
        var value = Regex.Replace(notes.Trim(), @"\s+", " ").Trim(" ,;•-".ToCharArray());
        value = value.Replace("DBI", "DBL").Replace("PKI", "PKT");
        if (Regex.IsMatch(value, @"^\d{1,2}\s*-\s*\d{1,2}(?:'\d{2,4})?$"))
            return null;
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.StartsWith("Page", StringComparison.OrdinalIgnoreCase))
            return null;
        return Truncate(value, 200);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd();

    private sealed record CustomerHit(int Id, string CustomerName, CustomerType Type);
    private sealed record RouteHit(int Id, string RouteName);
    private sealed record StopHit(int Id, int RouteId, string StopName);

    private sealed class DriverNotesSheetRow
    {
        public string? Sheet { get; set; }
        public string? Title { get; set; }
        public string? BrochureName { get; set; }
        public string? Notes { get; set; }
    }
}

public class DriverNotesSheetImportResult
{
    public int SourceRows { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public List<string> UpdatedNames { get; } = new();
    public List<string> UnmatchedCustomers { get; } = new();
    public List<string> UnmatchedRoutes { get; } = new();
    public List<string> NoContract { get; } = new();
    public List<string> Skipped { get; } = new();
}
