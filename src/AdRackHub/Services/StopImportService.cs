using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class StopImportService
{
    private readonly ApplicationDbContext _context;

    public StopImportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StopImportResult> ImportAsync(int routeId, Stream csvStream, bool replaceExisting, CancellationToken cancellationToken = default)
    {
        var route = await _context.Routes.FindAsync([routeId], cancellationToken);
        if (route == null)
            throw new InvalidOperationException("Route not found.");

        using var reader = new StreamReader(csvStream);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException("CSV file is empty.");

        var headers = ParseCsvLine(headerLine);
        var columnMap = MapColumns(headers);
        if (!columnMap.ContainsKey("BusinessName"))
            throw new InvalidOperationException("CSV must include a Business Name column.");

        var parsedRows = new List<ParsedStopRow>();
        var lineNumber = 1;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseCsvLine(line);
            var row = ParseRow(values, columnMap, lineNumber);
            if (row != null)
                parsedRows.Add(row);
        }

        if (replaceExisting)
        {
            var existingStops = await _context.Stops.Where(s => s.RouteId == routeId).ToListAsync(cancellationToken);
            var stopIds = existingStops.Select(s => s.Id).ToList();
            var routeStops = await _context.CustomerRouteStops
                .Where(crs => stopIds.Contains(crs.StopId))
                .ToListAsync(cancellationToken);
            _context.CustomerRouteStops.RemoveRange(routeStops);
            _context.Stops.RemoveRange(existingStops);
        }

        foreach (var row in parsedRows)
        {
            _context.Stops.Add(new Stop
            {
                RouteId = routeId,
                StepNumber = row.StepNumber,
                StopName = row.BusinessName,
                StopType = InferStopType(row.BusinessName),
                RackPlacement = row.RackPlacement,
                Address = row.Address,
                City = row.City,
                State = row.State,
                Zip = row.Zip,
                HighwayExit = row.HighwayExit,
                Notes = row.Notes,
                Status = StopStatus.Active
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new StopImportResult
        {
            Imported = parsedRows.Count
        };
    }

    private static ParsedStopRow? ParseRow(IReadOnlyList<string> values, Dictionary<string, int> columnMap, int lineNumber)
    {
        var businessName = GetValue(values, columnMap, "BusinessName");
        if (string.IsNullOrWhiteSpace(businessName))
            return null;

        var statusText = GetValue(values, columnMap, "Status");
        if (statusText?.ToLowerInvariant() is "removed" or "possible")
            return null;

        int? stepNumber = null;
        var stepText = GetValue(values, columnMap, "StepNumber");
        if (!string.IsNullOrWhiteSpace(stepText) && int.TryParse(stepText, out var step))
            stepNumber = step;

        var notes = GetValue(values, columnMap, "Notes");
        var direction = GetValue(values, columnMap, "Direction");
        if (!string.IsNullOrWhiteSpace(direction))
            notes = string.IsNullOrWhiteSpace(notes) ? direction : $"{notes}; {direction}";

        var rackPlacement = GetValue(values, columnMap, "RackPlacement");

        return new ParsedStopRow
        {
            StepNumber = stepNumber,
            RackPlacement = rackPlacement,
            BusinessName = businessName.Trim(),
            Address = NullIfEmpty(GetValue(values, columnMap, "Address")),
            City = NullIfEmpty(GetValue(values, columnMap, "City")),
            State = NullIfEmpty(GetValue(values, columnMap, "State")),
            Zip = NullIfEmpty(GetValue(values, columnMap, "Zip")),
            HighwayExit = NullIfEmpty(GetValue(values, columnMap, "HighwayExit")),
            Notes = NullIfEmpty(notes)
        };
    }

    private static Dictionary<string, int> MapColumns(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim();
            var key = header.ToLowerInvariant() switch
            {
                "status" => "Status",
                "step #" or "step#" or "step" or "stop #" or "stop#" => "StepNumber",
                "rack placement/note" or "rack placement" or "rack placement note" or "placement" => "RackPlacement",
                "business name" or "business" or "name" => "BusinessName",
                "address" => "Address",
                "city" => "City",
                "state" => "State",
                "zip" or "zip code" => "Zip",
                "highway/exit" or "highway / exit" or "highway exit" or "highway" => "HighwayExit",
                "direction" => "Direction",
                "notes" or "note" => "Notes",
                _ => null
            };

            if (key != null && !map.ContainsKey(key))
                map[key] = i;
        }

        return map;
    }

    private static string? GetValue(IReadOnlyList<string> values, Dictionary<string, int> columnMap, string key)
    {
        if (!columnMap.TryGetValue(key, out var index) || index >= values.Count)
            return null;
        return values[index].Trim();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StopType InferStopType(string businessName)
    {
        var name = businessName.ToUpperInvariant();
        if (name.Contains("REST AREA") || name.Contains("REST STOP"))
            return StopType.RestArea;
        if (name.Contains("OUTLET") || name.Contains("MUSEUM") || name.Contains("CENTER") || name.Contains("GARDEN"))
            return StopType.Attraction;
        if (name.Contains("INN") || name.Contains("HOTEL") || name.Contains("MOTEL") || name.Contains("SUITES") || name.Contains("LODGE"))
            return StopType.Hotel;
        return StopType.Other;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = "";
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(current);
                current = "";
                continue;
            }

            current += ch;
        }

        values.Add(current);
        return values;
    }

    private sealed class ParsedStopRow
    {
        public int? StepNumber { get; init; }
        public string? RackPlacement { get; init; }
        public string BusinessName { get; init; } = string.Empty;
        public string? Address { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? Zip { get; init; }
        public string? HighwayExit { get; init; }
        public string? Notes { get; init; }
    }
}

public class StopImportResult
{
    public int Imported { get; set; }
}
