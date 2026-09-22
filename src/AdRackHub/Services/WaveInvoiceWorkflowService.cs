using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.Extensions.Logging;

namespace AdRackHub.Services;

public sealed class WaveInvoiceWorkflowResult
{
    public bool Success { get; init; }
    public bool InvoiceApproved { get; init; }
    public bool PdfSaved { get; init; }
    public bool SessionExpired { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? WaveCustomerId { get; init; }
    public WaveInvoiceResult? Invoice { get; init; }
}

public class WaveInvoiceWorkflowService
{
    private readonly ApplicationDbContext _context;
    private readonly WaveApiService _waveApi;
    private readonly WaveSessionService _session;
    private readonly InvoicePdfStorageService _invoicePdfs;
    private readonly InvoicePdfGenerator _invoicePdf;
    private readonly ILogger<WaveInvoiceWorkflowService> _logger;

    public WaveInvoiceWorkflowService(
        ApplicationDbContext context,
        WaveApiService waveApi,
        WaveSessionService session,
        InvoicePdfStorageService invoicePdfs,
        InvoicePdfGenerator invoicePdf,
        ILogger<WaveInvoiceWorkflowService> logger)
    {
        _context = context;
        _waveApi = waveApi;
        _session = session;
        _invoicePdfs = invoicePdfs;
        _invoicePdf = invoicePdf;
        _logger = logger;
    }

    public async Task<WaveInvoiceWorkflowResult> ProcessContractAsync(
        CustomerContract contract,
        DateOnly invoiceDate,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var missingRoutes = contract.ContractRoutes
            .Where(cr => string.IsNullOrWhiteSpace(cr.Route.WaveProductId))
            .Select(cr => cr.Route.RouteName)
            .Distinct()
            .ToList();
        if (missingRoutes.Count > 0)
        {
            return Fail("Set Wave Product ID on these routes first: " + string.Join(", ", missingRoutes));
        }

        var customer = contract.Customer;
        var months = AnnualBillingHelper.BillingMonths(contract);
        var lineItems = contract.ContractRoutes
            .OrderBy(cr => cr.Route.RouteName)
            .Select(cr =>
            {
                var amount = AnnualBillingHelper.GetBillingAmount(cr);
                var content = InvoiceLineFormatter.Build(cr.Route.RouteName, invoiceDate, months, amount, customer, cr.Route);
                return new WaveInvoiceLineItem
                {
                    ProductId = cr.Route.WaveProductId,
                    ProductName = RouteNaming.DisplayName(cr.Route.RouteName),
                    Description = content.Description,
                    Quantity = 1,
                    UnitPrice = amount
                };
            })
            .ToList();

        if (lineItems.Count == 0)
            return Fail("This contract has no routes, so no invoice lines were created.");

        return await ProcessAsync(
            customer,
            new[] { contract },
            lineItems,
            invoiceDate,
            memo,
            cancellationToken);
    }

