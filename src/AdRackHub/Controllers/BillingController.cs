using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class BillingController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly MonthlyBillingService _billingService;
    private readonly WaveSessionService _waveSession;
    private readonly InvoicePdfStorageService _invoicePdfs;

    public BillingController(
        ApplicationDbContext context,
        MonthlyBillingService billingService,
        WaveSessionService waveSession,
        InvoicePdfStorageService invoicePdfs)
    {
        _context = context;
        _billingService = billingService;
        _waveSession = waveSession;
        _invoicePdfs = invoicePdfs;
    }

    public async Task<IActionResult> Index(int? year, int? month)
    {
        var now = DateTime.Today;
        var selectedYear = year ?? now.Year;
        var selectedMonth = month ?? now.Month;

        var model = new BillingPageViewModel
        {
            Year = selectedYear,
            Month = selectedMonth,
            PeriodLabel = BillingDueCalculator.PeriodLabel(selectedYear, selectedMonth),
            DueContracts = await _billingService.GetDueContractsAsync(selectedYear, selectedMonth),
            Run = await _billingService.GetRunAsync(selectedYear, selectedMonth),
            SubmittedInvoices = await _billingService.GetSubmittedInvoicesAsync(selectedYear, selectedMonth),
            WaveConfigured = _waveSession.IsReady,
            CreateOnSendConfigured = _waveSession.IsReady,
            WaveSessionExpired = _waveSession.NeedsBusinessReset,
            BatchPdfContractIds = ParseIdList(TempData["BatchPdfContractIds"] as string)
        };

        model.MissingBillingContacts = await GetMissingBillingContactsAsync(model.DueContractSummaries);
        await ApplyInvoicePdfAvailabilityAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOnSend(int year, int month, int customerContractId)
    {
        var (success, message, _) = await _billingService.CreateOnSendAsync(year, month, customerContractId);
        if (WaveSessionService.IsSessionExpiredMessage(message))
        {
            TempData["WaveSessionExpired"] = true;
        }
        else if (success)
        {
            TempData["Message"] = message;
        }
        else
        {
            TempData["Error"] = message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOnSendBatch(int year, int month, List<int>? customerContractIds)
    {
        var ids = (customerContractIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            TempData["Error"] = "Select at least one due contract to send.";
            return RedirectToAction(nameof(Index), new { year, month });
        }

        if (_waveSession.NeedsBusinessReset)
        {
            TempData["WaveSessionExpired"] = true;
            return RedirectToAction(nameof(Index), new { year, month });
        }

        var sentIds = new List<int>();
        var failed = new List<string>();
        foreach (var contractId in ids)
        {
            var (success, message, _) = await _billingService.CreateOnSendAsync(year, month, contractId);
            if (WaveSessionService.IsSessionExpiredMessage(message))
            {
                TempData["WaveSessionExpired"] = true;
                if (sentIds.Count > 0)
                {
                    TempData["Message"] = $"Sent {sentIds.Count} invoice(s) before the Wave session expired. Download the combined PDF.";
                    await RememberBatchPdfAsync(sentIds);
                }
                return RedirectToAction(nameof(Index), new { year, month });
            }

            if (success)
                sentIds.Add(contractId);
            else
                failed.Add(message);
        }

        if (sentIds.Count > 0)
        {
            await RememberBatchPdfAsync(sentIds);
            TempData["Message"] = failed.Count == 0
                ? $"Sent {sentIds.Count} invoice(s) to Wave. Downloading one combined PDF."
                : $"Sent {sentIds.Count} invoice(s). {failed.Count} failed: {string.Join(" ", failed.Take(3))}";
        }
        else
            TempData["Error"] = failed.Count > 0 ? string.Join(" ", failed.Take(3)) : "No invoices were sent.";

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetInvoice(int year, int month, int invoiceId)
    {
        var (success, message) = await _billingService.ResetInvoiceForResendAsync(invoiceId);
        if (success)
            TempData["Message"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { year, month });
    }

    public async Task<IActionResult> InvoicePdf(int invoiceId)
    {
        var invoice = await _context.BillingRunInvoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice == null)
            return NotFound();

        var contractIds = invoice.Lines.Select(l => l.CustomerContractId).Distinct().ToList();
        var contracts = await _context.CustomerContracts
            .AsNoTracking()
            .Where(c => contractIds.Contains(c.Id))
            .Select(c => new { c.Id, c.WaveInvoicePdfPath, c.WaveInvoiceNumber })
            .ToListAsync();

        foreach (var contract in contracts)
        {
            var path = _invoicePdfs.ResolveFilePath(contract.WaveInvoicePdfPath, contract.WaveInvoiceNumber, contract.Id);
            if (path != null)
            {
                return PhysicalFile(
                    path,
                    "application/pdf",
                    InvoicePdfStorageService.DownloadFileName(
                        contract.WaveInvoiceNumber ?? invoice.WaveInvoiceNumber,
                        contract.WaveInvoicePdfPath));
            }
        }

        var byNumber = _invoicePdfs.ResolveFilePath(null, invoice.WaveInvoiceNumber);
        if (byNumber == null)
            return NotFound();

        return PhysicalFile(
            byNumber,
            "application/pdf",
            InvoicePdfStorageService.DownloadFileName(invoice.WaveInvoiceNumber, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadPdfs(
        int year,
        int month,
        List<int>? customerContractIds,
        List<int>? invoiceIds,
        string? downloadSelected)
    {
        if (string.IsNullOrWhiteSpace(downloadSelected))
            invoiceIds = null;

        var files = await CollectInvoicePdfsAsync(year, month, customerContractIds, invoiceIds);
        if (files.Count == 0)
        {
            TempData["Error"] = customerContractIds is { Count: > 0 } || invoiceIds is { Count: > 0 }
                ? "None of the selected invoices have a saved PDF yet."
                : "No invoice PDFs are saved for this billing period.";
            return RedirectToAction(nameof(Index), new { year, month });
        }

        byte[] bytes;
        try
        {
            bytes = _invoicePdfs.Merge(files.Select(f => f.Path).ToList());
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Could not combine invoice PDFs: {ex.Message}";
            return RedirectToAction(nameof(Index), new { year, month });
        }

        var period = BillingDueCalculator.PeriodLabel(year, month).Replace(' ', '-');
        return File(bytes, "application/pdf", $"AdRack-invoices-{period}.pdf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkInvoiceReceived(
        int year,
        int month,
        int invoiceId,
        DateOnly? receivedDate,
        string? waveInvoiceNumber)
    {
        var (success, message) = await _billingService.UpdateInvoiceWaveStatusAsync(
            invoiceId,
            BillingRunInvoiceStatus.Received,
            waveInvoiceNumber,
            receivedDate: receivedDate ?? DateOnly.FromDateTime(DateTime.Today));

        if (success)
            TempData["Message"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInvoiceStatus(
        int year,
        int month,
        int invoiceId,
        string status,
        string? waveInvoiceNumber,
        DateOnly? receivedDate)
    {
        if (!WaveInvoiceStatuses.TryParse(status, out var parsed))
        {
            TempData["Error"] = "Status must be Submitted, Received, or Canceled.";
            return RedirectToAction(nameof(Index), new { year, month });
        }

        var (success, message) = await _billingService.UpdateInvoiceWaveStatusAsync(
            invoiceId,
            parsed,
            waveInvoiceNumber,
            receivedDate: receivedDate);

        if (success)
            TempData["Message"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Prepare(int year, int month)
    {
        try
        {
            var run = await _billingService.PrepareRunAsync(year, month);
            TempData["Message"] = $"Prepared {run.Invoices.Count} invoices for {BillingDueCalculator.PeriodLabel(year, month)}.";
        }
        catch (Exception ex)
        {
            if (WaveSessionService.IsSessionExpiredMessage(ex.Message))
                TempData["WaveSessionExpired"] = true;
            else
                TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToWave(int year, int month)
    {
        try
        {
            var run = await _billingService.GetRunAsync(year, month);
            if (run == null)
                run = await _billingService.PrepareRunAsync(year, month);

            run = await _billingService.SubmitRunAsync(run.Id);
            TempData["Message"] = run.Status switch
            {
                BillingRunStatus.Submitted => $"Sent {run.Invoices.Count(i => i.Status == BillingRunInvoiceStatus.Submitted)} invoice(s) to Wave.",
                BillingRunStatus.PartiallySubmitted => "Some invoices were sent to Wave. Retry failed invoices below.",
                BillingRunStatus.Failed => "Wave submission failed. Check errors below and retry.",
                _ => "Billing updated."
            };
        }
        catch (Exception ex)
        {
            if (WaveSessionService.IsSessionExpiredMessage(ex.Message))
                TempData["WaveSessionExpired"] = true;
            else
                TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int year, int month)
    {
        var run = await _billingService.GetRunAsync(year, month);
        if (run == null)
            return RedirectToAction(nameof(Index), new { year, month });

        try
        {
            run = await _billingService.SubmitRunAsync(run.Id);
            TempData["Message"] = run.Status switch
            {
                BillingRunStatus.Submitted => "All remaining invoices sent to Wave.",
                BillingRunStatus.PartiallySubmitted => "Some invoices still failed. Review errors below.",
                _ => "Retry completed."
            };
        }
        catch (Exception ex)
        {
            if (WaveSessionService.IsSessionExpiredMessage(ex.Message))
                TempData["WaveSessionExpired"] = true;
            else
                TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }

    private async Task<List<MissingBillingContactAlert>> GetMissingBillingContactsAsync(
        IReadOnlyList<DueContractSummary> dueContracts)
    {
        var customerIds = dueContracts.Select(c => c.CustomerId).Distinct().ToList();
        if (customerIds.Count == 0)
            return new List<MissingBillingContactAlert>();

        var contacts = await _context.Contacts
            .AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId))
            .ToListAsync();

        var contactsByCustomer = contacts
            .GroupBy(c => c.CustomerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var customerEmails = await _context.Customers
            .AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Email, c.InvoiceReceiptMethod })
            .ToDictionaryAsync(c => c.Id);

        return dueContracts
            .GroupBy(c => new { c.CustomerId, c.CustomerName })
            .Select(g =>
            {
                contactsByCustomer.TryGetValue(g.Key.CustomerId, out var customerContacts);
                customerEmails.TryGetValue(g.Key.CustomerId, out var customer);
                var contactsForCustomer = customerContacts ?? new List<Contact>();
                var issue = BillingContactHelper.MissingReason(contactsForCustomer);
                var emailWarning = customer != null && customer.InvoiceReceiptMethod.IncludesEmail()
                    ? BillingContactHelper.MissingEmailReason(contactsForCustomer, customer.Email)
                    : null;
                if (issue == null && emailWarning == null)
                    return null;

                return new MissingBillingContactAlert
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.CustomerName,
                    Issue = issue ?? emailWarning!,
                    IsEmailWarning = issue == null,
                    DueContractCount = g.Count(),
                    DueAmount = g.Sum(c => c.Amount)
                };
            })
            .Where(a => a != null)
            .Select(a => a!)
            .OrderBy(a => a.CustomerName)
            .ToList();
    }

    private async Task ApplyInvoicePdfAvailabilityAsync(BillingPageViewModel model)
    {
        var contractIds = model.SubmittedInvoices
            .SelectMany(i => i.Lines.Select(l => l.CustomerContractId))
            .Concat(model.DueContracts.Select(d => d.CustomerContractId))
            .Distinct()
            .ToList();
        if (contractIds.Count == 0)
        {
            model.ContractsWithInvoicePdf = new HashSet<int>();
            model.InvoicesWithInvoicePdf = new HashSet<int>();
            return;
        }

        var contracts = await _context.CustomerContracts
            .AsNoTracking()
            .Where(c => contractIds.Contains(c.Id))
            .Select(c => new { c.Id, c.WaveInvoicePdfPath, c.WaveInvoiceNumber })
            .ToListAsync();

        var byId = contracts.ToDictionary(c => c.Id);
        model.ContractsWithInvoicePdf = contracts
            .Where(c => _invoicePdfs.ResolveFilePath(c.WaveInvoicePdfPath, c.WaveInvoiceNumber, c.Id) != null)
            .Select(c => c.Id)
            .ToHashSet();

        var invoicesWithPdf = new HashSet<int>();
        foreach (var invoice in model.SubmittedInvoices)
        {
            if (string.IsNullOrWhiteSpace(invoice.WaveInvoiceNumber))
            {
                invoice.WaveInvoiceNumber = invoice.Lines
                    .Select(l => byId.GetValueOrDefault(l.CustomerContractId)?.WaveInvoiceNumber)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            }

            var hasPdf = invoice.Lines.Any(l => model.ContractsWithInvoicePdf.Contains(l.CustomerContractId))
                || _invoicePdfs.ResolveFilePath(null, invoice.WaveInvoiceNumber) != null;
            if (hasPdf)
                invoicesWithPdf.Add(invoice.Id);
        }

        model.InvoicesWithInvoicePdf = invoicesWithPdf;
    }

    private async Task<List<(string Path, string EntryName)>> CollectInvoicePdfsAsync(
        int year,
        int month,
        List<int>? customerContractIds,
        List<int>? invoiceIds)
    {
        var contractFilter = (customerContractIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
        var invoiceFilter = (invoiceIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
        var files = new List<(string Path, string EntryName)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (contractFilter.Count > 0)
        {
            var contracts = await _context.CustomerContracts
                .AsNoTracking()
                .Include(c => c.Customer)
                .Where(c => contractFilter.Contains(c.Id))
                .OrderBy(c => c.Customer.CustomerName)
                .ThenBy(c => c.ContractName)
                .ToListAsync();
            foreach (var contract in contracts)
                TryAddPdf(files, seen, contract.WaveInvoicePdfPath, contract.WaveInvoiceNumber, contract.Id, contract.Customer.CustomerName);
            return files;
        }

        var invoices = await _billingService.GetSubmittedInvoicesAsync(year, month);
        if (invoiceFilter.Count > 0)
            invoices = invoices.Where(i => invoiceFilter.Contains(i.Id)).ToList();

        var contractIds = invoices.SelectMany(i => i.Lines.Select(l => l.CustomerContractId)).Distinct().ToList();
        var billedContracts = await _context.CustomerContracts
            .AsNoTracking()
            .Where(c => contractIds.Contains(c.Id))
            .Select(c => new { c.Id, c.WaveInvoicePdfPath, c.WaveInvoiceNumber })
            .ToListAsync();
        var byId = billedContracts.ToDictionary(c => c.Id);

        foreach (var invoice in invoices)
        {
            var added = false;
            foreach (var line in invoice.Lines)
            {
                if (!byId.TryGetValue(line.CustomerContractId, out var contract))
                    continue;
                added |= TryAddPdf(
                    files,
                    seen,
                    contract.WaveInvoicePdfPath,
                    contract.WaveInvoiceNumber ?? invoice.WaveInvoiceNumber,
                    contract.Id,
                    invoice.Customer.CustomerName);
            }

            if (!added)
                TryAddPdf(files, seen, null, invoice.WaveInvoiceNumber, invoice.Id, invoice.Customer.CustomerName);
        }

        return files;
    }

    private bool TryAddPdf(
        List<(string Path, string EntryName)> files,
        HashSet<string> seenPaths,
        string? storedFileName,
        string? invoiceNumber,
        int id,
        string customerName)
    {
        var path = _invoicePdfs.ResolveFilePath(storedFileName, invoiceNumber, id);
        if (path == null || !seenPaths.Add(path))
            return false;

        files.Add((path, customerName));
        return true;
    }

    private async Task RememberBatchPdfAsync(IReadOnlyList<int> contractIds)
    {
        if (contractIds.Count == 0)
            return;

        var contracts = await _context.CustomerContracts
            .AsNoTracking()
            .Where(c => contractIds.Contains(c.Id))
            .Select(c => new { c.Id, c.WaveInvoicePdfPath, c.WaveInvoiceNumber })
            .ToListAsync();

        var withPdf = contracts
            .Where(c => _invoicePdfs.ResolveFilePath(c.WaveInvoicePdfPath, c.WaveInvoiceNumber, c.Id) != null)
            .Select(c => c.Id)
            .ToList();
        if (withPdf.Count == 0)
            return;

        TempData["BatchPdfContractIds"] = string.Join(",", withPdf);
    }

    private static List<int> ParseIdList(string? raw) =>
        (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
}
