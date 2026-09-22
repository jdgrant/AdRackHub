using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class HighValueProspectProximityService
{
    public const double DefaultRadiusMiles = 5.0;

    private readonly ApplicationDbContext _context;

    public HighValueProspectProximityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<HighValueProximityHit?> GetNearestHitAsync(
        Customer prospect,
        double radiusMiles = DefaultRadiusMiles,
        CancellationToken cancellationToken = default)
    {
        if (!CustomerTypeLabels.IsProspectLike(prospect.Type)
            || !prospect.Latitude.HasValue
            || !prospect.Longitude.HasValue)
            return null;

        var anchors = await LoadAnchorsAsync(cancellationToken);
        return FindNearestWithin(prospect.Latitude.Value, prospect.Longitude.Value, anchors, radiusMiles);
    }

    public async Task<HighValueProximityResult> ApplyAsync(
        double radiusMiles = DefaultRadiusMiles,
        bool dryRun = false,
        bool replace = false,
        CancellationToken cancellationToken = default)
    {
        var result = new HighValueProximityResult { RadiusMiles = radiusMiles };
        var anchors = await LoadAnchorsAsync(cancellationToken);
        result.CustomerAnchorCount = anchors.Count(a => a.Kind == HighValueProximityKind.Customer);
        result.ExitStopAnchorCount = anchors.Count(a => a.Kind == HighValueProximityKind.ExitStop);

        var prospects = await _context.Customers
            .Where(c => c.Type == CustomerType.Prospect && c.Status == CustomerStatus.Active)
            .OrderBy(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        result.ProspectsConsidered = prospects.Count;

        foreach (var prospect in prospects)
        {
            if (!prospect.Latitude.HasValue || !prospect.Longitude.HasValue)
            {
                result.SkippedNoCoords++;
                continue;
            }

            var hit = FindNearestWithin(
                prospect.Latitude.Value,
                prospect.Longitude.Value,
                anchors,
                radiusMiles);

            if (hit != null)
            {
                result.Matches.Add(new HighValueProximityMatch
                {
                    ProspectId = prospect.Id,
                    ProspectName = prospect.CustomerName,
                    AlreadyHighValue = prospect.IsHighValueProspect,
                    Hit = hit
                });

                if (!prospect.IsHighValueProspect)
                {
                    result.Marked++;
                    if (!dryRun)
                        prospect.IsHighValueProspect = true;
                }
            }
            else if (replace && prospect.IsHighValueProspect)
            {
                result.Unmarked++;
                result.Cleared.Add($"#{prospect.Id} {prospect.CustomerName}");
                if (!dryRun)
                    prospect.IsHighValueProspect = false;
            }
        }

        if (!dryRun && (result.Marked > 0 || result.Unmarked > 0))
            await _context.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<List<ProximityAnchor>> LoadAnchorsAsync(CancellationToken cancellationToken)
    {
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c =>
                c.Type == CustomerType.Customer
                && c.Latitude != null
                && c.Longitude != null)
            .Select(c => new ProximityAnchor(
                HighValueProximityKind.Customer,
                c.Id,
                c.CustomerName,
                null,
                c.Latitude!.Value,
                c.Longitude!.Value))
            .ToListAsync(cancellationToken);

        var exitStops = await _context.Stops
            .AsNoTracking()
            .Where(s =>
                s.Status == StopStatus.Active
                && s.StopType != StopType.ProspectStop
                && s.Latitude != null
                && s.Longitude != null
                && s.Route != null
                && s.Route.Status == RouteStatus.Active
                && !s.Route.RouteName.Contains("Rest Area"))
            .Select(s => new ProximityAnchor(
                HighValueProximityKind.ExitStop,
                s.Id,
                s.StopName,
                s.Route!.RouteName,
                s.Latitude!.Value,
                s.Longitude!.Value))
            .ToListAsync(cancellationToken);

        customers.AddRange(exitStops);
        return customers;
    }

    private static HighValueProximityHit? FindNearestWithin(
        double lat,
        double lng,
        IReadOnlyList<ProximityAnchor> anchors,
        double radiusMiles)
    {
        HighValueProximityHit? best = null;
        foreach (var anchor in anchors)
        {
            var miles = GeoMath.HaversineMiles(lat, lng, anchor.Latitude, anchor.Longitude);
            if (miles > radiusMiles)
                continue;
            if (best != null && miles >= best.Miles)
                continue;

            best = new HighValueProximityHit
            {
                Kind = anchor.Kind,
                Id = anchor.Id,
                Name = anchor.Name,
                RouteName = anchor.RouteName,
                Miles = miles
            };
        }

        return best;
    }

    private sealed record ProximityAnchor(
        HighValueProximityKind Kind,
        int Id,
        string Name,
        string? RouteName,
        double Latitude,
        double Longitude);
}

public enum HighValueProximityKind
{
    Customer,
    ExitStop
}

public class HighValueProximityHit
{
    public HighValueProximityKind Kind { get; init; }
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? RouteName { get; init; }
    public double Miles { get; init; }

    public string KindLabel => Kind == HighValueProximityKind.Customer ? "customer" : "exit stop";

    public string Summary =>
        Kind == HighValueProximityKind.Customer
            ? $"{Miles:0.0} mi from {Name} (customer)"
            : $"{Miles:0.0} mi from {Name} ({RouteName ?? "exit stop"})";
}

public class HighValueProximityMatch
{
    public int ProspectId { get; init; }
    public string ProspectName { get; init; } = string.Empty;
    public bool AlreadyHighValue { get; init; }
    public HighValueProximityHit Hit { get; init; } = new();
}

public class HighValueProximityResult
{
    public double RadiusMiles { get; init; }
    public int CustomerAnchorCount { get; set; }
    public int ExitStopAnchorCount { get; set; }
    public int ProspectsConsidered { get; set; }
    public int SkippedNoCoords { get; set; }
    public int Marked { get; set; }
    public int Unmarked { get; set; }
    public List<HighValueProximityMatch> Matches { get; } = new();
    public List<string> Cleared { get; } = new();
}
