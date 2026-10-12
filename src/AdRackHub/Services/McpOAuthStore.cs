using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdRackHub.Services;

public sealed class McpOAuthStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly ILogger<McpOAuthStore> _logger;
    private readonly object _gate = new();
    private StoreFile _state;

    public McpOAuthStore(string? path, ILogger<McpOAuthStore> logger)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : path;
        _logger = logger;
        _state = Load();
    }

    public void SaveClient(McpRegisteredClient client)
    {
        lock (_gate)
        {
            _state.Clients.RemoveAll(c => c.ClientId == client.ClientId);
            _state.Clients.Add(client);
            Persist();
        }
    }

    public McpRegisteredClient? FindClient(string clientId)
    {
        lock (_gate)
        {
            return _state.Clients.FirstOrDefault(c => c.ClientId == clientId);
        }
    }

    public string SaveAuthorizationCode(McpAuthorizationCode code)
    {
        var raw = NewSecret();
        code.CodeHash = Hash(raw);
        lock (_gate)
        {
            Prune();
            _state.Codes.Add(code);
            Persist();
        }
        return raw;
    }

    public McpAuthorizationCode? TakeAuthorizationCode(string rawCode)
    {
        var hash = Hash(rawCode);
        lock (_gate)
        {
            var index = _state.Codes.FindIndex(c => FixedEquals(c.CodeHash, hash));
            if (index < 0)
                return null;
            var code = _state.Codes[index];
            _state.Codes.RemoveAt(index);
            Persist();
            return code.ExpiresAt > DateTimeOffset.UtcNow ? code : null;
        }
    }

    public string SaveAccessToken(McpAccessToken token)
    {
        var raw = NewSecret();
        token.TokenHash = Hash(raw);
        lock (_gate)
        {
            Prune();
            _state.AccessTokens.Add(token);
            Persist();
        }
        return raw;
    }

    public McpAccessToken? FindAccessToken(string rawToken)
    {
        var hash = Hash(rawToken);
        lock (_gate)
        {
            Prune();
            return _state.AccessTokens.FirstOrDefault(t =>
                FixedEquals(t.TokenHash, hash) && t.ExpiresAt > DateTimeOffset.UtcNow);
        }
    }

    public string SaveRefreshToken(McpRefreshToken token)
    {
        var raw = NewSecret();
        token.TokenHash = Hash(raw);
        lock (_gate)
        {
            Prune();
            _state.RefreshTokens.Add(token);
            Persist();
        }
        return raw;
    }

    public McpRefreshToken? TakeRefreshToken(string rawToken)
    {
        var hash = Hash(rawToken);
        lock (_gate)
        {
            var index = _state.RefreshTokens.FindIndex(t => FixedEquals(t.TokenHash, hash));
            if (index < 0)
                return null;
            var token = _state.RefreshTokens[index];
            _state.RefreshTokens.RemoveAt(index);
            Persist();
            return token.ExpiresAt > DateTimeOffset.UtcNow ? token : null;
        }
    }

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool PkceMatches(string verifier, string challenge)
    {
        if (string.IsNullOrEmpty(verifier) || verifier.Length is < 43 or > 128)
            return false;
        if (verifier.Any(c => c > 127))
            return false;

        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return FixedEquals(computed, challenge);
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.ASCII.GetBytes(left);
        var b = Encoding.ASCII.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        _state.Codes.RemoveAll(c => c.ExpiresAt <= now);
        _state.AccessTokens.RemoveAll(t => t.ExpiresAt <= now);
        _state.RefreshTokens.RemoveAll(t => t.ExpiresAt <= now);
    }

    private StoreFile Load()
    {
        if (_path == null || !File.Exists(_path))
            return new StoreFile();

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<StoreFile>(json) ?? new StoreFile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP OAuth store could not be read. Starting with an empty store.");
            return new StoreFile();
        }
    }

    private void Persist()
    {
        if (_path == null)
            return;

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private sealed class StoreFile
    {
        public List<McpRegisteredClient> Clients { get; set; } = new();
        public List<McpAuthorizationCode> Codes { get; set; } = new();
        public List<McpAccessToken> AccessTokens { get; set; } = new();
        public List<McpRefreshToken> RefreshTokens { get; set; } = new();
    }
}

public sealed class McpRegisteredClient
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = "ChatGPT";
    public List<string> RedirectUris { get; set; } = new();
}

public sealed class McpAuthorizationCode
{
    public string CodeHash { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class McpAccessToken
{
    public string TokenHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class McpRefreshToken
{
    public string TokenHash { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
