namespace AdRackHub.Services;

public class WaveOptions
{
    public const string SectionName = "Wave";

    public string? AccessToken { get; set; }
    public string? BusinessId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string GraphQLEndpoint { get; set; } = "https://gql.waveapps.com/graphql/public";
    public string AuthorizeUrl { get; set; } = "https://api.waveapps.com/oauth2/authorize/";
    public string TokenUrl { get; set; } = "https://api.waveapps.com/oauth2/token/";
    public string DefaultCurrency { get; set; } = "USD";
    public string DefaultCountryCode { get; set; } = "US";
    public const string DefaultInvoiceFromAddress = "invoices@ad-rack.net";

    public const string DefaultInvoiceEmailSubject =
        "Invoice #{{Invoice number}} from {{Your business name}}";

    public const string DefaultInvoiceEmailMessage =
        """
        Hi {{Client company name}},

        Here's Invoice #{{Invoice number}} for the amount of {{Invoice amount}}.

        Please note the new payment address of:Ad-Rack Services LLC
        7608 KY-146
        STE 104
        Pewee Valley, Kentucky 40056

        If you have any questions, feel free to reach out.
        """;

    public string InvoiceFromAddress { get; set; } = DefaultInvoiceFromAddress;
    public string InvoiceEmailSubject { get; set; } = DefaultInvoiceEmailSubject;
    public string InvoiceEmailMessage { get; set; } = DefaultInvoiceEmailMessage;
    public string InvoiceFooter { get; set; } = string.Empty;
    public string InvoiceMemo { get; set; } = string.Empty;

    public const string DefaultCompanyName = "Ad-Rack Services LLC";
    public const string DefaultCompanyAddress1 = "7608 KY-146";
    public const string DefaultCompanyAddress2 = "STE 104";
    public const string DefaultCompanyCityStateZip = "Pewee Valley, Kentucky 40056";
    public const string DefaultCompanyCountry = "United States";
    public const string DefaultCompanyPhone = "(502) 253-5454";
    public const string DefaultCompanyWebsite = "www.ad-rack.com";
    public const string DefaultAddressNotice = "PLEASE NOTE THE NEW ADDRESS";
    public const string DefaultInvoiceTerms =
        """
        Payment of this invoice constitutes acceptance of and agreement to Terms and Conditions.

        Please mail payment to:
        Ad-Rack Services LLC
        7608 KY-146
        STE 104
        Pewee Valley, Kentucky 40056
        """;

    public string InvoiceCompanyName { get; set; } = DefaultCompanyName;
    public string InvoiceCompanyAddress1 { get; set; } = DefaultCompanyAddress1;
    public string InvoiceCompanyAddress2 { get; set; } = DefaultCompanyAddress2;
    public string InvoiceCompanyCityStateZip { get; set; } = DefaultCompanyCityStateZip;
    public string InvoiceCompanyCountry { get; set; } = DefaultCompanyCountry;
    public string InvoiceCompanyPhone { get; set; } = DefaultCompanyPhone;
    public string InvoiceCompanyWebsite { get; set; } = DefaultCompanyWebsite;
    public string InvoiceAddressNotice { get; set; } = DefaultAddressNotice;
    public string InvoiceTerms { get; set; } = DefaultInvoiceTerms;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(BusinessId);

    public bool IsOAuthConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
