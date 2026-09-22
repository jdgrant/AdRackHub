using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.RoutesStops)]
public class ProspectStopsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ProspectHotelDiscoveryService _discovery;
    private readonly ProspectHotelDiscoveryJobService _jobs;

    public ProspectStopsController(
        ApplicationDbContext context,
        ProspectHotelDiscoveryService discovery,
        ProspectHotelDiscoveryJobService jobs)
    {
        _context = context;
        _discovery = discovery;
        _jobs = jobs;
    }

    public async Task<IActionResult> Index(Guid? jobId, int? routeId, bool done = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Stops
            .Include(s => s.Route)
            .Where(s => s.StopType == StopType.ProspectStop)
            .AsQueryable();

        if (routeId.HasValue)
            query = query.Where(s => s.RouteId == routeId.Value);

        ProspectHotelDiscoveryResult? lastResult = null;
        Guid? activeJobId = null;
        if (jobId.HasValue)
        {
            var job = _jobs.Get(jobId.Value);
            if (job?.Status == ProspectHotelJobStatus.Completed)
            {
                lastResult = job.Result;
                // Completed jobs only show the summary — do not keep polling.
            }
            else if (job?.Status == ProspectHotelJobStatus.Failed)
            {
                TempData["Error"] ??= job.Error;
            }
            else if (job != null && !done)
            {
                activeJobId = jobId;
            }
            else if (job == null && !done)
            {
                // Job lost after restart — avoid endless "Starting…" polling.
                TempData["Error"] ??= "That scan is no longer available (server may have restarted). Start a new scan.";
            }
        }

        var model = new ProspectStopsPageViewModel
        {
            Stops = await query
                .OrderByDescending(s => s.IsHighValueTarget)
                .ThenBy(s => s.Route!.RouteName)
                .ThenBy(s => s.StopName)
                .ToListAsync(cancellationToken),
            Stats = await _discovery.GetStatsAsync(cancellationToken),
            LastResult = lastResult,
            ActiveJobId = activeJobId,
            DryRun = false,
            MaxSeeds = 25,
            SkipSeeds = 0,
            RouteId = routeId
        };

        ViewBag.RouteId = new SelectList(
            await _context.Routes.OrderBy(r => r.RouteName).ToListAsync(cancellationToken),
            "Id",
            "RouteName",
            routeId);
        ViewBag.SeedRouteId = new SelectList(
            await _context.Routes.OrderBy(r => r.RouteName).ToListAsync(cancellationToken),
            "Id",
            "RouteName");

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult FindNearby(
        bool dryRun = false,
        int? maxSeeds = null,
        int? skipSeeds = null,
        int? seedRouteId = null,
        string? cityContains = null)
    {
        if (!_discovery.IsConfigured)
        {
            TempData["Error"] = "DataForSEO credentials are not configured.";
            return RedirectToAction(nameof(Index));
        }

        maxSeeds ??= 25;
        if (maxSeeds < 1)
            maxSeeds = 25;
        skipSeeds = Math.Max(0, skipSeeds ?? 0);
        cityContains = string.IsNullOrWhiteSpace(cityContains) ? null : cityContains.Trim();

        var jobId = _jobs.Start(dryRun, maxSeeds, skipSeeds, seedRouteId, cityContains);
        var bits = new List<string>();
        if (seedRouteId.HasValue) bits.Add("selected route");
        if (cityContains != null) bits.Add($"city containing '{cityContains}'");
        var scope = bits.Count > 0 ? " (" + string.Join(", ", bits) + ")" : "";
        TempData["Message"] = dryRun
            ? $"Dry-run started for up to {maxSeeds} stop(s){scope} (skip {skipSeeds}). Progress updates below."
            : $"Finding hotels within 1 mile of up to {maxSeeds} stop(s){scope} (skip {skipSeeds}). Progress updates below.";
        return RedirectToAction(nameof(Index), new { jobId });
    }

    [HttpGet]
    public IActionResult JobStatus(Guid jobId)
    {
        var job = _jobs.Get(jobId);
        if (job == null)
            return NotFound(new { error = "Job not found." });

        return Json(new
        {
            id = job.Id,
            status = job.Status.ToString(),
            dryRun = job.DryRun,
            maxSeeds = job.MaxSeeds,
            error = job.Error,
            startedAt = job.StartedAt,
            completedAt = job.CompletedAt,
            progress = job.Progress,
            result = job.Status is ProspectHotelJobStatus.Completed or ProspectHotelJobStatus.Failed
                ? job.Result
                : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CancelJob(Guid jobId)
    {
        _jobs.Cancel(jobId);
        TempData["Message"] = "Cancel requested.";
        return RedirectToAction(nameof(Index), new { jobId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleHighValueTarget(int id, int? routeId, CancellationToken cancellationToken)
    {
        var stop = await _context.Stops
            .FirstOrDefaultAsync(s => s.Id == id && s.StopType == StopType.ProspectStop, cancellationToken);
        if (stop == null)
            return NotFound();

        stop.IsHighValueTarget = !stop.IsHighValueTarget;
        await _context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = stop.IsHighValueTarget
            ? $"{stop.StopName} marked as High Value Target."
            : $"{stop.StopName} is no longer a High Value Target.";

        return RedirectToAction(nameof(Index), new { routeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var stop = await _context.Stops
            .FirstOrDefaultAsync(s => s.Id == id && s.StopType == StopType.ProspectStop, cancellationToken);
        if (stop != null)
        {
            _context.Stops.Remove(stop);
            await _context.SaveChangesAsync(cancellationToken);
            TempData["Message"] = $"Removed prospect stop {stop.StopName}.";
        }

        return RedirectToAction(nameof(Index));
    }
}
