using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.RoutesStops)]
public class GoogleSheetsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly GoogleSheetsService _googleSheetsService;
    private readonly RouteSheetSyncService _routeSheetSyncService;

    public GoogleSheetsController(
        ApplicationDbContext context,
        GoogleSheetsService googleSheetsService,
        RouteSheetSyncService routeSheetSyncService)
    {
        _context = context;
        _googleSheetsService = googleSheetsService;
        _routeSheetSyncService = routeSheetSyncService;
    }

    public async Task<IActionResult> Index()
    {
        var connection = _googleSheetsService.IsConfigured
            ? await _googleSheetsService.GetConnectionInfoAsync()
            : new GoogleSheetsConnectionInfo { IsConfigured = false };

        var routes = await _context.Routes
            .Include(r => r.Stops)
            .OrderBy(r => r.RouteName)
            .ToListAsync();

        var model = new GoogleSheetsIndexViewModel
        {
            Connection = connection,
            Routes = routes.Select(dbRoute =>
            {
                var slug = RouteSheetMap.GetSlugForRouteName(dbRoute.RouteName);
                var sheetName = slug != null ? RouteSheetMap.GetSheetName(slug) : null;
                return new GoogleSheetsRouteRow
                {
                    RouteId = dbRoute.Id,
                    RouteName = dbRoute.RouteName,
                    Slug = slug,
                    SheetName = sheetName,
                    StopCount = dbRoute.Stops.Count(s => s.Status == StopStatus.Active),
                    SheetFound = sheetName != null && connection.SheetNames.Any(name =>
                        name.Equals(sheetName, StringComparison.OrdinalIgnoreCase))
                };
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncAll()
    {
        try
        {
            var results = await _routeSheetSyncService.SyncAllRoutesAsync();
            var imported = results.Sum(r => r.Imported);
            TempData["Message"] = $"Synced {results.Count} route sheets ({imported} stops imported).";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncRoute(string slug)
    {
        try
        {
            var result = await _routeSheetSyncService.SyncRouteAsync(slug);
            TempData["Message"] = $"Synced {result.RouteName} from Google Sheets ({result.Imported} stops).";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
