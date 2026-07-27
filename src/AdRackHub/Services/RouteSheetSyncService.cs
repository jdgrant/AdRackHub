using System.Text;
using AdRackHub.Data;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class RouteSheetSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly GoogleSheetsService _googleSheetsService;
    private readonly StopImportService _stopImportService;

    public RouteSheetSyncService(
        ApplicationDbContext context,
        GoogleSheetsService googleSheetsService,
        StopImportService stopImportService)
    {
        _context = context;
        _googleSheetsService = googleSheetsService;
        _stopImportService = stopImportService;
    }

    public bool IsConfigured => _googleSheetsService.IsConfigured;

    public async Task<RouteSheetSyncResult> SyncRouteAsync(
        string routeSlug,
        bool replaceExisting = true,
        CancellationToken cancellationToken = default)
    {
        var routeName = RouteSheetMap.GetRouteName(routeSlug);
        var route = await _context.Routes.FirstOrDefaultAsync(r => r.RouteName == routeName, cancellationToken)
            ?? throw new InvalidOperationException($"Route '{routeName}' not found in the database.");

        var sheetName = RouteSheetMap.GetSheetName(routeSlug);
        var rows = await _googleSheetsService.GetSheetValuesAsync(sheetName, cancellationToken);
        if (rows.Count == 0)
            throw new InvalidOperationException($"Sheet '{sheetName}' is empty.");

        await using var csvStream = BuildCsvStream(rows);
        var importResult = await _stopImportService.ImportAsync(route.Id, csvStream, replaceExisting, cancellationToken);

        return new RouteSheetSyncResult
        {
            RouteSlug = routeSlug,
            RouteName = routeName,
            SheetName = sheetName,
            Imported = importResult.Imported
        };
    }

    public async Task<IReadOnlyList<RouteSheetSyncResult>> SyncAllRoutesAsync(
        bool replaceExisting = true,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RouteSheetSyncResult>();
        foreach (var slug in RouteSheetMap.SlugToSheetName.Keys)
        {
            results.Add(await SyncRouteAsync(slug, replaceExisting, cancellationToken));
        }

        return results;
    }

    private static MemoryStream BuildCsvStream(IList<IList<object>> rows)
    {
        var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        foreach (var row in rows)
        {
            if (row.Count == 0 || row.All(cell => string.IsNullOrWhiteSpace(cell?.ToString())))
                continue;

            writer.WriteLine(string.Join(",", row.Select(FormatCsvCell)));
        }

        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    private static string FormatCsvCell(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
            return $"\"{text.Replace("\"", "\"\"")}\"";
        return text;
    }
}

public class RouteSheetSyncResult
{
    public string RouteSlug { get; init; } = string.Empty;
    public string RouteName { get; init; } = string.Empty;
    public string SheetName { get; init; } = string.Empty;
    public int Imported { get; init; }
}
