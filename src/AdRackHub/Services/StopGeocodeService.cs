using System.Globalization;
using System.Text.Json;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class StopGeocodeService
{
    private readonly ApplicationDbContext _context;
    private readonly DataForSeoClient _dataForSeo;
    private readonly HttpClient _http;
    private readonly ILogger<StopGeocodeService> _logger;

    public StopGeocodeService(
        ApplicationDbContext context,
        DataForSeoClient dataForSeo,
        IHttpClientFactory httpClientFactory,
        ILogger<StopGeocodeService> logger)
    {
        _context = context;
        _dataForSeo = dataForSeo;
        _http = httpClientFactory.CreateClient(nameof(StopGeocodeService));
        _logger = logger;
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(45))
            _http.Timeout = TimeSpan.FromSeconds(45);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AdRackHub/1.0");
    }

    public async Task<int> FixKnownBadMidTnRowsAsync(CancellationToken cancellationToken = default)
    {
        var fixedCount = 0;

        var mainstay = await _context.Stops.FirstOrDefaultAsync(s => s.Id == 513, cancellationToken);
        if (mainstay != null
            && string.Equals(mainstay.StopName, "hotel list too", StringComparison.OrdinalIgnoreCase))
        {
            mainstay.StepNumber = 2;
            mainstay.StopName = "MAINSTAY SUITES";
            mainstay.Address = "144 MERCHANTS DR";
            mainstay.City = "KNOXVILLE";
            mainstay.State = "TN";
            mainstay.HighwayExit = "#I-75 & MERCHANTS DR (EXIT 108)";
            fixedCount++;
        }

        var quality = await _context.Stops.FirstOrDefaultAsync(s => s.Id == 514, cancellationToken);
        if (quality != null
            && string.Equals(quality.StopName, "hotel list too", StringComparison.OrdinalIgnoreCase))
        {
            quality.StepNumber = 3;
            quality.StopName = "QUALITY INN";
            quality.Address = "117 CEDAR LN";
            quality.City = "KNOXVILLE";
            quality.State = "TN";
            quality.HighwayExit = "#I-75 & EXIT 108";
            fixedCount++;
        }

        if (fixedCount > 0)
            await _context.SaveChangesAsync(cancellationToken);

        return fixedCount;
    }

    public async Task<StopGeocodeResult> GeocodeMissingAsync(
        int? limit = null,
        int delayMs = 350,
        CancellationToken cancellationToken = default)
    {
        var result = new StopGeocodeResult();
        var stops = await _context.Stops
            .Include(s => s.Route)
            .Where(s => s.Status == StopStatus.Active
                && (s.Latitude == null || s.Longitude == null))
            .OrderBy(s => s.Route!.RouteName)
            .ThenBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .ToListAsync(cancellationToken);

        result.Eligible = stops.Count;
        if (limit is > 0)
            stops = stops.Take(limit.Value).ToList();

        foreach (var stop in stops)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var search = StopAddressHelper.GetGeocodingQuery(stop);
            var label = $"#{stop.Id} [{stop.Route?.RouteName}] {stop.StopName}";

            if (string.IsNullOrWhiteSpace(search) || search == "—")
            {
                result.SkippedNoQuery++;
                result.Failures.Add($"{label}: no address to geocode");
                continue;
            }

            try
            {
                var coords = await GeocodeAsync(stop, cancellationToken);
                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);

                if (coords == null)
                {
                    result.NotFound++;
                    result.Failures.Add($"{label}: no match for '{search}'");
                    continue;
                }

                stop.Latitude = coords.Value.Latitude;
                stop.Longitude = coords.Value.Longitude;
                await _context.SaveChangesAsync(cancellationToken);
                result.Updated++;
                result.Updates.Add(
                    $"{label}: {coords.Value.Latitude.ToString("0.######", CultureInfo.InvariantCulture)}, {coords.Value.Longitude.ToString("0.######", CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Failures.Add($"{label}: {ex.Message}");
                _logger.LogWarning(ex, "Geocode failed for stop {StopId}", stop.Id);
            }
        }

        return result;
    }

    private async Task<(double Latitude, double Longitude)?> GeocodeAsync(
        Stop stop,
        CancellationToken cancellationToken)
    {
        foreach (var query in CensusQueries(stop))
        {
            var census = await GeocodeCensusAsync(query, cancellationToken);
            if (census != null && IsPlausibleUsCoordinate(census.Value.Latitude, census.Value.Longitude))
                return census;
        }

        if (_dataForSeo.IsConfigured)
        {
            var mapsQuery = StopAddressHelper.GetGeocodingQuery(stop);
            if (!string.IsNullOrWhiteSpace(mapsQuery) && mapsQuery != "—")
            {
                var maps = await _dataForSeo.GeocodeAsync(mapsQuery, cancellationToken);
                if (maps != null && IsPlausibleUsCoordinate(maps.Value.Latitude, maps.Value.Longitude))
                    return maps;
            }
        }

        return null;
    }

    private static IEnumerable<string> CensusQueries(Stop stop)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != "—")
                seen.Add(trimmed);
        }

        Add(StopAddressHelper.FormatFullAddress(stop));
        if (!string.IsNullOrWhiteSpace(stop.Address)
            && !string.IsNullOrWhiteSpace(stop.City)
            && !string.IsNullOrWhiteSpace(stop.State))
        {
            Add($"{stop.Address.Trim()}, {stop.City.Trim()}, {stop.State.Trim()}");
        }

        Add(StopAddressHelper.GetGeocodingQuery(stop));
        return seen;
    }

    private static bool IsPlausibleUsCoordinate(double latitude, double longitude) =>
        latitude is >= 24 and <= 50 && longitude is >= -125 and <= -66;

    private async Task<(double Latitude, double Longitude)?> GeocodeCensusAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var url =
            "https://geocoding.geo.census.gov/geocoder/locations/onelineaddress"
            + "?benchmark=Public_AR_Current&format=json&address="
            + Uri.EscapeDataString(query);

        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Census geocoder {Status} for {Query}", (int)response.StatusCode, query);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("addressMatches", out var matches)
            || matches.ValueKind != JsonValueKind.Array
            || matches.GetArrayLength() == 0)
        {
            return null;
        }

        var coordinates = matches[0].GetProperty("coordinates");
        var longitude = coordinates.GetProperty("x").GetDouble();
        var latitude = coordinates.GetProperty("y").GetDouble();
        return (latitude, longitude);
    }
}

public class StopGeocodeResult
{
    public int Eligible { get; set; }
    public int Updated { get; set; }
    public int NotFound { get; set; }
    public int SkippedNoQuery { get; set; }
    public int Failed { get; set; }
    public List<string> Updates { get; } = new();
    public List<string> Failures { get; } = new();
}
