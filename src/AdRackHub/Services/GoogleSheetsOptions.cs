namespace AdRackHub.Services;

public class GoogleSheetsOptions
{
    public const string SectionName = "GoogleSheets";

    public string? SpreadsheetId { get; set; }
    public string? ServiceAccountJsonPath { get; set; }
    public string? ServiceAccountJson { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SpreadsheetId)
        && (!string.IsNullOrWhiteSpace(ServiceAccountJsonPath) || !string.IsNullOrWhiteSpace(ServiceAccountJson));
}
