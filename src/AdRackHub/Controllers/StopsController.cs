using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.RoutesStops)]
public class StopsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly StopVisitService _visitService;

    public StopsController(ApplicationDbContext context, StopVisitService visitService)
    {
        _context = context;
        _visitService = visitService;
    }

    public async Task<IActionResult> Index(int? routeId, StopStatus? status, StopType? stopType)
    {
        var query = _context.Stops.Include(s => s.Route).AsQueryable();

        if (routeId.HasValue)
            query = query.Where(s => s.RouteId == routeId.Value);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        if (stopType.HasValue)
            query = query.Where(s => s.StopType == stopType.Value);

        ViewBag.RouteId = new SelectList(await _context.Routes.OrderBy(r => r.RouteName).ToListAsync(), "Id", "RouteName", routeId);
        ViewBag.Status = status;
        ViewBag.StopType = stopType;

        return View(await query.OrderBy(s => s.Route.RouteName).ThenBy(s => s.StopName).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var stop = await _context.Stops
            .Include(s => s.Route)
            .Include(s => s.CustomerRouteStops)
                .ThenInclude(crs => crs.CustomerRoute)
                    .ThenInclude(cr => cr.Customer)
            .Include(s => s.Visits.OrderByDescending(v => v.VisitedAt).Take(25))
            .FirstOrDefaultAsync(s => s.Id == id);

        if (stop == null) return NotFound();
        return View(stop);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogVisit(int id, string? notes)
    {
        var stop = await _context.Stops.FindAsync(id);
        if (stop == null) return NotFound();

        await _visitService.LogVisitAsync(stop, User, notes);
        TempData["Message"] = $"Visit logged for {stop.StopName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Create(int? routeId)
    {
        await PopulateRoutesAsync(routeId);
        return View(new Stop
        {
            RouteId = routeId ?? 0,
            Status = StopStatus.Active,
            StopType = StopType.Hotel
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Stop stop)
    {
        ModelState.Remove(nameof(Stop.Route));
        ModelState.Remove(nameof(Stop.CustomerRouteStops));
        ModelState.Remove(nameof(Stop.Visits));

        if (ModelState.IsValid)
        {
            _context.Add(stop);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = stop.Id });
        }
        await PopulateRoutesAsync(stop.RouteId);
        return View(stop);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var stop = await _context.Stops.FindAsync(id);
        if (stop == null) return NotFound();
        await PopulateRoutesAsync(stop.RouteId);
        return View(stop);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Stop stop)
    {
        if (id != stop.Id) return NotFound();

        ModelState.Remove(nameof(Stop.Route));
        ModelState.Remove(nameof(Stop.CustomerRouteStops));
        ModelState.Remove(nameof(Stop.Visits));

        if (ModelState.IsValid)
        {
            var existing = await _context.Stops.FindAsync(id);
            if (existing == null) return NotFound();

            existing.RouteId = stop.RouteId;
            existing.StepNumber = stop.StepNumber;
            existing.StopName = stop.StopName;
            existing.StopType = stop.StopType;
            existing.RackPlacement = stop.RackPlacement;
            existing.Address = stop.Address;
            existing.City = stop.City;
            existing.State = stop.State;
            existing.Zip = stop.Zip;
            existing.HighwayExit = stop.HighwayExit;
            existing.Notes = stop.Notes;
            existing.Latitude = stop.Latitude;
            existing.Longitude = stop.Longitude;
            existing.PlaceId = stop.PlaceId;
            existing.Status = stop.Status;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }
        await PopulateRoutesAsync(stop.RouteId);
        return View(stop);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var stop = await _context.Stops.Include(s => s.Route).FirstOrDefaultAsync(s => s.Id == id);
        if (stop == null) return NotFound();
        return View(stop);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var stop = await _context.Stops.FindAsync(id);
        if (stop != null)
        {
            _context.Stops.Remove(stop);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateRoutesAsync(int? selectedId = null)
    {
        ViewBag.RouteId = new SelectList(await _context.Routes.OrderBy(r => r.RouteName).ToListAsync(), "Id", "RouteName", selectedId);
    }
}
