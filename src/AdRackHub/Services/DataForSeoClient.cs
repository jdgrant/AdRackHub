using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public class DataForSeoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly DataForSeoOptions _options;
    private readonly ILogger<DataForSeoClient> _logger;

    public DataForSeoClient(
        HttpClient http,
        IOptions<DataForSeoOptions> options,
        ILogger<DataForSeoClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress ??= new Uri("https://api.dataforseo.com/");
        _http.Timeout = TimeSpan.FromMinutes(2);
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<(double Latitude, double Longitude)?> GeocodeAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var payload = new[]
        {
            new
            {
                keyword = query.Trim(),
                location_name = "United States",
                language_code = "en",
                depth = 10
            }
        };

        var root = await PostAsync<DataForSeoMapsResponse>(
            "v3/serp/google/maps/live/advanced",
            payload,
            cancellationToken);

        var first = root.Tasks?
            .SelectMany(t => t.Result ?? Enumerable.Empty<DataForSeoMapsResult>())
            .SelectMany(r => r.Items ?? Enumerable.Empty<DataForSeoMapsItem>())
            .FirstOrDefault(i => i.Latitude.HasValue && i.Longitude.HasValue);

        if (first?.Latitude == null || first.Longitude == null)
            return null;

        return (first.Latitude.Value, first.Longitude.Value);
    }

    public async Task<IReadOnlyList<DataForSeoBusinessListing>> SearchHotelsNearAsync(
        double latitude,
        double longitude,
        int radiusKm = 2,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var coordinate = string.Create(
            CultureInfo.InvariantCulture,
            $"{latitude:0.#######},{longitude:0.#######},{Math.Max(1, radiusKm)}");

        var payload = new[]
        {
            new
            {
                categories = new[] { "hotel", "lodging", "motel", "inn" },
                location_coordinate = coordinate,
                limit
            }
        };

        var root = await PostAsync<DataForSeoBusinessListingsResponse>(
            "v3/business_data/business_listings/search/live",
            payload,
            cancellationToken);

        return root.Tasks?
            .SelectMany(t => t.Result ?? Enumerable.Empty<DataForSeoBusinessListingsResult>())
            .SelectMany(r => r.Items ?? Enumerable.Empty<DataForSeoBusinessListing>())
            .Where(i => i.Latitude.HasValue && i.Longitude.HasValue && !string.IsNullOrWhiteSpace(i.Title))
            .ToList()
            ?? (IReadOnlyList<DataForSeoBusinessListing>)Array.Empty<DataForSeoBusinessListing>();
    }

    private async Task<T> PostAsync<T>(
        string path,
        object payload,
        CancellationToken cancellationToken)
        where T : DataForSeoResponseBase
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("DataForSEO login and password are not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Login}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DataForSEO {Path} failed: {Status} {Body}", path, (int)response.StatusCode, Truncate(body));
            throw new InvalidOperationException($"DataForSEO request failed ({(int)response.StatusCode}). Check credentials and quota.");
        }

        var root = JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("DataForSEO returned an empty response.");

        if (root.StatusCode is not null and not 20000)
        {
            _logger.LogError("DataForSEO {Path} status {Code}: {Message}", path, root.StatusCode, root.StatusMessage);
            throw new InvalidOperationException(root.StatusMessage ?? $"DataForSEO error {root.StatusCode}.");
        }

        _logger.LogInformation("DataForSEO {Path} ok. Cost={Cost} Time={Time}", path, root.Cost, root.Time);
        return root;
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500] + "…";
}

public abstract class DataForSeoResponseBase
{
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("cost")]
    public double? Cost { get; set; }
}

public class DataForSeoMapsResponse : DataForSeoResponseBase
{
    [JsonPropertyName("tasks")]
    public List<DataForSeoMapsTask>? Tasks { get; set; }
}

public class DataForSeoMapsTask
{
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    [JsonPropertyName("result")]
    public List<DataForSeoMapsResult>? Result { get; set; }
}

public class DataForSeoMapsResult
{
    [JsonPropertyName("items")]
    public List<DataForSeoMapsItem>? Items { get; set; }
}

public class DataForSeoMapsItem
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("place_id")]
    public string? PlaceId { get; set; }
}

public class DataForSeoBusinessListingsResponse : DataForSeoResponseBase
{
    [JsonPropertyName("tasks")]
    public List<DataForSeoBusinessListingsTask>? Tasks { get; set; }
}

public class DataForSeoBusinessListingsTask
{
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    [JsonPropertyName("result")]
    public List<DataForSeoBusinessListingsResult>? Result { get; set; }
}

public class DataForSeoBusinessListingsResult
{
    [JsonPropertyName("items")]
    public List<DataForSeoBusinessListing>? Items { get; set; }
}

public class DataForSeoBusinessListing
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("place_id")]
    public string? PlaceId { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("address_info")]
    public DataForSeoAddressInfo? AddressInfo { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("hotel_rating")]
    public int? HotelRating { get; set; }
}

public class DataForSeoAddressInfo
{
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}
