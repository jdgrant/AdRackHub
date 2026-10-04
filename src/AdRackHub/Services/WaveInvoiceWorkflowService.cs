using System.Globalization;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
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
    private readonly MailgunInvoiceEmailService _mailgun;
    private readonly ILogger<WaveInvoiceWorkflowService> _logger;

    public WaveInvoiceWorkflowService(
        ApplicationDbContext context,
        WaveApiService waveApi,
        WaveSessionService session,
        InvoicePdfStorageService invoicePdfs,
        InvoicePdfGenerator invoicePdf,
        MailgunInvoiceEmailService mailgun,
        ILogger<WaveInvoiceWorkflowService> logger)
    {
        _context = context;
        _waveApi = waveApi;
        _session = session;
        _invoicePdfs = invoicePdfs;
        _invoicePdf = invoicePdf;
        _mailgun = mailgun;
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
                : "Connect to Wave on the Billing page before sending invoices.");
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

        var (pdfSaved, pdfBytes) = await PersistOnContractsAsync(
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
            approved,
            pdfBytes,
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

        var (pdfSaved, pdfBytes) = await PersistOnContractsAsync(
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
            approved,
            pdfBytes,
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

    public async Task<(int Saved, int Failed, string Message)> RegenerateSavedPdfsAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var invoices = await _context.BillingRunInvoices
            .Include(i => i.BillingRun)
            .Include(i => i.Lines)
            .Include(i => i.Customer)
                .ThenInclude(c => c.Contacts)
            .Where(i => i.BillingRun.Year == year && i.BillingRun.Month == month)
            .Where(i => i.Status == BillingRunInvoiceStatus.Submitted
                        || i.Status == BillingRunInvoiceStatus.Received)
            .OrderBy(i => i.Customer.CustomerName)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
            return (0, 0, $"No submitted invoices for {BillingDueCalculator.PeriodLabel(year, month)}.");

        var contractIds = invoices
            .SelectMany(i => i.Lines.Select(l => l.CustomerContractId))
            .Distinct()
            .ToList();
        var contracts = await _context.CustomerContracts
            .Where(c => contractIds.Contains(c.Id))
            .ToListAsync(cancellationToken);
        var byId = contracts.ToDictionary(c => c.Id);

        var invoiceDate = new DateOnly(year, month, 1);
        var saved = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var invoice in invoices)
        {
            var persistContracts = invoice.Lines
                .Select(l => byId.GetValueOrDefault(l.CustomerContractId))
                .Where(c => c != null)
                .Cast<CustomerContract>()
                .Distinct()
                .ToList();
            if (persistContracts.Count == 0)
            {
                failed++;
                errors.Add($"{invoice.Customer.CustomerName}: no contracts on the invoice.");
                continue;
            }

            var lineItems = invoice.Lines
                .OrderBy(l => l.RouteName)
                .Select(l =>
                {
                    var months = l.BillingMonthCount > 0
                        ? l.BillingMonthCount
                        : AnnualBillingHelper.MonthsInTerm(l.Term);
                    var content = InvoiceLineFormatter.Build(
                        l.RouteName,
                        invoiceDate,
                        months,
                        l.Amount);
                    return new WaveInvoiceLineItem
                    {
                        ProductName = RouteNaming.DisplayName(l.RouteName),
                        Description = content.Description,
                        Quantity = 1,
                        UnitPrice = l.Amount
                    };
                })
                .ToList();

            var wave = WaveInvoiceResult.Succeeded(
                invoice.WaveInvoiceId ?? "",
                invoice.WaveInvoiceUrl,
                invoice.WaveInvoiceNumber);

            try
            {
                var (ok, _) = await PersistOnContractsAsync(
                    invoice.Customer,
                    persistContracts,
                    lineItems,
                    invoiceDate,
                    wave,
                    accessToken: null,
                    businessId: null,
                    cancellationToken,
                    resolvePayUrl: false);
                if (ok)
                    saved++;
                else
                {
                    failed++;
                    errors.Add($"{invoice.Customer.CustomerName}: Ad-Rack PDF was not generated.");
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{invoice.Customer.CustomerName}: {ex.Message}");
                _logger.LogWarning(
                    ex,
                    "Failed regenerating Ad-Rack PDF for billing invoice {InvoiceId}.",
                    invoice.Id);
            }
        }

        var period = BillingDueCalculator.PeriodLabel(year, month);
        var message = $"Rebuilt {saved} Ad-Rack invoice PDF(s) for {period}.";
        if (failed > 0)
            message += $" {failed} failed. {string.Join(" ", errors.Take(3))}";

        _logger.LogInformation(
            "Regenerated Ad-Rack invoice PDFs for {Period}: saved {Saved}, failed {Failed}.",
            period,
            saved,
            failed);

        return (saved, failed, message);
    }

    public async Task<WaveInvoiceWorkflowResult> RecordPaymentAsync(
        string? waveInvoiceId,
        string? waveInvoiceNumber,
        decimal fallbackAmount,
        DateOnly paymentDate,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _session.GetCredentialsAsync(cancellationToken);
        if (credentials == null
            || string.IsNullOrWhiteSpace(credentials.AccessToken)
            || string.IsNullOrWhiteSpace(credentials.BusinessId))
        {
            return Fail(_session.NeedsBusinessReset
                ? WaveSessionService.SessionExpiredReconnectMessage
                : "Connect to Wave on the Billing page before recording a payment.");
        }

        WaveInvoiceResult? invoice = null;
        if (!string.IsNullOrWhiteSpace(waveInvoiceId))
        {
            invoice = await _waveApi.GetInvoiceAsync(
                waveInvoiceId,
                cancellationToken,
                credentials.AccessToken,
                credentials.BusinessId);
        }

        if ((invoice == null || !invoice.Success) && !string.IsNullOrWhiteSpace(waveInvoiceNumber))
        {
            invoice = await _waveApi.FindInvoiceByNumberAsync(
                waveInvoiceNumber,
                cancellationToken,
                credentials.AccessToken,
                credentials.BusinessId);
        }

        if (invoice == null || !invoice.Success || string.IsNullOrWhiteSpace(invoice.WaveInvoiceId))
        {
            return Fail(invoice?.ErrorMessage
                ?? "This invoice has no Wave invoice to mark paid.");
        }

        var status = invoice.Status ?? string.Empty;
        if (status.Equals("PAID", StringComparison.OrdinalIgnoreCase)
            || status.Equals("OVERPAID", StringComparison.OrdinalIgnoreCase))
        {
            return new WaveInvoiceWorkflowResult
            {
                Success = true,
                Invoice = invoice,
                Message = $"Wave invoice {invoice.InvoiceNumber ?? invoice.WaveInvoiceId} is already paid."
            };
        }

        var amount = ParseAmount(invoice.Amount);
        if (amount <= 0)
            amount = fallbackAmount;
        if (amount <= 0)
            return Fail("Could not determine the amount due on the Wave invoice.");

        var paid = await _waveApi.RecordInvoicePaymentAsync(
            invoice.WaveInvoiceId,
            amount,
            paymentDate,
            cancellationToken,
            credentials.AccessToken,
            credentials.BusinessId);
        if (!paid.Success)
        {
            return new WaveInvoiceWorkflowResult
            {
                Success = false,
                SessionExpired = WaveSessionService.IsSessionExpiredMessage(paid.ErrorMessage),
                Invoice = invoice,
                Message = paid.ErrorMessage ?? "Wave payment failed."
            };
        }

        _logger.LogInformation(
            "Recorded Wave payment {Amount} on invoice {InvoiceNumber} ({InvoiceId}).",
            paid.Amount,
            invoice.InvoiceNumber,
            invoice.WaveInvoiceId);

        return new WaveInvoiceWorkflowResult
        {
            Success = true,
            Invoice = invoice,
            Message = $"Recorded {paid.Amount:C} in Wave on invoice {invoice.InvoiceNumber ?? invoice.WaveInvoiceId}."
        };
    }

    private static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        var trimmed = value.Trim().Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0;
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

    private async Task<(bool Saved, byte[]? PdfBytes)> PersistOnContractsAsync(
        Customer customer,
        IReadOnlyList<CustomerContract> contracts,
        IReadOnlyList<WaveInvoiceLineItem> lineItems,
        DateOnly invoiceDate,
        WaveInvoiceResult invoice,
        string? accessToken,
        string? businessId,
        CancellationToken cancellationToken,
        bool resolvePayUrl = true)
    {
        if (contracts.Count == 0)
            return (false, null);

        var dueDate = invoiceDate.AddDays(WaveApiService.InvoiceDueDays);
        if (!string.IsNullOrWhiteSpace(invoice.DueDate) && DateOnly.TryParse(invoice.DueDate, out var parsedDue))
            dueDate = parsedDue;

        string? payUrl = invoice.WaveInvoiceUrl;
        var invoiceNotes = CombinedInvoiceNotes(contracts);
        byte[]? bytes = TryGeneratePdf(customer, lineItems, invoice.InvoiceNumber, invoiceDate, dueDate, payUrl, invoiceNotes);

        if (resolvePayUrl
            && !string.IsNullOrWhiteSpace(accessToken)
            && !string.IsNullOrWhiteSpace(invoice.WaveInvoiceId)
            && !WaveApiService.IsWaveShortPayUrl(payUrl))
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
                    var withShortUrl = TryGeneratePdf(customer, lineItems, invoice.InvoiceNumber, invoiceDate, dueDate, payUrl, invoiceNotes);
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
        return (savedPdf, bytes);
    }

    private static string? CombinedInvoiceNotes(IReadOnlyList<CustomerContract> contracts)
    {
        var notes = contracts
            .Select(c => WarehouseLocation.NullIfEmpty(c.InvoiceNotes))
            .Where(n => n != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return notes.Count == 0 ? null : string.Join("\n\n", notes!);
    }

    private byte[]? TryGeneratePdf(
        Customer customer,
        IReadOnlyList<WaveInvoiceLineItem> lineItems,
        string? invoiceNumber,
        DateOnly invoiceDate,
        DateOnly dueDate,
        string? payUrl,
        string? invoiceNotes)
    {
        try
        {
            return _invoicePdf.Generate(_invoicePdf.Build(
                customer,
                lineItems,
                invoiceNumber,
                invoiceDate,
                dueDate,
                payUrl,
                invoiceNotes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ad-Rack invoice PDF generation failed for invoice {InvoiceNumber}.", invoiceNumber);
            return null;
        }
    }

    private async Task<string> SendIfEmailAsync(
        Customer customer,
        WaveInvoiceResult invoice,
        byte[]? pdfBytes,
        CancellationToken cancellationToken)
    {
        if (!customer.InvoiceReceiptMethod.IncludesEmail())
            return "Invoice receipt method is Mail, so the invoice was not emailed.";

        var recipients = BillingContactHelper.ContactsWithEmail(customer.Contacts);
        var emails = recipients.Select(c => c.Email!.Trim()).ToList();
        if (emails.Count == 0)
            return "No contacts have an email, so the invoice was not emailed.";

        if (pdfBytes is not { Length: > 0 })
            return "Ad-Rack invoice PDF was not generated, so the invoice was not emailed.";

        var sent = await _mailgun.SendInvoiceAsync(
            emails,
            invoice,
            customer.CustomerName,
            pdfBytes,
            InvoicePdfStorageService.BuildFileName(invoice.InvoiceNumber),
            cancellationToken);
        if (!sent.Success)
            return "Invoice email failed: " + (sent.ErrorMessage ?? "Unknown error");

        return "Emailed Ad-Rack PDF to " + string.Join(", ", sent.Recipients) + ".";
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