    public async Task<WaveInvoiceWorkflowResult> ProcessAsync(
        Customer customer,
        IReadOnlyList<CustomerContract> persistOnContracts,
        IReadOnlyList<WaveInvoiceLineItem> lineItems,
        DateOnly invoiceDate,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _session.GetCredentialsAsync(cancellationToken);
        if (credentials == null
            || string.IsNullOrWhiteSpace(credentials.AccessToken)
            || string.IsNullOrWhiteSpace(credentials.BusinessId))
        {
            return Fail(_session.NeedsBusinessReset
                ? WaveSessionService.SessionExpiredReconnectMessage
                : "Connect to Wave on the Admin Wave proof page before sending invoices.");
        }

        if (lineItems.Count == 0)
            return Fail("No invoice lines were created.");

        var missingProducts = lineItems
            .Where(l => string.IsNullOrWhiteSpace(l.ProductId))
            .Select(l => l.Description)
            .ToList();
        if (missingProducts.Count > 0)
            return Fail("Set Wave Product ID on the billed routes before sending.");

        var (waveCustomerId, customerError) = await EnsureCustomerAsync(
            customer,
            credentials.AccessToken,
            credentials.BusinessId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(waveCustomerId))
            return Fail(customerError ?? "Wave customerCreate failed.");

        var invoice = await _waveApi.CreateInvoiceAsync(
            waveCustomerId,
            invoiceDate,
            lineItems,
            memo,
            cancellationToken,
            credentials.AccessToken,
            credentials.BusinessId);
        if (!invoice.Success || string.IsNullOrWhiteSpace(invoice.WaveInvoiceId))
            return Fail(invoice.ErrorMessage ?? "Wave invoiceCreate failed.", waveCustomerId);

        var approved = await _waveApi.ApproveInvoiceAsync(
            invoice.WaveInvoiceId,
            cancellationToken,
            credentials.AccessToken);
        if (!approved.Success)
        {
            return new WaveInvoiceWorkflowResult
            {
                Success = false,
                InvoiceApproved = false,
                SessionExpired = WaveSessionService.IsSessionExpiredMessage(approved.ErrorMessage),
                WaveCustomerId = waveCustomerId,
                Invoice = invoice,
                Message = WaveSessionService.IsSessionExpiredMessage(approved.ErrorMessage)
                    ? WaveSessionService.SessionExpiredReconnectMessage
                    : "Created a Wave DRAFT invoice, but approval failed: "
                        + (approved.ErrorMessage ?? "Unknown error")
            };
        }

        approved = await EnsureInvoicePdfUrlAsync(
            approved,
            credentials.AccessToken,
            credentials.BusinessId,
            cancellationToken);

        var pdfSaved = await PersistOnContractsAsync(
            customer,
            persistOnContracts,
            lineItems,
            invoiceDate,
            approved,
            credentials.AccessToken,
            credentials.BusinessId,
            cancellationToken);

        var sendMessage = await SendIfEmailAsync(
            customer,
            approved.WaveInvoiceId ?? invoice.WaveInvoiceId,
            credentials.AccessToken,
            credentials.BusinessId,
            cancellationToken);

        var invoiceNumber = approved.InvoiceNumber ?? invoice.InvoiceNumber;
        var pdfName = pdfSaved ? InvoicePdfStorageService.BuildFileName(invoiceNumber) : null;
        var message =
            $"Created and approved Wave invoice{(string.IsNullOrWhiteSpace(invoiceNumber) ? "" : $" {invoiceNumber}")}."
            + (pdfSaved ? $" Saved PDF {pdfName}." : " Wave did not return a PDF yet.")
            + " "
            + sendMessage;

        _logger.LogInformation(
            "Wave invoice {InvoiceNumber} created for customer {CustomerId}; pdf {PdfSaved}; {SendMessage}",
            invoiceNumber ?? approved.WaveInvoiceId,
            customer.Id,
            pdfSaved,
            sendMessage);

        return new WaveInvoiceWorkflowResult
        {
            Success = true,
            InvoiceApproved = true,
            PdfSaved = pdfSaved,
            WaveCustomerId = waveCustomerId,
            Invoice = approved,
            Message = message.Trim()
        };
    }

    public async Task<WaveInvoiceWorkflowResult> ApprovePersistAndSendAsync(
        CustomerContract contract,
        string waveInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _session.GetCredentialsAsync(cancellationToken);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            return Fail(_session.NeedsBusinessReset
                ? WaveSessionService.SessionExpiredReconnectMessage
                : "Connect to Wave before approving an invoice.");
        }

        var approved = await _waveApi.ApproveInvoiceAsync(waveInvoiceId, cancellationToken, credentials.AccessToken);
        if (!approved.Success)
        {
            return Fail(approved.ErrorMessage ?? "Wave invoiceApprove failed.");
        }

        approved = await EnsureInvoicePdfUrlAsync(
            approved,
            credentials.AccessToken,
            credentials.BusinessId,
            cancellationToken);

        var invoiceDate = contract.NextBillDate;
        var months = AnnualBillingHelper.BillingMonths(contract);
        var lineItems = contract.ContractRoutes
            .OrderBy(cr => cr.Route.RouteName)
            .Select(cr =>
            {
                var amount = AnnualBillingHelper.GetBillingAmount(cr);
                var content = InvoiceLineFormatter.Build(
                    cr.Route.RouteName,
                    invoiceDate,
                    months,
                    amount,
                    contract.Customer,
                    cr.Route);
                return new WaveInvoiceLineItem
                {
                    ProductId = cr.Route.WaveProductId,
                    ProductName = RouteNaming.DisplayName(cr.Route.RouteName),
                    Description = content.Description,
                    Quantity = 1,
                    UnitPrice = amount
                };
            })
            .ToList();

