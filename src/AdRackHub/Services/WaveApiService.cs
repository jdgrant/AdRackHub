using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public class WaveApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly WaveOptions _options;

    public WaveApiService(HttpClient httpClient, IOptions<WaveOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => _options.IsConfigured;

    public static bool IsLikelyWaveCustomerId(string? waveCustomerId) =>
        !string.IsNullOrWhiteSpace(waveCustomerId)
        && waveCustomerId.Length >= 12
        && waveCustomerId.All(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=');

    public async Task<WaveCustomerResult> CreateCustomerAsync(
        WaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            return WaveCustomerResult.Failed("Wave API is not configured. Set Wave:AccessToken and Wave:BusinessId.");

        const string mutation = """
            mutation ($input: CustomerCreateInput!) {
              customerCreate(input: $input) {
                didSucceed
                inputErrors {
                  message
                  code
                  path
                }
                customer {
                  id
                  name
                  email
                }
              }
            }
            """;

        object? address = null;
        if (!string.IsNullOrWhiteSpace(request.AddressLine1)
            || !string.IsNullOrWhiteSpace(request.City)
            || !string.IsNullOrWhiteSpace(request.PostalCode))
        {
            address = new
            {
                addressLine1 = request.AddressLine1,
                city = request.City,
                postalCode = request.PostalCode,
                provinceCode = ToProvinceCode(request.StateCode),
                countryCode = _options.DefaultCountryCode
            };
        }

        var payload = new
        {
            query = mutation,
            variables = new
            {
                input = new
                {
                    businessId = _options.BusinessId,
                    name = request.Name,
                    firstName = request.FirstName,
                    lastName = request.LastName,
                    email = request.Email,
                    currency = _options.DefaultCurrency,
                    address
                }
            }
        };

        var response = await ExecuteGraphQlAsync(payload, cancellationToken);
        if (!response.Success)
            return WaveCustomerResult.Failed(response.ErrorMessage!);

        try
        {
            var customerCreate = response.Data!.Value.GetProperty("customerCreate");
            if (!customerCreate.GetProperty("didSucceed").GetBoolean())
            {
                var errors = customerCreate.GetProperty("inputErrors").EnumerateArray()
                    .Select(e => e.GetProperty("message").GetString())
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                return WaveCustomerResult.Failed(string.Join("; ", errors));
            }

            var customer = customerCreate.GetProperty("customer");
            return WaveCustomerResult.Succeeded(customer.GetProperty("id").GetString()!);
        }
        catch (Exception ex)
        {
            return WaveCustomerResult.Failed($"Failed to parse Wave customer response: {ex.Message}");
        }
    }

    public async Task<WaveInvoiceResult> CreateInvoiceAsync(
        string waveCustomerId,
        DateOnly invoiceDate,
        IReadOnlyList<WaveInvoiceLineItem> lineItems,
        string? memo = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            return WaveInvoiceResult.Failed("Wave API is not configured. Set Wave:AccessToken and Wave:BusinessId.");

        const string mutation = """
            mutation ($input: InvoiceCreateInput!) {
              invoiceCreate(input: $input) {
                didSucceed
                inputErrors {
                  message
                  code
                  path
                }
                invoice {
                  id
                  viewUrl
                  invoiceNumber
                }
              }
            }
            """;

        var payload = new
        {
            query = mutation,
            variables = new
            {
                input = new
                {
                    businessId = _options.BusinessId,
                    customerId = waveCustomerId,
                    status = "DRAFT",
                    invoiceDate = invoiceDate.ToString("yyyy-MM-dd"),
                    memo,
                    items = lineItems.Select(item => new
                    {
                        description = item.Description,
                        quantity = item.Quantity,
                        unitPrice = item.UnitPrice
                    }).ToArray()
                }
            }
        };

        var response = await ExecuteGraphQlAsync(payload, cancellationToken);
        if (!response.Success)
            return WaveInvoiceResult.Failed(response.ErrorMessage!);

        try
        {
            var invoiceCreate = response.Data!.Value.GetProperty("invoiceCreate");
            if (!invoiceCreate.GetProperty("didSucceed").GetBoolean())
            {
                var errors = invoiceCreate.GetProperty("inputErrors").EnumerateArray()
                    .Select(e => e.GetProperty("message").GetString())
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                return WaveInvoiceResult.Failed(string.Join("; ", errors));
            }

            var invoice = invoiceCreate.GetProperty("invoice");
            return WaveInvoiceResult.Succeeded(
                invoice.GetProperty("id").GetString()!,
                invoice.TryGetProperty("viewUrl", out var viewUrl) ? viewUrl.GetString() : null,
                invoice.TryGetProperty("invoiceNumber", out var invoiceNumber) ? invoiceNumber.GetString() : null);
        }
        catch (Exception ex)
        {
            return WaveInvoiceResult.Failed($"Failed to parse Wave invoice response: {ex.Message}");
        }
    }

    private async Task<WaveGraphQlResponse> ExecuteGraphQlAsync(object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.GraphQLEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            return WaveGraphQlResponse.Fail($"Wave API returned {(int)response.StatusCode}: {body}");

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.Clone();

            if (root.TryGetProperty("errors", out var topLevelErrors) && topLevelErrors.ValueKind == JsonValueKind.Array)
            {
                var message = string.Join("; ", topLevelErrors.EnumerateArray()
                    .Select(e => e.GetProperty("message").GetString()));
                return WaveGraphQlResponse.Fail(message);
            }

            if (!root.TryGetProperty("data", out var data))
                return WaveGraphQlResponse.Fail("Wave API response missing data.");

            return WaveGraphQlResponse.Ok(data.Clone());
        }
        catch (Exception ex)
        {
            return WaveGraphQlResponse.Fail($"Failed to parse Wave response: {ex.Message}");
        }
    }

    private static string? ToProvinceCode(string? stateCode)
    {
        if (string.IsNullOrWhiteSpace(stateCode))
            return null;

        var normalized = stateCode.Trim().ToUpperInvariant();
        return normalized.Contains('-') ? normalized : $"US-{normalized}";
    }

    private sealed class WaveGraphQlResponse
    {
        public bool Success { get; init; }
        public JsonElement? Data { get; init; }
        public string? ErrorMessage { get; init; }

        public static WaveGraphQlResponse Ok(JsonElement data) =>
            new() { Success = true, Data = data };

        public static WaveGraphQlResponse Fail(string message) =>
            new() { Success = false, ErrorMessage = message };
    }
}

public sealed class WaveCustomerRequest
{
    public string Name { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? AddressLine1 { get; init; }
    public string? City { get; init; }
    public string? StateCode { get; init; }
    public string? PostalCode { get; init; }
}

public sealed class WaveInvoiceLineItem
{
    public string Description { get; init; } = string.Empty;
    public decimal Quantity { get; init; } = 1;
    public decimal UnitPrice { get; init; }
}

public sealed class WaveCustomerResult
{
    public bool Success { get; init; }
    public string? WaveCustomerId { get; init; }
    public string? ErrorMessage { get; init; }

    public static WaveCustomerResult Succeeded(string id) =>
        new() { Success = true, WaveCustomerId = id };

    public static WaveCustomerResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class WaveInvoiceResult
{
    public bool Success { get; init; }
    public string? WaveInvoiceId { get; init; }
    public string? WaveInvoiceUrl { get; init; }
    public string? InvoiceNumber { get; init; }
    public string? ErrorMessage { get; init; }

    public static WaveInvoiceResult Succeeded(string id, string? url, string? invoiceNumber) =>
        new() { Success = true, WaveInvoiceId = id, WaveInvoiceUrl = url, InvoiceNumber = invoiceNumber };

    public static WaveInvoiceResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}
