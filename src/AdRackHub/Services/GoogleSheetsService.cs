using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Options;

namespace AdRackHub.Services;

public class GoogleSheetsService
{
    private readonly GoogleSheetsOptions _options;
    private readonly IWebHostEnvironment _environment;

    public GoogleSheetsService(IOptions<GoogleSheetsOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IList<IList<object>>> GetSheetValuesAsync(
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("Google Sheets is not configured.");

        var service = await CreateSheetsServiceAsync(cancellationToken);
        var range = $"{QuoteSheetName(sheetName)}!A:Z";
        var request = service.Spreadsheets.Values.Get(_options.SpreadsheetId, range);
        var response = await request.ExecuteAsync(cancellationToken);

        return response.Values ?? new List<IList<object>>();
    }

    public async Task<GoogleSheetsConnectionInfo> GetConnectionInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            return new GoogleSheetsConnectionInfo { IsConfigured = false };

        try
        {
            var service = await CreateSheetsServiceAsync(cancellationToken);
            var spreadsheet = await service.Spreadsheets.Get(_options.SpreadsheetId).ExecuteAsync(cancellationToken);
            return new GoogleSheetsConnectionInfo
            {
                IsConfigured = true,
                SpreadsheetTitle = spreadsheet.Properties?.Title,
                SheetNames = spreadsheet.Sheets?
                    .Select(s => s.Properties?.Title ?? "")
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            return new GoogleSheetsConnectionInfo
            {
                IsConfigured = true,
                ConnectionError = ex.Message
            };
        }
    }

    private async Task<SheetsService> CreateSheetsServiceAsync(CancellationToken cancellationToken)
    {
        var credential = await LoadCredentialAsync(cancellationToken);
        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AdRackHub"
        });
    }

    private async Task<GoogleCredential> LoadCredentialAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceAccountJson))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(_options.ServiceAccountJson));
            return GoogleCredential.FromStream(stream)
                .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
        }

        var path = _options.ServiceAccountJsonPath!;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(_environment.ContentRootPath, path);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Google service account file not found: {path}");

        await using var fileStream = File.OpenRead(path);
        return GoogleCredential.FromStream(fileStream)
            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
    }

    private static string QuoteSheetName(string sheetName) =>
        sheetName.Contains('\'') ? $"''{sheetName.Replace("'", "''")}''" : $"'{sheetName}'";
}

public class GoogleSheetsConnectionInfo
{
    public bool IsConfigured { get; init; }
    public string? SpreadsheetTitle { get; init; }
    public List<string> SheetNames { get; init; } = [];
    public string? ConnectionError { get; init; }
    public bool IsConnected => string.IsNullOrWhiteSpace(ConnectionError);
}