        var pdfSaved = await PersistOnContractsAsync(
            contract.Customer,
            new[] { contract },
            lineItems,
            invoiceDate,
            approved,
            credentials.AccessToken,
            credentials.BusinessId,
            cancellationToken);
        var sendMessage = await SendIfEmailAsync(
            contract.Customer,
            approved.WaveInvoiceId ?? waveInvoiceId,
            credentials.AccessToken,
            credentials.BusinessId,
            cancellationToken);

        return new WaveInvoiceWorkflowResult
        {
            Success = true,
            InvoiceApproved = true,
            PdfSaved = pdfSaved,
            Invoice = approved,
            WaveCustomerId = contract.Customer.WaveCustomerId,
            Message =
                $"Approved the Wave invoice{(string.IsNullOrWhiteSpace(approved.Status) ? "" : $" ({approved.Status})")}. {sendMessage}"
        };
    }

    private async Task<(string? WaveCustomerId, string? ErrorMessage)> EnsureCustomerAsync(
        Customer customer,
        string accessToken,
        string businessId,
        CancellationToken cancellationToken)
    {
        if (WaveApiService.IsLikelyWaveCustomerId(customer.WaveCustomerId))
            return (customer.WaveCustomerId, null);

        var contact = customer.Contacts
            .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .FirstOrDefault();

        var created = await _waveApi.CreateCustomerAsync(new WaveCustomerRequest
        {
            Name = customer.CustomerName,
            FirstName = contact?.FirstName,
            LastName = contact?.LastName,
            Email = FirstNonEmpty(contact?.Email, customer.Email),
            AddressLine1 = FirstNonEmpty(contact?.Address, customer.Address),
            City = FirstNonEmpty(contact?.City, customer.City),
            StateCode = FirstNonEmpty(contact?.State, customer.State),
            PostalCode = FirstNonEmpty(contact?.Zip, customer.Zip)
        }, cancellationToken, accessToken, businessId);

        if (!created.Success || string.IsNullOrWhiteSpace(created.WaveCustomerId))
            return (null, created.ErrorMessage ?? "Wave customerCreate failed.");

        customer.WaveCustomerId = created.WaveCustomerId;
        await _context.SaveChangesAsync(cancellationToken);
        return (created.WaveCustomerId, null);
    }

