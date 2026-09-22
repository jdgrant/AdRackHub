namespace AdRackHub.Services;

public class DataForSeoOptions
{
    public const string SectionName = "DataForSEO";

    public string? Login { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}
