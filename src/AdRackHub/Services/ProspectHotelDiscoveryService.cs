using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class ProspectHotelDiscoveryService
{
    public const double MaxDistanceMiles = 1.0;

    private readonly ApplicationDbContext _context;
    private readonly DataForSeoClient _dataForSeo;
    private readonly ILogger<ProspectHotelDiscoveryService> _logger;

    public ProspectHotelDiscoveryService(
        ApplicationDbContext context,
        DataForSeoClient dataForSeo,
        ILogger<ProspectHotelDiscoveryService> logger)
    {
        _context = context;
        _dataForSeo = dataForSeo;
        _logger = logger;
    }

    public bool IsConfigured => _dataForSeo.IsConfigured;

    public async Task<ProspectHotelDiscoveryStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var stops = await _context.Stops.AsNoTracking().ToListAsync(cancellationToken);
        return new ProspectHotelDiscoveryStats
        {
            TotalStops = stops.Count,
            ActiveSeedCandidates = stops.Count(s =>
                s.Status == StopStatus.Active
                && s.StopType != StopType.ProspectStop
                && StopAddressHelper.HasMappableLocation(s)),
            ProspectStops = stops.Count(s => s.StopType == StopType.ProspectStop),
            StopsWithCoordinates = stops.Count(s => s.Latitude.HasValue && s.Longitude.HasValue),
            IsConfigured = IsConfigured
        };
    }

    public async Task<ProspectHotelDiscoveryResult> DiscoverAsync(
        bool dryRun,
        int? maxSeeds = null,
        int? skipSeeds = null,
        int? routeId = null,
        string? cityContains = null,
        IReadOnlyCollection<int>? routeIds = null,
        IReadOnlyCollection<string>? states = null,
        CancellationToken cancellationToken = default,
        IProgress<ProspectHotelProgress>? progress = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("DataForSEO credentials are not configured.");

        var result = new ProspectHotelDiscoveryResult { DryRun = dryRun };
        var allStops = await _context.Stops.ToListAsync(cancellationToken);

        var knownPlaceIds = new HashSet<string>(
            allStops.Where(s => !string.IsNullOrWhiteSpace(s.PlaceId)).Select(s => s.PlaceId!),
            StringComparer.OrdinalIgnoreCase);

        var knownKeys = new HashSet<string>(
            allStops.Select(NormalizeKey),
            StringComparer.OrdinalIgnoreCase);

        var existingNames = allStops
            .Where(s => s.StopType != StopType.ProspectStop)
            .Select(s => s.StopName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var cityFilter = string.IsNullOrWhiteSpace(cityContains) ? null : cityContains.Trim();
        var routeIdSet = routeIds is { Count: > 0 }
            ? routeIds.ToHashSet()
            : routeId.HasValue ? new HashSet<int> { routeId.Value } : null;
        var stateSet = states is { Count: > 0 }
            ? new HashSet<string>(
                states.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeStateCode),
                StringComparer.OrdinalIgnoreCase)
            : null;

        // Prefer stops that still need geocoding so each batch advances coverage.
        var seeds = allStops
            .Where(s =>
                s.Status == StopStatus.Active
                && s.StopType != StopType.ProspectStop
                && StopAddressHelper.HasMappableLocation(s)
                && (routeIdSet == null || routeIdSet.Contains(s.RouteId))
                && (stateSet == null || StateMatches(s.State, stateSet))
                && (cityFilter == null
                    || (!string.IsNullOrWhiteSpace(s.City)
                        && s.City.Contains(cityFilter, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(s => s.Latitude.HasValue && s.Longitude.HasValue ? 1 : 0)
            .ThenBy(s => s.RouteId)
            .ThenBy(s => s.StepNumber)
            .ThenBy(s => s.StopName)
            .ToList();

        var skip = Math.Max(0, skipSeeds ?? 0);
        if (skip > 0)
            seeds = seeds.Skip(skip).ToList();

        if (maxSeeds is > 0)
            seeds = seeds.Take(maxSeeds.Value).ToList();

        result.SeedsSelected = seeds.Count;
        var pendingProspects = new List<Stop>();
        var index = 0;

        foreach (var seed in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new ProspectHotelProgress
            {
                SeedsSelected = result.SeedsSelected,
                SeedsScanned = result.SeedsScanned,
                SeedsSkippedNoCoords = result.SeedsSkippedNoCoords,
                SeedsFailed = result.SeedsFailed,
                CandidatesWithinMile = result.CandidatesWithinMile,
                SkippedDuplicates = result.SkippedDuplicates,
                CurrentIndex = index,
                CurrentStopName = seed.StopName,
                Phase = seed.Latitude.HasValue && seed.Longitude.HasValue
                    ? "Searching hotels"
                    : "Geocoding stop"
            });

            try
            {
                if (!await EnsureCoordinatesAsync(seed, cancellationToken))
                {
                    result.SeedsSkippedNoCoords++;
                    result.Messages.Add($"Skipped (no coordinates): {seed.StopName}");
                    continue;
                }

                progress?.Report(new ProspectHotelProgress
                {
                    SeedsSelected = result.SeedsSelected,
                    SeedsScanned = result.SeedsScanned,
                    SeedsSkippedNoCoords = result.SeedsSkippedNoCoords,
                    SeedsFailed = result.SeedsFailed,
                    CandidatesWithinMile = result.CandidatesWithinMile,
                    SkippedDuplicates = result.SkippedDuplicates,
                    CurrentIndex = index,
                    CurrentStopName = seed.StopName,
                    Phase = "Searching hotels"
                });

                result.SeedsScanned++;
                var listings = await _dataForSeo.SearchHotelsNearAsync(
                    seed.Latitude!.Value,
                    seed.Longitude!.Value,
                    radiusKm: 2,
                    limit: 50,
                    cancellationToken);

                await Task.Delay(250, cancellationToken);

                foreach (var listing in listings)
                {
                    if (!listing.Latitude.HasValue || !listing.Longitude.HasValue || string.IsNullOrWhiteSpace(listing.Title))
                        continue;

                    var miles = HaversineMiles(
                        seed.Latitude.Value,
                        seed.Longitude.Value,
                        listing.Latitude.Value,
                        listing.Longitude.Value);

                    if (miles >= MaxDistanceMiles)
                    {
                        result.CandidatesTooFar++;
                        continue;
                    }

                    result.CandidatesWithinMile++;

                    if (!string.IsNullOrWhiteSpace(listing.PlaceId) && knownPlaceIds.Contains(listing.PlaceId))
                    {
                        result.SkippedDuplicates++;
                        continue;
                    }

                    var street = listing.AddressInfo?.Address ?? listing.Address;
                    var city = listing.AddressInfo?.City;
                    var state = NormalizeState(listing.AddressInfo?.Region);
                    var zip = listing.AddressInfo?.Zip;
                    var key = NormalizeKey(listing.Title, street, city, state);

                    if (knownKeys.Contains(key))
                    {
                        result.SkippedDuplicates++;
                        continue;
                    }

                    if (NamesMatch(seed.StopName, listing.Title) && miles < 0.15)
                    {
                        result.SkippedDuplicates++;
                        continue;
                    }

                    if (existingNames.Any(n => NamesMatch(n, listing.Title)))
                    {
                        result.SkippedDuplicates++;
                        continue;
                    }

                    var prospect = new Stop
                    {
                        RouteId = seed.RouteId,
                        StopName = listing.Title.Trim(),
                        StopType = StopType.ProspectStop,
                        Status = StopStatus.Active,
                        Address = Truncate(street, 300),
                        City = Truncate(city, 100),
                        State = Truncate(state, 50),
                        Zip = Truncate(zip, 20),
                        Latitude = listing.Latitude,
                        Longitude = listing.Longitude,
                        PlaceId = Truncate(listing.PlaceId, 255),
                        Notes = Truncate(
                            $"Prospect near {seed.StopName} ({miles:0.00} mi)"
                            + (string.IsNullOrWhiteSpace(listing.Category) ? "" : $"; {listing.Category}"),
                            1000)
                    };

                    knownKeys.Add(key);
                    if (!string.IsNullOrWhiteSpace(prospect.PlaceId))
                        knownPlaceIds.Add(prospect.PlaceId);
                    existingNames.Add(prospect.StopName);

                    pendingProspects.Add(prospect);
                    result.Candidates.Add(new ProspectHotelCandidate
                    {
                        StopName = prospect.StopName,
                        RouteId = prospect.RouteId,
                        Address = StopAddressHelper.FormatFullAddress(prospect),
                        DistanceMiles = miles,
                        NearStopName = seed.StopName,
                        PlaceId = prospect.PlaceId,
                        Latitude = prospect.Latitude,
                        Longitude = prospect.Longitude
                    });
                }

                // Persist in small batches so a long run can be interrupted safely.
                if (!dryRun && pendingProspects.Count >= 10)
                {
                    _context.Stops.AddRange(pendingProspects);
                    await _context.SaveChangesAsync(cancellationToken);
                    result.Added += pendingProspects.Count;
                    pendingProspects.Clear();
                }
            }
            catch (Exception ex)
            {
                result.SeedsFailed++;
                result.Messages.Add($"Error near {seed.StopName}: {ex.Message}");
                _logger.LogWarning(ex, "Prospect hotel discovery failed for stop {StopId}", seed.Id);
            }
        }

        if (!dryRun && pendingProspects.Count > 0)
        {
            _context.Stops.AddRange(pendingProspects);
            await _context.SaveChangesAsync(cancellationToken);
            result.Added += pendingProspects.Count;
            pendingProspects.Clear();
        }
        else if (dryRun)
        {
            result.WouldAdd = result.Candidates.Count;
        }

        progress?.Report(new ProspectHotelProgress
        {
            SeedsSelected = result.SeedsSelected,
            SeedsScanned = result.SeedsScanned,
            SeedsSkippedNoCoords = result.SeedsSkippedNoCoords,
            SeedsFailed = result.SeedsFailed,
            CandidatesWithinMile = result.CandidatesWithinMile,
            SkippedDuplicates = result.SkippedDuplicates,
            CurrentIndex = result.SeedsSelected,
            CurrentStopName = null,
            Phase = "Done"
        });

        return result;
    }

    private async Task<bool> EnsureCoordinatesAsync(Stop stop, CancellationToken cancellationToken)
    {
        if (stop.Latitude.HasValue && stop.Longitude.HasValue)
            return true;

        var query = StopAddressHelper.GetGeocodingQuery(stop);
        if (string.IsNullOrWhiteSpace(query) || query == "—")
            return false;

        try
        {
            var coords = await _dataForSeo.GeocodeAsync(query, cancellationToken);
            await Task.Delay(250, cancellationToken);
            if (coords == null)
                return false;

            stop.Latitude = coords.Value.Latitude;
            stop.Longitude = coords.Value.Longitude;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Geocode failed for stop {StopId} ({Query})", stop.Id, query);
            return false;
        }
    }

    public static double HaversineMiles(double lat1, double lon1, double lat2, double lon2) =>
        GeoMath.HaversineMiles(lat1, lon1, lat2, lon2);

    private static string NormalizeKey(Stop stop) =>
        NormalizeKey(stop.StopName, stop.Address, stop.City, stop.State);

    private static string NormalizeKey(string? name, string? address, string? city, string? state)
    {
        static string Clean(string? value) =>
            new string((value ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        return $"{Clean(name)}|{Clean(address)}|{Clean(city)}|{Clean(state)}";
    }

    private static bool NamesMatch(string? left, string? right)
    {
        static string Clean(string? value) =>
            new string((value ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        var a = Clean(left);
        var b = Clean(right);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        return a == b || a.Contains(b) || b.Contains(a);
    }

    private static string NormalizeStateCode(string state)
    {
        var trimmed = state.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "KENTUCKY" => "KY",
            "OHIO" => "OH",
            "TENNESSEE" => "TN",
            "INDIANA" => "IN",
            _ => trimmed.Length == 2 ? trimmed.ToUpperInvariant() : trimmed
        };
    }

    private static bool StateMatches(string? stopState, HashSet<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(stopState))
            return false;
        var normalized = NormalizeStateCode(stopState);
        return allowed.Contains(normalized) || allowed.Contains(stopState.Trim());
    }

    private static string? NormalizeState(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            return null;
        var trimmed = region.Trim();
        // DataForSEO sometimes returns "Kentucky" — keep as-is up to 50 chars.
        return trimmed.Length <= 50 ? trimmed : trimmed[..50];
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

public class ProspectHotelDiscoveryStats
{
    public int TotalStops { get; set; }
    public int ActiveSeedCandidates { get; set; }
    public int ProspectStops { get; set; }
    public int StopsWithCoordinates { get; set; }
    public bool IsConfigured { get; set; }
}

public class ProspectHotelDiscoveryResult
{
    public bool DryRun { get; set; }
    public int SeedsSelected { get; set; }
    public int SeedsScanned { get; set; }
    public int SeedsSkippedNoCoords { get; set; }
    public int SeedsFailed { get; set; }
    public int CandidatesWithinMile { get; set; }
    public int CandidatesTooFar { get; set; }
    public int SkippedDuplicates { get; set; }
    public int Added { get; set; }
    public int WouldAdd { get; set; }
    public List<ProspectHotelCandidate> Candidates { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}

public class ProspectHotelCandidate
{
    public string StopName { get; set; } = string.Empty;
    public int RouteId { get; set; }
    public string Address { get; set; } = string.Empty;
    public double DistanceMiles { get; set; }
    public string NearStopName { get; set; } = string.Empty;
    public string? PlaceId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class ProspectHotelProgress
{
    public int SeedsSelected { get; set; }
    public int SeedsScanned { get; set; }
    public int SeedsSkippedNoCoords { get; set; }
    public int SeedsFailed { get; set; }
    public int CandidatesWithinMile { get; set; }
    public int SkippedDuplicates { get; set; }
    public int CurrentIndex { get; set; }
    public string? CurrentStopName { get; set; }
    public string Phase { get; set; } = string.Empty;
}
