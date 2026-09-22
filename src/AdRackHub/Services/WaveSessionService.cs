using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public sealed class WaveSessionCredentials
{
    public string AccessToken { get; init; } = string.Empty;
    public string? BusinessId { get; init; }
    public string? BusinessName { get; init; }
}

public class WaveSessionService
{
    private readonly WavePocTokenStore _tokens;
    private readonly WaveApiService _waveApi;
    private readonly WaveOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WaveSessionService(
        WavePocTokenStore tokens,
        WaveApiService waveApi,
        IOptions<WaveOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        _tokens = tokens;
        _waveApi = waveApi;
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public const string SessionExpiredReconnectMessage = WaveApiService.SessionExpiredReconnectMessage;

    public bool IsReady
    {
        get
        {
            var stored = _tokens.Load();
            if (stored is { HasAccessToken: true } && !string.IsNullOrWhiteSpace(BusinessIdOf(stored)))
                return true;
            return _options.IsConfigured;
        }
    }

    public bool NeedsBusinessReset
    {
        get
        {
            var stored = _tokens.Load();
            if (stored == null || !stored.HasAccessToken)
                return false;
            if (string.IsNullOrWhiteSpace(BusinessIdOf(stored)))
                return true;
            return stored.ExpiresAtUtc is { } expires && expires <= DateTimeOffset.UtcNow;
        }
    }

    public static bool IsSessionExpiredMessage(string? message) =>
        WaveApiService.IsSessionExpiredMessage(message);

    public async Task<WaveSessionCredentials?> GetCredentialsAsync(
        CancellationToken cancellationToken = default,
        string? clientId = null,
        string? clientSecret = null,
        string? redirectUri = null)
    {
        var stored = _tokens.Load();
        if (stored != null && stored.HasAccessToken && stored.IsExpiring)
        {
            if (string.IsNullOrWhiteSpace(stored.RefreshToken))
                return null;

            var refreshed = await _waveApi.RefreshAccessTokenAsync(
                stored.RefreshToken,
                FirstNonEmpty(redirectUri, stored.RedirectUri, CurrentRedirectUri()) ?? string.Empty,
                cancellationToken,
                clientId,
                clientSecret);
            if (!refreshed.Success || string.IsNullOrWhiteSpace(refreshed.AccessToken))
            {
                if (stored.ExpiresAtUtc is { } expires && expires <= DateTimeOffset.UtcNow)
                    return null;
            }
            else
            {
                var expiresIn = refreshed.ExpiresIn > 0 ? refreshed.ExpiresIn : 90 * 60;
                stored = new WavePocStoredConnection
                {
                    AccessToken = refreshed.AccessToken,
                    RefreshToken = refreshed.RefreshToken ?? stored.RefreshToken,
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                    BusinessId = FirstNonEmpty(stored.BusinessId, refreshed.BusinessId),
                    BusinessName = stored.BusinessName,
                    RedirectUri = FirstNonEmpty(stored.RedirectUri, redirectUri, CurrentRedirectUri())
                };
                _tokens.Save(stored);
            }
        }

        if (stored is { HasAccessToken: true }
            && (stored.ExpiresAtUtc is null || stored.ExpiresAtUtc > DateTimeOffset.UtcNow))
        {
            return new WaveSessionCredentials
            {
                AccessToken = stored.AccessToken!,
                BusinessId = BusinessIdOf(stored),
                BusinessName = stored.BusinessName
            };
        }

        if (stored is { HasAccessToken: true })
            return null;

        if (!_options.IsConfigured)
            return null;

        return new WaveSessionCredentials
        {
            AccessToken = _options.AccessToken!,
            BusinessId = _options.BusinessId,
            BusinessName = null
        };
    }

    private string? BusinessIdOf(WavePocStoredConnection stored) =>
        FirstNonEmpty(stored.BusinessId, _options.BusinessId);

    private string? CurrentRedirectUri()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
            return null;

        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        if (scheme.Contains(',', StringComparison.Ordinal))
            scheme = scheme.Split(',')[0].Trim();
        return $"{scheme}://{request.Host}/Admin/WaveOAuthCallback";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
