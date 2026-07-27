namespace AdRackHub.Services;

public class WaveOptions
{
    public const string SectionName = "Wave";

    public string? AccessToken { get; set; }
    public string? BusinessId { get; set; }
    public string GraphQLEndpoint { get; set; } = "https://gql.waveapps.com/graphql/public";
    public string DefaultCurrency { get; set; } = "USD";
    public string DefaultCountryCode { get; set; } = "US";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(BusinessId);
}
