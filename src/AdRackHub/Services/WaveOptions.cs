using Microsoft.AspNetCore.Http;

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
    public const string DefaultInvoiceFromAddress = "billing@ad-rack.net";

    public const string DefaultInvoiceEmailSubject =
        "Invoice #{{Invoice number}} from Ad-Rack Services";

    public const string DefaultInvoiceEmailMessage =
        """
        Hi {{Client company name}},

        Here’s Invoice #{{Invoice number}} for the amount of {{Invoice amount}}.

        View and pay this invoice: {{Invoice link}}


        Please note the new payment address of:Ad-Rack Services LLC
        7608 W HWY 146
        STE 104
        Pewee Valley, KY 40056

        If you have any questions, feel free to reach out.

        Thank you,

        {{Your business name}}
        """;

    public string InvoiceFromAddress { get; set; } = DefaultInvoiceFromAddress;
    public string InvoiceEmailSubject { get; set; } = DefaultInvoiceEmailSubject;
    public string InvoiceEmailMessage { get; set; } = DefaultInvoiceEmailMessage;
    public string InvoiceFooter { get; set; } = string.Empty;
    public string InvoiceMemo { get; set; } = string.Empty;

    public const string DefaultCompanyName = "Ad-Rack Services LLC";
    public const string DefaultCompanyAddress1 = "7608 W HWY 146";
    public const string DefaultCompanyAddress2 = "STE 104";
    public const string DefaultCompanyCityStateZip = "Pewee Valley, KY 40056";
    public const string DefaultCompanyCountry = "United States";
    public const string DefaultCompanyPhone = "(502) 253-5454";
    public const string DefaultCompanyWebsite = "www.ad-rack.com";
    public const string DefaultTermsUrl = "https://ad-rack.com/terms/";
    public const string DefaultAddressNotice = "PLEASE NOTE NEW PAYMENT INFORMATION";
    public const string DefaultInvoiceTerms =
        "Payment of this invoice constitutes acceptance of and agreement to Terms and Conditions. Please note new payment information below.";
    public const string DefaultContractTerms =
        "Signing this contract constitutes acceptance of and agreement to Terms and Conditions.";

    public const string PaymentChangeNotice =
        "Please note: Our mailing address and ACH payment account have changed. To avoid delays, please update any payment information you have saved and use the details below for future payments.";

    public const string AchAccountName = "Ad-Rack Services LLC";
    public const string AchRoutingNumber = "125109019";
    public const string AchAccountNumber = "875113400472";
    public const string PaperlessEmail = "billing@ad-rack.net";

    public string InvoiceCompanyName { get; set; } = DefaultCompanyName;
    public string InvoiceCompanyAddress1 { get; set; } = DefaultCompanyAddress1;
    public string InvoiceCompanyAddress2 { get; set; } = DefaultCompanyAddress2;
    public string InvoiceCompanyCityStateZip { get; set; } = DefaultCompanyCityStateZip;
    public string InvoiceCompanyCountry { get; set; } = DefaultCompanyCountry;
    public string InvoiceCompanyPhone { get; set; } = DefaultCompanyPhone;
    public string InvoiceCompanyWebsite { get; set; } = DefaultCompanyWebsite;
    public string InvoiceAddressNotice { get; set; } = DefaultAddressNotice;
    public string InvoiceTerms { get; set; } = DefaultInvoiceTerms;

    /// <summary>Wave Money in Transit account for received invoices. Never a real bank.</summary>
    public string? PaymentAccountId { get; set; }

    /// <summary>Ignored for payment posting. Kept so old configs do not silently retarget a bank.</summary>
    public string PaymentAccountLast4 { get; set; } = "";

    public string PaymentAccountHint { get; set; } = WaveApiService.ReceivedInvoicesAccountName;

    public string PaymentMethod { get; set; } = "BANK_TRANSFER";

    /// <summary>Public site origin for Wave OAuth (e.g. https://hub.ad-rack.net). Empty uses the current request.</summary>
    public string? PublicBaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(BusinessId);

    public bool IsOAuthConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public string OAuthRedirectUri(HttpRequest? request)
    {
        var configured = (PublicBaseUrl ?? "").Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(configured))
            return $"{configured}/Admin/WaveOAuthCallback";

        if (request == null)
            return "/Admin/WaveOAuthCallback";

        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        if (scheme.Contains(',', StringComparison.Ordinal))
            scheme = scheme.Split(',')[0].Trim();
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.ToString();
        if (host.Contains(',', StringComparison.Ordinal))
            host = host.Split(',')[0].Trim();
        return $"{scheme}://{host}/Admin/WaveOAuthCallback";
    }
}
