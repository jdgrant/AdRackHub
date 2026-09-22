using System.Globalization;
using System.Text.Json;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class CustomerGeocodeService
{
    private readonly ApplicationDbContext _context;
    private readonly DataForSeoClient _dataForSeo;
    private readonly HttpClient _http;
    private readonly ILogger<CustomerGeocodeService> _logger;

    public CustomerGeocodeService(
        ApplicationDbContext context,
        DataForSeoClient dataForSeo,
        IHttpClientFactory httpClientFactory,
        ILogger<CustomerGeocodeService> logger)
    {
        _context = context;
        _dataForSeo = dataForSeo;
        _http = httpClientFactory.CreateClient(nameof(CustomerGeocodeService));
        _logger = logger;
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(45))
            _http.Timeout = TimeSpan.FromSeconds(45);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AdRackHub/1.0");
    }

    public async Task<CustomerGeocodeResult> GeocodeMissingAsync(
        bool force = false,
        int? limit = null,
        int delayMs = 400,
        CancellationToken cancellationToken = default)
    {
        var result = new CustomerGeocodeResult();
        var query = _context.Customers.Include(c => c.Contacts).AsQueryable();
        if (!force)
        {
            query = query.Where(c =>
                c.Latitude == null
                || c.Longitude == null
                || c.Latitude < 24
                || c.Latitude > 50
                || c.Longitude < -125
                || c.Longitude > -66);
        }

        var customers = await query
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

        result.Eligible = customers.Count;
        if (limit is > 0)
            customers = customers.Take(limit.Value).ToList();

        foreach (var customer in customers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var search = CustomerAddressHelper.GetGeocodingQuery(customer);
            if (string.IsNullOrWhiteSpace(search))
            {
                result.SkippedNoQuery++;
                result.Failures.Add($"#{customer.Id} {customer.CustomerName}: no address to geocode");
                continue;
            }

            try
            {
                var found = await ApplyAsync(customer, requireAddressParts: false, cancellationToken);
                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);

                if (!found)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    result.NotFound++;
                    result.Failures.Add($"#{customer.Id} {customer.CustomerName}: no match for '{search}'");
                    continue;
                }

                customer.NeedsMoreInfo = CustomerContactCompleteness.NeedsMoreInfo(customer);
                await _context.SaveChangesAsync(cancellationToken);
                result.Updated++;
                result.Updates.Add(
                    $"#{customer.Id} {customer.CustomerName}: {customer.Latitude!.Value.ToString("0.######", CultureInfo.InvariantCulture)}, {customer.Longitude!.Value.ToString("0.######", CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Failures.Add($"#{customer.Id} {customer.CustomerName}: {ex.Message}");
                _logger.LogWarning(ex, "Geocode failed for customer {CustomerId}", customer.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Rebuilds only Latitude and Longitude for one customer. Does not change High Value,
    /// At Risk, Needs More Info, or any other fields. Leaves existing coordinates in place
    /// if the geocoder does not return a match.
    /// </summary>
    public async Task<bool> UpdateCoordinatesOnlyAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer == null)
            return false;

        if (!CustomerAddressHelper.HasGeocodableAddress(customer))
        {
            if (customer.Latitude.HasValue || customer.Longitude.HasValue)
            {
                customer.Latitude = null;
                customer.Longitude = null;
                await _context.SaveChangesAsync(cancellationToken);
            }
            return false;
        }

        var coords = await GeocodeAsync(customer, cancellationToken);
        if (coords == null)
            return false;

        customer.Latitude = coords.Value.Latitude;
        customer.Longitude = coords.Value.Longitude;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ApplyAsync(
        Customer customer,
        bool requireAddressParts = true,
        CancellationToken cancellationToken = default)
    {
        if (requireAddressParts && !CustomerAddressHelper.HasGeocodableAddress(customer))
        {
            customer.Latitude = null;
            customer.Longitude = null;
            return false;
        }

        var coords = await GeocodeAsync(customer, cancellationToken);
        if (coords == null || !IsPlausibleUsCoordinate(coords.Value.Latitude, coords.Value.Longitude))
        {
            customer.Latitude = null;
            customer.Longitude = null;
            return false;
        }

        customer.Latitude = coords.Value.Latitude;
        customer.Longitude = coords.Value.Longitude;
        return true;
    }

    private async Task<(double Latitude, double Longitude)?> GeocodeAsync(
        Customer customer,
        CancellationToken cancellationToken)
    {
        foreach (var query in CensusQueries(customer))
        {
            if (IsZipOnlyQuery(query))
                continue;

            var census = await GeocodeCensusAsync(query, cancellationToken);
            if (census != null && IsPlausibleUsCoordinate(census.Value.Latitude, census.Value.Longitude))
                return census;
        }

        if (_dataForSeo.IsConfigured && HasStreetOrCity(customer))
        {
            var mapsQuery = CustomerAddressHelper.GetGeocodingQuery(customer);
            var maps = await _dataForSeo.GeocodeAsync(mapsQuery, cancellationToken);
            if (maps != null && IsPlausibleUsCoordinate(maps.Value.Latitude, maps.Value.Longitude))
                return maps;
        }

        var zip = CustomerAddressHelper.GetUsZip(customer);
        if (!string.IsNullOrWhiteSpace(zip))
        {
            var centroid = await GeocodeZipCentroidAsync(zip, cancellationToken);
            if (centroid != null && IsPlausibleUsCoordinate(centroid.Value.Latitude, centroid.Value.Longitude))
                return centroid;
        }

        return null;
    }

    private static bool HasStreetOrCity(Customer customer) =>
        !string.IsNullOrWhiteSpace(customer.Address)
        || !string.IsNullOrWhiteSpace(customer.City)
        || customer.Contacts?.Any(c =>
            !string.IsNullOrWhiteSpace(c.Address) || !string.IsNullOrWhiteSpace(c.City)) == true;

    private static bool IsZipOnlyQuery(string query)
    {
        var zip = CustomerAddressHelper.NormalizeUsZip(query);
        return zip != null && string.Equals(query.Trim(), zip, StringComparison.Ordinal);
    }

    private static IEnumerable<string> CensusQueries(Customer customer)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                seen.Add(trimmed);
        }

        Add(CustomerAddressHelper.FormatFullAddress(customer.Address, customer.City, customer.State, customer.Zip));
        Add(CustomerAddressHelper.FormatFullAddress(null, customer.City, customer.State, customer.Zip));
        Add(CustomerAddressHelper.GetGeocodingQuery(customer));
        return seen;
    }

    private static bool IsPlausibleUsCoordinate(double latitude, double longitude) =>
        latitude is >= 24 and <= 50 && longitude is >= -125 and <= -66;

    private async Task<(double Latitude, double Longitude)?> GeocodeZipCentroidAsync(
        string zip,
        CancellationToken cancellationToken)
    {
        var url = "https://api.zippopotam.us/us/" + Uri.EscapeDataString(zip);
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ZIP centroid {Status} for {Zip}", (int)response.StatusCode, zip);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("places", out var places)
            || places.ValueKind != JsonValueKind.Array
            || places.GetArrayLength() == 0)
        {
            return null;
        }

        var place = places[0];
        if (!place.TryGetProperty("latitude", out var latEl)
            || !place.TryGetProperty("longitude", out var lonEl)
            || !double.TryParse(latEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(lonEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return null;
        }

        return (latitude, longitude);
    }

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

public class CustomerGeocodeResult
{
    public int Eligible { get; set; }
    public int Updated { get; set; }
    public int NotFound { get; set; }
    public int SkippedNoQuery { get; set; }
    public int Failed { get; set; }
    public List<string> Updates { get; } = new();
    public List<string> Failures { get; } = new();
}
