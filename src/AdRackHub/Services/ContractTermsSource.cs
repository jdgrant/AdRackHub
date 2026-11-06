using System.Net;
using System.Text.RegularExpressions;

namespace AdRackHub.Services;

public class ContractTermsSource
{
    public const string SourceUrl = WaveOptions.DefaultTermsUrl;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? Cached;
    private static DateTime CachedAtUtc;
    private readonly IHttpClientFactory _http;

    public ContractTermsSource(IHttpClientFactory http)
    {
        _http = http;
    }

    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        if (Fresh(Cached, CachedAtUtc))
            return Cached!;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh(Cached, CachedAtUtc))
                return Cached!;

            var text = await FetchAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                Cached = text;
                CachedAtUtc = DateTime.UtcNow;
                return text;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        finally
        {
            Gate.Release();
        }

        return Cached ?? Fallback;
    }

    private async Task<string?> FetchAsync(CancellationToken cancellationToken)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);
        using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
        request.Headers.UserAgent.ParseAdd("AdRackHub/1.0");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return Extract(html);
    }

    internal static string? Extract(string html)
    {
        var marker = html.IndexOf("entry-content", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return null;
        var open = html.IndexOf('>', marker);
        if (open < 0)
            return null;
        var close = html.IndexOf("</div>", open, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
            return null;

        var fragment = html[(open + 1)..close];
        fragment = Regex.Replace(fragment, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        fragment = Regex.Replace(fragment, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
        fragment = Regex.Replace(fragment, "<[^>]+>", string.Empty);
        fragment = WebUtility.HtmlDecode(fragment).Replace('\u00a0', ' ');

        var lines = fragment.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();
        var compact = new List<string>();
        var blank = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (blank || compact.Count == 0)
                    continue;
                blank = true;
                compact.Add(string.Empty);
                continue;
            }

            blank = false;
            compact.Add(line);
        }

        while (compact.Count > 0 && compact[^1].Length == 0)
            compact.RemoveAt(compact.Count - 1);

        if (compact.Count == 0)
            return null;

        return "TERMS AND CONDITIONS\n\n" + string.Join('\n', compact);
    }

    private static bool Fresh(string? text, DateTime cachedAtUtc) =>
        !string.IsNullOrWhiteSpace(text) && DateTime.UtcNow - cachedAtUtc < TimeSpan.FromHours(6);

    internal const string Fallback =
        """
        TERMS AND CONDITIONS

        Payment of this invoice constitutes acceptance of and agreement to these Terms and Conditions. Ad-Rack Services LLC agrees to distribute the customer’s brochures in the service areas and for the service period shown on this invoice.

        At the end of the stated service period, brochures may remain in the display racks until Ad-Rack can reasonably remove them or while renewal arrangements are being completed.

        The customer is responsible for supplying enough brochures to stock the display racks for the duration of the service period. Inventory information is available upon request. Brochures remain the customer’s property and are not insured.

        Kentucky rest-area distribution (if applicable) is contingent upon Ad-Rack maintaining authorization from the Commonwealth of Kentucky. If that authorization ends, Ad-Rack may provide substitute distribution, a credit, or a prorated refund for services not provided.

        The customer may not assign this agreement without Ad-Rack’s written consent. Ad-Rack may assign this agreement without the customer’s consent. The customer is responsible for reasonable collection costs and attorney fees resulting from payment default, to the extent permitted by law. Kentucky law governs, and any dispute must be brought or conducted in Oldham County, Kentucky.

        SHIP BROCHURES PREPAID, INCLUDING PREPAYMENT FOR INSIDE GROUND-LEVEL DELIVERY AND LIFT-GATE SERVICE IF NEEDED, TO:

        AD-RACK
        C/O PUBLIC STORAGE, UNIT A-44
        3520 CHAMBERLAIN LANE
        LOUISVILLE, KY 40241

        MARK FREIGHT BILL: “INSIDE GROUND-LEVEL DELIVERY REQUIRED”

        A maximum of three pallets may be shipped at one time. Replenishment of these quantities should be made upon request by Ad-Rack.
        """;
}
