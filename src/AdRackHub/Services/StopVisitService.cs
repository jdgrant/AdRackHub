using System.Security.Claims;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class StopVisitService
{
    private readonly ApplicationDbContext _context;
    private readonly VisitPhotoStore _photos;

    public StopVisitService(ApplicationDbContext context, VisitPhotoStore photos)
    {
        _context = context;
        _photos = photos;
    }

    public async Task<StopVisit> LogWithPhotosAsync(
        Stop stop,
        string? userId,
        string? userName,
        string? driverName,
        string? notes,
        DateTime visitedAt,
        StoredVisitImage before,
        StoredVisitImage after,
        CancellationToken cancellationToken = default)
    {
        var driver = (driverName ?? "").Trim();
        if (driver.Length == 0)
            throw new InvalidOperationException("Enter the driver who made the visit.");
        if (driver.Length > 200)
            throw new InvalidOperationException("Driver name must be 200 characters or fewer.");

        var note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (note is { Length: > 1000 })
            throw new InvalidOperationException("Notes must be 1000 characters or fewer.");

        var previousVisit = stop.LastVisitedAt;
        var visit = new StopVisit
        {
            StopId = stop.Id,
            VisitedAt = visitedAt,
            VisitedByUserId = userId,
            VisitedByName = userName,
            DriverName = driver,
            Notes = note
        };

        stop.LastVisitedAt = visitedAt;
        _context.StopVisits.Add(visit);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var directory = _photos.DirectoryFor(visit.Id);
            Directory.CreateDirectory(directory);
            await AddPhotoAsync(visit, directory, StopVisitPhotoKind.Before, before, cancellationToken);
            await AddPhotoAsync(visit, directory, StopVisitPhotoKind.After, after, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return visit;
        }
        catch
        {
            _photos.DeleteVisitFolder(visit.Id);
            _context.StopVisits.Remove(visit);
            stop.LastVisitedAt = previousVisit;
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static async Task AddPhotoAsync(
        StopVisit visit,
        string directory,
        StopVisitPhotoKind kind,
        StoredVisitImage image,
        CancellationToken cancellationToken)
    {
        var stored = Guid.NewGuid().ToString("N") + image.Extension;
        await File.WriteAllBytesAsync(Path.Combine(directory, stored), image.Bytes, cancellationToken);
        visit.Photos.Add(new StopVisitPhoto
        {
            Kind = kind,
            OriginalFileName = image.OriginalFileName,
            StoredFileName = stored,
            ContentType = image.ContentType,
            SizeBytes = image.Bytes.LongLength
        });
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