    private async Task<bool> PersistOnContractsAsync(
        Customer customer,
        IReadOnlyList<CustomerContract> contracts,
        IReadOnlyList<WaveInvoiceLineItem> lineItems,
        DateOnly invoiceDate,
        WaveInvoiceResult invoice,
        string? accessToken,
        string? businessId,
        CancellationToken cancellationToken)
    {
        if (contracts.Count == 0)
            return false;

        var dueDate = invoiceDate.AddDays(WaveApiService.InvoiceDueDays);
        if (!string.IsNullOrWhiteSpace(invoice.DueDate) && DateOnly.TryParse(invoice.DueDate, out var parsedDue))
            dueDate = parsedDue;

        string? payUrl = invoice.WaveInvoiceUrl;
        byte[]? bytes = TryGeneratePdf(customer, lineItems, invoice.InvoiceNumber, invoiceDate, dueDate, payUrl);

        if (!string.IsNullOrWhiteSpace(invoice.WaveInvoiceId) && !WaveApiService.IsWaveShortPayUrl(payUrl))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                var resolved = await _waveApi.ResolvePayUrlAsync(
                    invoice.WaveInvoiceId,
                    timeout.Token,
                    accessToken,
                    businessId,
                    invoice.WaveInvoiceUrl,
                    invoice.PdfUrl);
                if (WaveApiService.IsWaveShortPayUrl(resolved)
                    && !string.Equals(resolved, payUrl, StringComparison.OrdinalIgnoreCase))
                {
                    payUrl = resolved;
                    var withShortUrl = TryGeneratePdf(customer, lineItems, invoice.InvoiceNumber, invoiceDate, dueDate, payUrl);
                    if (withShortUrl is { Length: > 0 })
                        bytes = withShortUrl;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Timed out resolving Wave short pay URL for invoice {InvoiceId}; saved the Ad-Rack PDF with the GraphQL view URL.",
                    invoice.WaveInvoiceId);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(
                    ex,
                    "Could not resolve Wave short pay URL for invoice {InvoiceId}; saved the Ad-Rack PDF anyway.",
                    invoice.WaveInvoiceId);
            }
        }

        if (!string.IsNullOrWhiteSpace(payUrl))
            invoice.WaveInvoiceUrl = payUrl;

        string? storedName = null;
        var savedPdf = false;
        var replaced = new List<(string? Previous, string Current)>();
        foreach (var contract in contracts)
        {
            if (!string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
                contract.WaveInvoiceNumber = invoice.InvoiceNumber.Trim();
            if (!string.IsNullOrWhiteSpace(invoice.WaveInvoiceId))
                contract.WaveInvoiceId = invoice.WaveInvoiceId.Trim();

            if (bytes is { Length: > 0 })
            {
                var previous = contract.WaveInvoicePdfPath;
                storedName = _invoicePdfs.Save(
                    contract.Id,
                    invoice.InvoiceNumber,
                    bytes,
                    previous);
                contract.WaveInvoicePdfPath = storedName;
                replaced.Add((previous, storedName));
                savedPdf = true;
            }
            else
            {
                _logger.LogWarning(
                    "Ad-Rack invoice PDF was not generated for Wave invoice {InvoiceNumber} / contract {ContractId}.",
                    invoice.InvoiceNumber,
                    contract.Id);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        foreach (var (previous, current) in replaced)
            _invoicePdfs.DeleteReplaced(previous, current);
        return savedPdf;
    }

    private byte[]? TryGeneratePdf(
        Customer customer,
        IReadOnlyList<WaveInvoiceLineItem> lineItems,
        string? invoiceNumber,
        DateOnly invoiceDate,
        DateOnly dueDate,
        string? payUrl)
    {
        try
        {
            return _invoicePdf.Generate(_invoicePdf.Build(
                customer,
                lineItems,
                invoiceNumber,
                invoiceDate,
                dueDate,
                payUrl));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ad-Rack invoice PDF generation failed for invoice {InvoiceNumber}.", invoiceNumber);
            return null;
        }
    }

    private async Task<string> SendIfEmailAsync(
        Customer customer,
        string waveInvoiceId,
        string? accessToken,
        string? businessId,
        CancellationToken cancellationToken)
    {
        if (!customer.InvoiceReceiptMethod.IncludesEmail())
            return "Invoice receipt method is Mail, so the invoice was not emailed.";

        var recipients = BillingContactHelper.ContactsWithEmail(customer.Contacts);
        var emails = recipients.Select(c => c.Email!.Trim()).ToList();
        if (emails.Count == 0)
            return "No contacts have an email, so the invoice was not emailed.";

        var sent = await _waveApi.SendInvoiceAsync(
            waveInvoiceId,
            emails,
            cancellationToken,
            accessToken,
            businessId,
            customer.CustomerName);
        if (!sent.Success)
            return "Wave invoice email failed: "
                + (sent.ErrorMessage ?? "Unknown error")
                + ". Disconnect and Connect to Wave again if this is a permission error.";

        return "Emailed to " + string.Join(", ", sent.Recipients) + ".";
    }

    private async Task<WaveInvoiceResult> EnsureInvoicePdfUrlAsync(
        WaveInvoiceResult invoice,
        string? accessToken,
        string? businessId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoice.WaveInvoiceId))
            return invoice;

        var fetched = await _waveApi.GetInvoiceAsync(invoice.WaveInvoiceId, cancellationToken, accessToken, businessId);
        if (!fetched.Success)
            return invoice;

        if (string.IsNullOrWhiteSpace(fetched.InvoiceNumber) && !string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            return WaveInvoiceResult.Succeeded(
                fetched.WaveInvoiceId ?? invoice.WaveInvoiceId!,
                fetched.WaveInvoiceUrl ?? invoice.WaveInvoiceUrl,
                invoice.InvoiceNumber,
                fetched.Status ?? invoice.Status,
                fetched.PdfUrl ?? invoice.PdfUrl,
                fetched.DueDate ?? invoice.DueDate,
                fetched.CustomerName ?? invoice.CustomerName,
                fetched.Amount ?? invoice.Amount,
                fetched.BusinessName ?? invoice.BusinessName);
        }

        return fetched;
    }

    private static WaveInvoiceWorkflowResult Fail(string message, string? waveCustomerId = null) =>
        new()
        {
            Success = false,
            SessionExpired = WaveSessionService.IsSessionExpiredMessage(message),
            Message = message,
            WaveCustomerId = waveCustomerId
        };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
