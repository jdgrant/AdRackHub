using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public class WaveApiService
{
    public const int InvoiceDueDays = 30;

    public const string DefaultOAuthScopes =
        "business:read customer:read customer:write invoice:read invoice:write invoice:send user:read";

    public const string SessionExpiredReconnectMessage =
        "Your Wave session expired. Reset the business, then send the contract again.";

    public static bool IsSessionExpiredMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        if (message.Contains(SessionExpiredReconnectMessage, StringComparison.OrdinalIgnoreCase))
            return true;
        return IsAuthFailureText(message);
    }

    public static bool IsAuthFailureText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || text.Contains("invalid_token", StringComparison.OrdinalIgnoreCase)
            || text.Contains("invalid token", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Not authenticated", StringComparison.OrdinalIgnoreCase)
            || text.Contains("expired access", StringComparison.OrdinalIgnoreCase)
            || text.Contains("access token expired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("token expired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Wave API returned 401", StringComparison.OrdinalIgnoreCase);
    }

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
    public bool IsOAuthConfigured => _options.IsOAuthConfigured;

    public static bool IsLikelyWaveCustomerId(string? waveCustomerId) =>
        !string.IsNullOrWhiteSpace(waveCustomerId)
        && waveCustomerId.Length >= 12
        && waveCustomerId.All(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=');

    public async Task<WaveCustomerResult> CreateCustomerAsync(
        WaveCustomerRequest request,
        CancellationToken cancellationToken = default,
        string? accessToken = null,
        string? businessId = null)
    {
        if (!HasApiCredentials(accessToken, businessId))
            return WaveCustomerResult.Failed("Wave API is not configured. Connect OAuth or set Wave:AccessToken and Wave:BusinessId.");

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
                    businessId = ResolveBusinessId(businessId),
                    name = request.Name,
                    firstName = request.FirstName,
                    lastName = request.LastName,
                    email = request.Email,
                    currency = _options.DefaultCurrency,
                    address
                }
            }
        };

        var response = await ExecuteGraphQlAsync(payload, cancellationToken, accessToken);
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
        CancellationToken cancellationToken = default,
        string? accessToken = null,
        string? businessId = null)
    {
        if (!HasApiCredentials(accessToken, businessId))
            return WaveInvoiceResult.Failed("Wave API is not configured. Connect OAuth or set Wave:AccessToken and Wave:BusinessId.");
        _ = memo;

        var missingProducts = lineItems
            .Select((item, index) => string.IsNullOrWhiteSpace(item.ProductId)
                ? $"line {index + 1}{(string.IsNullOrWhiteSpace(item.Description) ? "" : $" ({item.Description})")}"
                : null)
            .Where(line => line != null)
            .ToList();
        if (missingProducts.Count > 0)
        {
            return WaveInvoiceResult.Failed(
                "Each invoice line needs the route's Wave Product ID. Missing: "
                + string.Join("; ", missingProducts)
                + ". Set Wave Product ID on the route.");
        }

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
                  pdfUrl
                  dueDate
                  footer
                  memo
                }
              }
            }
            """;

        var defaults = await ResolveInvoiceDocumentTextAsync(cancellationToken, accessToken, businessId);
        var payload = new
        {
            query = mutation,
            variables = new
            {
                input = new
                {
                    businessId = ResolveBusinessId(businessId),
                    customerId = waveCustomerId,
                    status = "DRAFT",
                    invoiceDate = invoiceDate.ToString("yyyy-MM-dd"),
                    dueDate = invoiceDate.AddDays(InvoiceDueDays).ToString("yyyy-MM-dd"),
                    memo = defaults.Memo,
                    footer = defaults.Footer,
                    disableBankPayments = false,
                    disableCreditCardPayments = true,
                    items = lineItems.Select(item => new
                    {
                        productId = ToWaveGraphQlProductId(item.ProductId!, ResolveBusinessId(businessId)),
                        description = item.Description,
                        quantity = item.Quantity,
                        unitPrice = item.UnitPrice
                    }).ToArray()
                }
            }
        };

        var response = await ExecuteGraphQlAsync(payload, cancellationToken, accessToken);
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
            return FromInvoiceNode(invoice);
        }
        catch (Exception ex)
        {
            return WaveInvoiceResult.Failed($"Failed to parse Wave invoice response: {ex.Message}");
        }
    }

    public async Task<WaveInvoiceResult> ApproveInvoiceAsync(
        string waveInvoiceId,
        CancellationToken cancellationToken = default,
        string? accessToken = null)
    {
        if (string.IsNullOrWhiteSpace(ResolveAccessToken(accessToken)))
            return WaveInvoiceResult.Failed("Connect to Wave before approving an invoice.");
        if (string.IsNullOrWhiteSpace(waveInvoiceId))
            return WaveInvoiceResult.Failed("A Wave invoice id is required to approve.");

        const string mutation = """
            mutation ($input: InvoiceApproveInput!) {
              invoiceApprove(input: $input) {
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
                  status
                  pdfUrl
                  dueDate
                }
              }
            }
            """;

        var payload = new
        {
            query = mutation,
            variables = new
            {
                input = new { invoiceId = waveInvoiceId }
            }
        };

        var response = await ExecuteGraphQlAsync(payload, cancellationToken, accessToken);
        if (!response.Success)
            return WaveInvoiceResult.Failed(response.ErrorMessage!);

        try
        {
            var invoiceApprove = response.Data!.Value.GetProperty("invoiceApprove");
            if (!invoiceApprove.GetProperty("didSucceed").GetBoolean())
            {
                var errors = invoiceApprove.GetProperty("inputErrors").EnumerateArray()
                    .Select(e => e.GetProperty("message").GetString())
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                return WaveInvoiceResult.Failed(string.Join("; ", errors));
            }

            var invoice = invoiceApprove.GetProperty("invoice");
            return FromInvoiceNode(invoice, "SAVED");
        }
        catch (Exception ex)
        {
            return WaveInvoiceResult.Failed($"Failed to parse Wave approve response: {ex.Message}");
        }
    }

    public async Task<WaveSendResult> SendInvoiceAsync(
        string waveInvoiceId,
        IReadOnlyList<string> toEmails,
        CancellationToken cancellationToken = default,
        string? accessToken = null,
        string? businessId = null,
        string? customerName = null)
    {
        if (string.IsNullOrWhiteSpace(ResolveAccessToken(accessToken)))
            return WaveSendResult.Failed("Connect to Wave before sending an invoice.");
        if (string.IsNullOrWhiteSpace(waveInvoiceId))
            return WaveSendResult.Failed("A Wave invoice id is required to send.");

        var emails = toEmails
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (emails.Count == 0)
            return WaveSendResult.Failed("No contacts have an email address.");

        var invoice = await GetInvoiceAsync(waveInvoiceId, cancellationToken, accessToken, businessId);
        var subject = ApplyInvoiceEmailTemplate(InvoiceEmailSubject(), invoice, customerName);
        var message = ApplyInvoiceEmailTemplate(InvoiceEmailMessage(), invoice, customerName);

        const string mutation = """
            mutation ($input: InvoiceSendInput!) {
              invoiceSend(input: $input) {
                didSucceed
                inputErrors {
                  message
                  code
                  path
                }
                invoice {
                  id
                  invoiceNumber
                  status
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
                    invoiceId = waveInvoiceId,
                    to = emails,
                    attachPDF = true,
                    fromAddress = InvoiceFromAddress(),
                    subject,
                    message
                }
            }
        };

        var response = await ExecuteGraphQlAsync(payload, cancellationToken, accessToken);
        if (!response.Success)
            return WaveSendResult.Failed(response.ErrorMessage ?? "Wave invoiceSend failed.");

        try
        {
            var invoiceSend = response.Data!.Value.GetProperty("invoiceSend");
            if (!invoiceSend.GetProperty("didSucceed").GetBoolean())
            {
                var errors = invoiceSend.GetProperty("inputErrors").EnumerateArray()
                    .Select(e => e.GetProperty("message").GetString())
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                return WaveSendResult.Failed(string.Join("; ", errors));
            }

            return WaveSendResult.Succeeded(emails);
        }
        catch (Exception ex)
        {
            return WaveSendResult.Failed($"Failed to parse Wave send response: {ex.Message}");
        }
    }

    public async Task<WaveInvoiceResult> GetInvoiceAsync(
        string waveInvoiceId,
        CancellationToken cancellationToken = default,
        string? accessToken = null,
        string? businessId = null)
    {
        if (!HasApiCredentials(accessToken, businessId))
            return WaveInvoiceResult.Failed("Wave API is not configured.");

        const string query = """
            query ($businessId: ID!, $invoiceId: ID!) {
              business(id: $businessId) {
                name
                invoice(id: $invoiceId) {
                  id
                  viewUrl
                  invoiceNumber
                  status
                  pdfUrl
                  dueDate
                  footer
                  memo
                  customer {
                    name
                  }
                  total {
                    value
                  }
                  amountDue {
                    value
                  }
                }
              }
            }
            """;

        var response = await ExecuteGraphQlAsync(
            new { query, variables = new { businessId = ResolveBusinessId(businessId), invoiceId = waveInvoiceId } },
            cancellationToken,
            accessToken);
        if (!response.Success || response.Data == null)
            return WaveInvoiceResult.Failed(response.ErrorMessage ?? "Wave invoice query failed.");

        try
        {
            if (!response.Data.Value.TryGetProperty("business", out var business)
                || business.ValueKind != JsonValueKind.Object
                || !business.TryGetProperty("invoice", out var invoice)
                || invoice.ValueKind != JsonValueKind.Object)
                return WaveInvoiceResult.Failed("Wave invoice was not found.");
            return FromInvoiceNode(
                invoice,
                businessName: ReadString(business, "name"));
        }
        catch (Exception ex)
        {
            return WaveInvoiceResult.Failed($"Failed to parse Wave invoice query: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<WaveInvoiceListItem>> ListRecentInvoicesAsync(
        CancellationToken cancellationToken = default,
        string? accessToken = null,
        string? businessId = null,
        int pageSize = 50)
    {
        if (!HasApiCredentials(accessToken, businessId))
            return Array.Empty<WaveInvoiceListItem>();

        const string query = """
            query ($businessId: ID!, $pageSize: Int!) {
              business(id: $businessId) {
                invoices(page: 1, pageSize: $pageSize, sort: [CREATED_AT_DESC]) {
                  edges {
                    node {
                      id
                      invoiceNumber
                      status
                      customer { name }
                    }
                  }
                }
              }
            }
            """;

        var response = await ExecuteGraphQlAsync(
            new { query, variables = new { businessId = ResolveBusinessId(businessId), pageSize } },
            cancellationToken,
            accessToken);
        if (!response.Success || response.Data == null)
            return Array.Empty<WaveInvoiceListItem>();

        try
        {
            if (!response.Data.Value.TryGetProperty("business", out var business)
                || !business.TryGetProperty("invoices", out var invoices)
                || !invoices.TryGetProperty("edges", out var edges))
                return Array.Empty<WaveInvoiceListItem>();

            return edges.EnumerateArray()
                .Select(edge => edge.GetProperty("node"))
                .Select(node => new WaveInvoiceListItem(
                    node.GetProperty("id").GetString() ?? string.Empty,
                    node.TryGetProperty("invoiceNumber", out var number) ? number.GetString() : null,
                    node.TryGetProperty("status", out var status) ? status.GetString() : null,
                    ReadNestedString(node, "customer", "name")))
                .Where(item => !string.IsNullOrWhiteSpace(item.WaveInvoiceId))
                .ToList();
        }
        catch
        {
            return Array.Empty<WaveInvoiceListItem>();
        }
    }

    public async Task<WaveInvoiceResult> DeleteInvoiceAsync(
        string waveInvoiceId,
        CancellationToken cancellationToken = default,
        string? accessToken = null)
    {
        if (string.IsNullOrWhiteSpace(ResolveAccessToken(accessToken)))
            return WaveInvoiceResult.Failed("Connect to Wave before deleting an invoice.");
        if (string.IsNullOrWhiteSpace(waveInvoiceId))
            return WaveInvoiceResult.Failed("A Wave invoice id is required to delete.");

        const string mutation = """
            mutation ($input: InvoiceDeleteInput!) {
              invoiceDelete(input: $input) {
                didSucceed
                inputErrors {
                  message
                  code
                  path
                }
              }
            }
            """;

        var response = await ExecuteGraphQlAsync(
            new { query = mutation, variables = new { input = new { invoiceId = waveInvoiceId } } },
            cancellationToken,
            accessToken);
        if (!response.Success)
            return WaveInvoiceResult.Failed(response.ErrorMessage!);

        try
        {
            var deleted = response.Data!.Value.GetProperty("invoiceDelete");
            if (!deleted.GetProperty("didSucceed").GetBoolean())
            {
                var errors = deleted.GetProperty("inputErrors").EnumerateArray()
                    .Select(e => e.GetProperty("message").GetString())
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                return WaveInvoiceResult.Failed(string.Join("; ", errors));
            }

            return WaveInvoiceResult.Succeeded(waveInvoiceId, null, null);
        }
        catch (Exception ex)
        {
            return WaveInvoiceResult.Failed($"Failed to parse Wave invoiceDelete response: {ex.Message}");
        }
    }

    public async Task<byte[]?> DownloadInvoicePdfAsync(
        string waveInvoiceId,
        CancellationToken cancellationToken = default,
        string? accessToken = null,
        string? businessId = null)
    {
        var invoice = await GetInvoiceAsync(waveInvoiceId, cancellationToken, accessToken, businessId);
        if (!invoice.Success || string.IsNullOrWhiteSpace(invoice.PdfUrl))
            return null;
        return await DownloadBytesAsync(invoice.PdfUrl, accessToken, cancellationToken);
    }

    public async Task<string?> ResolvePayUrlAsync(
        string waveInvoiceId,
        CancellationToken cancellationToken = default,
        string? accessToken = null,
        string? businessId = null,
        string? existingViewUrl = null,
        string? existingPdfUrl = null)
    {
        if (IsWaveShortPayUrl(existingViewUrl))
            return existingViewUrl!.Trim();

        var invoice = await GetInvoiceAsync(waveInvoiceId, cancellationToken, accessToken, businessId);
        if (IsWaveShortPayUrl(invoice.WaveInvoiceUrl))
            return invoice.WaveInvoiceUrl!.Trim();

        try
        {
            byte[]? pdf = null;
            var pdfUrl = FirstNonEmpty(invoice.PdfUrl, existingPdfUrl);
            if (!string.IsNullOrWhiteSpace(pdfUrl))
                pdf = await DownloadBytesAsync(pdfUrl, accessToken, cancellationToken);
            if (pdf == null || pdf.Length == 0)
                pdf = await DownloadInvoicePdfAsync(waveInvoiceId, cancellationToken, accessToken, businessId);

            var fromPdf = ExtractWaveShortPayUrl(pdf);
            if (!string.IsNullOrWhiteSpace(fromPdf))
                return fromPdf;
        }
        catch
        {
            // Fall back to the GraphQL view URL.
        }

        return FirstNonEmpty(invoice.WaveInvoiceUrl, existingViewUrl)?.Trim();
    }

    public static bool IsWaveShortPayUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Contains("link.waveapps.com/", StringComparison.OrdinalIgnoreCase);

    public static string? ExtractWaveShortPayUrl(byte[]? pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            return null;

        var fromRaw = MatchWaveShortPayUrl(Encoding.Latin1.GetString(pdfBytes));
        if (fromRaw != null)
            return fromRaw;

        foreach (var inflated in InflatePdfStreams(pdfBytes))
        {
            var found = MatchWaveShortPayUrl(Encoding.Latin1.GetString(inflated));
            if (found != null)
                return found;
        }

        return null;
    }

    private static string? MatchWaveShortPayUrl(string text)
    {
        var match = Regex.Match(
            text,
            @"https?://link\.waveapps\.com/[A-Za-z0-9-]+",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Value.Trim();

        match = Regex.Match(
            text,
            @"link\.waveapps\.com/[A-Za-z0-9]+-[A-Za-z0-9]+",
            RegexOptions.IgnoreCase);
        return match.Success ? "https://" + match.Value : null;
    }

    private static IEnumerable<byte[]> InflatePdfStreams(byte[] pdf)
    {
        var marker = Encoding.ASCII.GetBytes("stream");
        var endMarker = Encoding.ASCII.GetBytes("endstream");
        var index = 0;
        while (index < pdf.Length)
        {
            var streamAt = IndexOf(pdf, marker, index);
            if (streamAt < 0)
                yield break;

            var dataStart = streamAt + marker.Length;
            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\r')
                dataStart++;
            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\n')
                dataStart++;

            var endAt = IndexOf(pdf, endMarker, dataStart);
            if (endAt < 0)
                yield break;

            var length = endAt - dataStart;
            if (length > 2 && pdf[endAt - 2] == (byte)'\r' && pdf[endAt - 1] == (byte)'\n')
                length -= 2;
            else if (length > 0 && (pdf[endAt - 1] == (byte)'\n' || pdf[endAt - 1] == (byte)'\r'))
                length--;

            if (length > 0 && TryInflatePdfStream(pdf.AsSpan(dataStart, length), out var inflated))
                yield return inflated;

            index = endAt + endMarker.Length;
        }
    }

    private static bool TryInflatePdfStream(ReadOnlySpan<byte> raw, out byte[] inflated)
    {
        inflated = Array.Empty<byte>();
        foreach (var zlib in new[] { true, false })
        {
            try
            {
                using var input = new MemoryStream(raw.ToArray());
                using Stream decoder = zlib
                    ? new ZLibStream(input, CompressionMode.Decompress)
                    : new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                decoder.CopyTo(output);
                if (output.Length == 0)
                    continue;
                inflated = output.ToArray();
                return true;
            }
            catch
            {
                // Try the next decoder.
            }
        }

        return false;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        if (needle.Length == 0 || start > haystack.Length - needle.Length)
            return -1;
        var max = haystack.Length - needle.Length;
        for (var i = start; i <= max; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }
            if (found)
                return i;
        }
        return -1;
    }

    private async Task<byte[]?> DownloadBytesAsync(string url, string? accessToken, CancellationToken cancellationToken)
    {
        var token = ResolveAccessToken(accessToken);
        using var authorized = new HttpRequestMessage(HttpMethod.Get, url);
        authorized.Headers.Accept.ParseAdd("application/pdf,application/octet-stream,*/*");
        if (!string.IsNullOrWhiteSpace(token))
            authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(authorized, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var authorizedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (IsPdf(authorizedBytes))
                return authorizedBytes;
        }

        if (string.IsNullOrWhiteSpace(token))
            return null;

        using var anonymous = new HttpRequestMessage(HttpMethod.Get, url);
        using var retry = await _httpClient.SendAsync(anonymous, cancellationToken);
        if (!retry.IsSuccessStatusCode)
            return null;
        var anonymousBytes = await retry.Content.ReadAsByteArrayAsync(cancellationToken);
        return IsPdf(anonymousBytes) ? anonymousBytes : null;
    }

    private static bool IsPdf(byte[]? bytes) =>
        bytes is { Length: >= 4 }
        && bytes[0] == (byte)'%'
        && bytes[1] == (byte)'P'
        && bytes[2] == (byte)'D'
        && bytes[3] == (byte)'F';

    private static WaveInvoiceResult FromInvoiceNode(
        JsonElement invoice,
        string? fallbackStatus = null,
        string? businessName = null)
    {
        return WaveInvoiceResult.Succeeded(
            invoice.GetProperty("id").GetString()!,
            invoice.TryGetProperty("viewUrl", out var viewUrl) ? viewUrl.GetString() : null,
            invoice.TryGetProperty("invoiceNumber", out var invoiceNumber) ? invoiceNumber.GetString() : null,
            invoice.TryGetProperty("status", out var status) ? status.GetString() : fallbackStatus,
            invoice.TryGetProperty("pdfUrl", out var pdfUrl) ? pdfUrl.GetString() : null,
            invoice.TryGetProperty("dueDate", out var dueDate) ? dueDate.GetString() : null,
            ReadNestedString(invoice, "customer", "name"),
            ReadMoneyValue(invoice, "amountDue") ?? ReadMoneyValue(invoice, "total"),
            businessName);
    }

    public string BuildAuthorizationUrl(
        string redirectUri,
        string state,
        string? scope = null,
        string? clientId = null)
    {
        var scopes = scope ?? DefaultOAuthScopes;
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = FirstNonEmpty(clientId, _options.ClientId),
            ["response_type"] = "code",
            ["scope"] = scopes,
            ["redirect_uri"] = redirectUri,
            ["state"] = state
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(_options.AuthorizeUrl, query);
    }

    public async Task<WaveOAuthTokenResult> ExchangeAuthorizationCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default,
        string? clientId = null,
        string? clientSecret = null)
    {
        var resolvedClientId = FirstNonEmpty(clientId, _options.ClientId);
        var resolvedClientSecret = FirstNonEmpty(clientSecret, _options.ClientSecret);
        if (string.IsNullOrWhiteSpace(resolvedClientId) || string.IsNullOrWhiteSpace(resolvedClientSecret))
            return WaveOAuthTokenResult.Failed("Paste the Wave Client ID and Client Secret on the proof page first.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = resolvedClientId,
            ["client_secret"] = resolvedClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return WaveOAuthTokenResult.Failed($"Wave token exchange returned {(int)response.StatusCode}: {body}");

        return ParseTokenResponse(body);
    }

    public async Task<WaveOAuthTokenResult> RefreshAccessTokenAsync(
        string refreshToken,
        string redirectUri,
        CancellationToken cancellationToken = default,
        string? clientId = null,
        string? clientSecret = null)
    {
        var resolvedClientId = FirstNonEmpty(clientId, _options.ClientId);
        var resolvedClientSecret = FirstNonEmpty(clientSecret, _options.ClientSecret);
        if (string.IsNullOrWhiteSpace(resolvedClientId) || string.IsNullOrWhiteSpace(resolvedClientSecret))
            return WaveOAuthTokenResult.Failed("Wave Client ID and Client Secret are required to refresh the login.");
        if (string.IsNullOrWhiteSpace(refreshToken))
            return WaveOAuthTokenResult.Failed("No Wave refresh token is stored. Connect to Wave again.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = resolvedClientId,
            ["client_secret"] = resolvedClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["redirect_uri"] = redirectUri
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return WaveOAuthTokenResult.Failed($"Wave token refresh returned {(int)response.StatusCode}: {body}");

        return ParseTokenResponse(body);
    }

    public async Task<IReadOnlyList<WaveBusinessInfo>> ListBusinessesAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        const string query = """
            query {
              businesses {
                edges {
                  node {
                    id
                    name
                  }
                }
              }
            }
            """;

        var response = await ExecuteGraphQlAsync(new { query }, cancellationToken, accessToken);
        if (!response.Success || response.Data == null)
            return Array.Empty<WaveBusinessInfo>();

        var businesses = new List<WaveBusinessInfo>();
        foreach (var edge in response.Data.Value.GetProperty("businesses").GetProperty("edges").EnumerateArray())
        {
            var node = edge.GetProperty("node");
            businesses.Add(new WaveBusinessInfo(
                node.GetProperty("id").GetString() ?? string.Empty,
                node.GetProperty("name").GetString() ?? string.Empty));
        }

        return businesses;
    }

    private async Task<(string? Footer, string? Memo)> ResolveInvoiceDocumentTextAsync(
        CancellationToken cancellationToken,
        string? accessToken,
        string? businessId)
    {
        var footer = NullIfWhiteSpace(_options.InvoiceFooter);
        var memo = NullIfWhiteSpace(_options.InvoiceMemo);
        if (footer != null && memo != null)
            return (footer, memo);

        var fromWave = await FetchInvoiceDocumentTextFromExistingInvoicesAsync(
            cancellationToken,
            accessToken,
            businessId);
        return (
            footer ?? fromWave.Footer,
            memo ?? fromWave.Memo);
    }

    private async Task<(string? Footer, string? Memo)> FetchInvoiceDocumentTextFromExistingInvoicesAsync(
        CancellationToken cancellationToken,
        string? accessToken,
        string? businessId)
    {
        const string query = """
            query ($businessId: ID!) {
              business(id: $businessId) {
                invoices(page: 1, pageSize: 25, sort: [CREATED_AT_DESC]) {
                  edges {
                    node {
                      footer
                      memo
                    }
                  }
                }
              }
            }
            """;

        var response = await ExecuteGraphQlAsync(
            new { query, variables = new { businessId = ResolveBusinessId(businessId) } },
            cancellationToken,
            accessToken);
        if (!response.Success || response.Data == null)
            return (null, null);

        try
        {
            var footers = new List<string>();
            var memos = new List<string>();
            foreach (var edge in response.Data.Value.GetProperty("business").GetProperty("invoices").GetProperty("edges").EnumerateArray())
            {
                var node = edge.GetProperty("node");
                var footer = UsableInvoiceText(ReadString(node, "footer"));
                var memo = UsableInvoiceMemo(ReadString(node, "memo"));
                if (footer != null)
                    footers.Add(footer);
                if (memo != null)
                    memos.Add(memo);
            }

            return (MostCommonText(footers), MostCommonText(memos));
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? MostCommonText(IReadOnlyList<string> values) =>
        values
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key.Length)
            .Select(g => g.Key)
            .FirstOrDefault();

    private static string? UsableInvoiceMemo(string? memo)
    {
        var text = UsableInvoiceText(memo);
        if (text == null)
            return null;
        if (text.Contains("AdRackHub", StringComparison.OrdinalIgnoreCase))
            return null;
        return text;
    }

    private static string? UsableInvoiceText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool HasApiCredentials(string? accessToken, string? businessId) =>
        !string.IsNullOrWhiteSpace(ResolveAccessToken(accessToken))
        && !string.IsNullOrWhiteSpace(ResolveBusinessId(businessId));

    private string? ResolveAccessToken(string? accessToken) =>
        string.IsNullOrWhiteSpace(accessToken) ? _options.AccessToken : accessToken;

    private string InvoiceFromAddress() =>
        string.IsNullOrWhiteSpace(_options.InvoiceFromAddress)
            ? WaveOptions.DefaultInvoiceFromAddress
            : _options.InvoiceFromAddress.Trim();

    private string InvoiceEmailSubject() =>
        string.IsNullOrWhiteSpace(_options.InvoiceEmailSubject)
            ? WaveOptions.DefaultInvoiceEmailSubject
            : _options.InvoiceEmailSubject;

    private string InvoiceEmailMessage() =>
        string.IsNullOrWhiteSpace(_options.InvoiceEmailMessage)
            ? WaveOptions.DefaultInvoiceEmailMessage
            : _options.InvoiceEmailMessage;

    private static string ApplyInvoiceEmailTemplate(
        string template,
        WaveInvoiceResult invoice,
        string? fallbackCustomerName)
    {
        var businessName = FirstNonEmpty(invoice.BusinessName, "Ad-Rack Services LLC") ?? "Ad-Rack Services LLC";
        var customerName = FirstNonEmpty(invoice.CustomerName, fallbackCustomerName) ?? string.Empty;
        var invoiceNumber = invoice.InvoiceNumber ?? string.Empty;
        var amount = FormatInvoiceAmount(invoice.Amount);
        return template
            .Replace("{{Invoice number}}", invoiceNumber, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Your business name}}", businessName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Client company name}}", customerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Invoice amount}}", amount, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Invoice balance}}", amount, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatInvoiceAmount(string? raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || decimal.TryParse(raw, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out amount))
            return amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        return raw ?? string.Empty;
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static string? ReadNestedString(JsonElement parent, string objectName, string name)
    {
        if (!parent.TryGetProperty(objectName, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;
        return ReadString(nested, name);
    }

    private static string? ReadMoneyValue(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var money) || money.ValueKind != JsonValueKind.Object)
            return null;
        if (!money.TryGetProperty("value", out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private string? ResolveBusinessId(string? businessId) =>
        string.IsNullOrWhiteSpace(businessId) ? _options.BusinessId : businessId;

    private async Task<WaveGraphQlResponse> ExecuteGraphQlAsync(
        object payload,
        CancellationToken cancellationToken,
        string? accessToken = null)
    {
        var token = ResolveAccessToken(accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.GraphQLEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return IsUnauthorizedStatus(response.StatusCode) || IsAuthFailureText(body)
                ? WaveGraphQlResponse.AuthFail()
                : WaveGraphQlResponse.Fail($"Wave API returned {(int)response.StatusCode}: {body}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.Clone();

            if (root.TryGetProperty("errors", out var topLevelErrors) && topLevelErrors.ValueKind == JsonValueKind.Array)
            {
                var authFailed = topLevelErrors.EnumerateArray().Any(IsGraphQlAuthError);
                if (authFailed)
                    return WaveGraphQlResponse.AuthFail();

                var message = string.Join("; ", topLevelErrors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrWhiteSpace(m)));
                return WaveGraphQlResponse.Fail(string.IsNullOrWhiteSpace(message) ? "Wave API request failed." : message);
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

    internal static string ToWaveGraphQlProductId(string productId, string? businessId)
    {
        var trimmed = productId.Trim();
        if (TryDecodeWaveId(trimmed, out var decoded)
            && decoded.Contains("Product:", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var businessUuid = ExtractBusinessUuid(businessId);
        if (string.IsNullOrWhiteSpace(businessUuid))
            return trimmed;

        var raw = $"Business:{businessUuid};Product:{trimmed}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static string? ExtractBusinessUuid(string? businessId)
    {
        if (string.IsNullOrWhiteSpace(businessId))
            return null;

        var value = businessId.Trim();
        if (TryDecodeWaveId(value, out var decoded))
            value = decoded;

        const string prefix = "Business:";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = value[prefix.Length..];
            var separator = rest.IndexOf(';');
            return separator >= 0 ? rest[..separator] : rest;
        }

        return value;
    }

    private static bool TryDecodeWaveId(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = (padded.Length % 4) switch
            {
                2 => padded + "==",
                3 => padded + "=",
                1 => padded,
                _ => padded
            };
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return decoded.StartsWith("Business:", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static WaveOAuthTokenResult ParseTokenResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var token = root.TryGetProperty("access_token", out var accessToken)
                ? accessToken.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(token))
                return WaveOAuthTokenResult.Failed("Wave token response did not include access_token.");

            var refresh = root.TryGetProperty("refresh_token", out var refreshToken)
                ? refreshToken.GetString()
                : null;
            var expiresIn = 0;
            if (root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds))
                expiresIn = seconds;
            var businessId = root.TryGetProperty("businessId", out var business)
                ? business.GetString()
                : null;

            return WaveOAuthTokenResult.Succeeded(token, refresh, expiresIn, businessId);
        }
        catch (Exception ex)
        {
            return WaveOAuthTokenResult.Failed($"Failed to parse Wave token response: {ex.Message}");
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? ToProvinceCode(string? stateCode)
    {
        if (string.IsNullOrWhiteSpace(stateCode))
            return null;

        var normalized = stateCode.Trim().ToUpperInvariant();
        return normalized.Contains('-') ? normalized : $"US-{normalized}";
    }

    private static bool IsUnauthorizedStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    private static bool IsGraphQlAuthError(JsonElement error)
    {
        if (error.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object)
        {
            if (extensions.TryGetProperty("code", out var code))
            {
                var value = code.GetString() ?? string.Empty;
                if (value.Equals("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("UNAUTHORIZED", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return error.TryGetProperty("message", out var message) && IsAuthFailureText(message.GetString());
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

        public static WaveGraphQlResponse AuthFail() =>
            new() { Success = false, ErrorMessage = SessionExpiredReconnectMessage };
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
    public string? ProductId { get; init; }
    public string? ProductName { get; init; }
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
    public string? WaveInvoiceUrl { get; set; }
    public string? InvoiceNumber { get; init; }
    public string? Status { get; init; }
    public string? PdfUrl { get; init; }
    public string? DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? Amount { get; init; }
    public string? BusinessName { get; init; }
    public string? ErrorMessage { get; init; }

    public static WaveInvoiceResult Succeeded(
        string id,
        string? url,
        string? invoiceNumber,
        string? status = null,
        string? pdfUrl = null,
        string? dueDate = null,
        string? customerName = null,
        string? amount = null,
        string? businessName = null) =>
        new()
        {
            Success = true,
            WaveInvoiceId = id,
            WaveInvoiceUrl = url,
            InvoiceNumber = invoiceNumber,
            Status = status,
            PdfUrl = pdfUrl,
            DueDate = dueDate,
            CustomerName = customerName,
            Amount = amount,
            BusinessName = businessName
        };

    public static WaveInvoiceResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class WaveSendResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> Recipients { get; init; } = Array.Empty<string>();
    public string? ErrorMessage { get; init; }

    public static WaveSendResult Succeeded(IReadOnlyList<string> recipients) =>
        new() { Success = true, Recipients = recipients };

    public static WaveSendResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class WaveOAuthTokenResult
{
    public bool Success { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int ExpiresIn { get; init; }
    public string? BusinessId { get; init; }
    public string? ErrorMessage { get; init; }

    public static WaveOAuthTokenResult Succeeded(
        string token,
        string? refreshToken = null,
        int expiresIn = 0,
        string? businessId = null) =>
        new()
        {
            Success = true,
            AccessToken = token,
            RefreshToken = refreshToken,
            ExpiresIn = expiresIn,
            BusinessId = businessId
        };

    public static WaveOAuthTokenResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed record WaveBusinessInfo(string Id, string Name);

public sealed record WaveInvoiceListItem(
    string WaveInvoiceId,
    string? InvoiceNumber,
    string? Status,
    string? CustomerName);
