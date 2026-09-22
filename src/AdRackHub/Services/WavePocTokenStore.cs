using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace AdRackHub.Services;

public class WavePocStoredConnection
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? BusinessId { get; set; }
    public string? BusinessName { get; set; }
    public string? RedirectUri { get; set; }

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);

    public bool IsExpiring =>
        ExpiresAtUtc is { } expires && expires <= DateTimeOffset.UtcNow.AddMinutes(2);
}

public class WavePocTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _gate = new();

    public WavePocTokenStore(IWebHostEnvironment env)
    {
        var folder = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "wave-poc-oauth.json");
    }

    public WavePocStoredConnection? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return null;
            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<WavePocStoredConnection>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }
    }

    public void Save(WavePocStoredConnection connection)
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(connection, JsonOptions);
            File.WriteAllText(_path, json);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }
}
