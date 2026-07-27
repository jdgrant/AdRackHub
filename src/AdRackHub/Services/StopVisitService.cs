using System.Security.Claims;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class StopVisitService
{
    private readonly ApplicationDbContext _context;

    public StopVisitService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StopVisit> LogVisitAsync(Stop stop, ClaimsPrincipal user, string? notes = null, DateTime? visitedAt = null, CancellationToken cancellationToken = default)
    {
        var when = visitedAt ?? DateTime.UtcNow;
        var visit = new StopVisit
        {
            StopId = stop.Id,
            VisitedAt = when,
            VisitedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            VisitedByName = user.Identity?.Name,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        stop.LastVisitedAt = when;
        _context.StopVisits.Add(visit);
        await _context.SaveChangesAsync(cancellationToken);
        return visit;
    }

    public async Task<int> LogVisitsForStopsAsync(IEnumerable<Stop> stops, ClaimsPrincipal user, string? notes = null, CancellationToken cancellationToken = default)
    {
        var when = DateTime.UtcNow;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = user.Identity?.Name;
        var count = 0;

        foreach (var stop in stops)
        {
            _context.StopVisits.Add(new StopVisit
            {
                StopId = stop.Id,
                VisitedAt = when,
                VisitedByUserId = userId,
                VisitedByName = userName,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
            });
            stop.LastVisitedAt = when;
            count++;
        }

        if (count > 0)
            await _context.SaveChangesAsync(cancellationToken);

        return count;
    }
}
