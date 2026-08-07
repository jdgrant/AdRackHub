using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdRackHub.Services;

public class MonthlyBillingService
{
    private readonly ApplicationDbContext _context;
    private readonly WaveApiService _waveApiService;
    private readonly WaveSyncService _waveSyncService;
    private readonly ILogger<MonthlyBillingService> _logger;

    public MonthlyBillingService(
        ApplicationDbContext context,
        WaveApiService waveApiService,
        WaveSyncService waveSyncService,
        ILogger<MonthlyBillingService> logger)
    {
        _context = context;
        _waveApiService = waveApiService;
        _waveSyncService = waveSyncService;
        _logger = logger;
    }

    public Task<List<DueContractItem>> GetDueContractsAsync(int year, int month, CancellationToken cancellationToken = default) =>
        GetDueBillingsAsync(year, month, cancellationToken);

    public async Task<List<DueContractItem>> GetDueBillingsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var billedContractIds = await GetBilledContractIdsForPeriodAsync(year, month, cancellationToken);

        var contracts = await _context.CustomerContracts
            .Include(c => c.Customer)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
            .Where(c => c.Customer.Status == CustomerStatus.Active && c.Customer.Type == CustomerType.Customer)
            .Where(c => c.ContractRoutes.Any())
            .ToListAsync(cancellationToken);

        return contracts
            .Where(c => BillingDueCalculator.IsContractDue(c, year, month))
            .Where(c => !billedContractIds.Contains(c.Id))
            .SelectMany(c => c.ContractRoutes.Select(cr => new DueContractItem
            {
                CustomerId = c.CustomerId,
                CustomerName = c.Customer.CustomerName,
                WaveCustomerId = c.Customer.WaveCustomerId,
                CustomerContractId = c.Id,
                ContractName = c.ContractName,
                Term = c.Term,
                NextBillDate = c.NextBillDate,
                ServiceMonthMask = c.ServiceMonthMask,
                RouteId = cr.RouteId,
                RouteName = cr.Route.RouteName,
                Amount = AnnualBillingHelper.GetBillingAmount(cr)
            }))
            .OrderBy(i => i.CustomerName)
            .ThenBy(i => i.ContractName)
            .ThenBy(i => i.RouteName)
            .ToList();
    }

    private async Task<HashSet<int>> GetBilledContractIdsForPeriodAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var ids = await _context.BillingRunInvoiceLines
            .Where(l => l.BillingRunInvoice.BillingRun.Year == year
                        && l.BillingRunInvoice.BillingRun.Month == month
                        && (l.BillingRunInvoice.Status == BillingRunInvoiceStatus.Submitted
                            || l.BillingRunInvoice.Status == BillingRunInvoiceStatus.Received
                            || l.BillingRunInvoice.Status == BillingRunInvoiceStatus.Canceled))
            .Select(l => l.CustomerContractId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task<BillingRun?> GetRunAsync(int year, int month, CancellationToken cancellationToken = default) =>
        await _context.BillingRuns
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Customer)
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Lines)
            .FirstOrDefaultAsync(r => r.Year == year && r.Month == month, cancellationToken);

    public async Task<BillingRun> PrepareAndSubmitAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var run = await GetRunAsync(year, month, cancellationToken);
        if (run == null)
            run = await PrepareRunAsync(year, month, cancellationToken);
        else
            run = await GetRunAsync(year, month, cancellationToken) ?? run;

        if (run.Status == BillingRunStatus.Submitted)
            throw new InvalidOperationException("This billing period has already been submitted to Wave.");

        return await SubmitRunAsync(run.Id, cancellationToken);
    }

    public async Task<BillingRun> PrepareRunAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var existing = await _context.BillingRuns
            .Include(r => r.Invoices)
            .FirstOrDefaultAsync(r => r.Year == year && r.Month == month, cancellationToken);

        if (existing != null)
        {
            if (existing.Status is BillingRunStatus.Submitted or BillingRunStatus.PartiallySubmitted)
                throw new InvalidOperationException(
                    $"Billing for {BillingDueCalculator.PeriodLabel(year, month)} has already been submitted.");

            _context.BillingRuns.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var dueItems = await GetDueContractsAsync(year, month, cancellationToken);
        var run = new BillingRun
        {
            Year = year,
            Month = month,
            Status = BillingRunStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var customerGroup in dueItems.GroupBy(i => i.CustomerId))
        {
            var first = customerGroup.First();
            var invoice = new BillingRunInvoice
            {
                CustomerId = customerGroup.Key,
                WaveCustomerId = first.WaveCustomerId,
                TotalAmount = customerGroup.Sum(i => i.Amount),
                Status = BillingRunInvoiceStatus.Pending,
                Lines = customerGroup.Select(item => new BillingRunInvoiceLine
                {
                    CustomerContractId = item.CustomerContractId,
                    ContractName = item.ContractName,
                    Term = item.Term,
                    RouteName = item.RouteName,
                    Amount = item.Amount
                }).ToList()
            };

            run.Invoices.Add(invoice);
        }

        _context.BillingRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<BillingRun> SubmitRunAsync(int billingRunId, CancellationToken cancellationToken = default)
    {
        var run = await _context.BillingRuns
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Lines)
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Customer)
                    .ThenInclude(c => c.Contacts)
            .FirstOrDefaultAsync(r => r.Id == billingRunId, cancellationToken)
            ?? throw new InvalidOperationException("Billing run not found.");

        if (run.Status == BillingRunStatus.Submitted)
            throw new InvalidOperationException("This billing run has already been submitted.");

        if (!_waveApiService.IsConfigured)
            throw new InvalidOperationException("Wave API is not configured.");

        var invoiceDate = new DateOnly(run.Year, run.Month, 1);
        var memo = $"AdRack route billing — {BillingDueCalculator.PeriodLabel(run.Year, run.Month)}";

        foreach (var invoice in run.Invoices.Where(i =>
                     i.Status is BillingRunInvoiceStatus.Pending
                         or BillingRunInvoiceStatus.Failed
                         or BillingRunInvoiceStatus.Skipped))
        {
            var (waveCustomerId, customerError) = await _waveSyncService.EnsureCustomerOnWaveAsync(invoice.Customer, cancellationToken);
            if (waveCustomerId == null)
            {
                invoice.Status = BillingRunInvoiceStatus.Failed;
                invoice.ErrorMessage = customerError ?? "Failed to create or resolve Wave customer.";
                continue;
            }

            invoice.WaveCustomerId = waveCustomerId;
            invoice.Status = BillingRunInvoiceStatus.Pending;
            invoice.ErrorMessage = null;

            var lineItems = invoice.Lines.Select(line => new WaveInvoiceLineItem
            {
                Description = $"{line.ContractName} ({BillingTermDisplay.Label(line.Term)}) — {line.RouteName}",
                Quantity = 1,
                UnitPrice = line.Amount
            }).ToList();

            var result = await _waveApiService.CreateInvoiceAsync(
                waveCustomerId,
                invoiceDate,
                lineItems,
                memo,
                cancellationToken);

            if (result.Success)
            {
                invoice.Status = BillingRunInvoiceStatus.Submitted;
                invoice.WaveInvoiceId = result.WaveInvoiceId;
                invoice.WaveInvoiceNumber = result.InvoiceNumber;
                invoice.WaveInvoiceUrl = result.WaveInvoiceUrl;
                invoice.ErrorMessage = null;

                await AdvanceContractBillDatesAsync(invoice.Lines.Select(l => l.CustomerContractId).Distinct(), cancellationToken);
            }
            else
            {
                invoice.Status = BillingRunInvoiceStatus.Failed;
                invoice.ErrorMessage = result.ErrorMessage;
            }
        }

        run.SubmittedAt = DateTime.UtcNow;
        run.Status = run.Invoices.All(i => IsCompletedInvoiceStatus(i.Status))
            ? BillingRunStatus.Submitted
            : run.Invoices.Any(i => IsCompletedInvoiceStatus(i.Status))
                ? BillingRunStatus.PartiallySubmitted
                : BillingRunStatus.Failed;

        await _context.SaveChangesAsync(cancellationToken);
        return run;
    }

    /// <summary>
    /// Creates an invoice for one contract, POSTs it to the Make/Zapier webhook, logs the invoice ID,
    /// and advances NextBillDate by the contract term (1 month / 3 months / 1 year).
    /// </summary>
    public async Task<(bool Success, string Message, int? InvoiceId)> CreateOnSendAsync(
        int year,
        int month,
        int customerContractId,
        CancellationToken cancellationToken = default)
    {
        if (!_waveSyncService.IsCreateOnSendConfigured)
            return (false, "Configure WaveSync:TestMakeWebhookUrl or WaveSync:TestZapierWebhookUrl first.", null);

        var dueItems = (await GetDueContractsAsync(year, month, cancellationToken))
            .Where(i => i.CustomerContractId == customerContractId)
            .ToList();

        if (dueItems.Count == 0)
        {
            // May already be billed this period (and therefore excluded from due).
            var existing = await _context.BillingRunInvoices
                .Include(i => i.Lines)
                .Where(i => i.BillingRun.Year == year
                            && i.BillingRun.Month == month
                            && (i.Status == BillingRunInvoiceStatus.Submitted
                                || i.Status == BillingRunInvoiceStatus.Received
                                || i.Status == BillingRunInvoiceStatus.Canceled)
                            && i.Lines.Any(l => l.CustomerContractId == customerContractId))
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing != null)
                return (true, $"Invoice already sent for this period. Invoice ID: {existing.Id}.", existing.Id);

            return (false, "This contract is not due for the selected period.", null);
        }

        var customerId = dueItems.First().CustomerId;
        var invoice = await EnsureContractInvoiceAsync(year, month, customerId, customerContractId, dueItems, cancellationToken);

        if (invoice.Status == BillingRunInvoiceStatus.Submitted)
        {
            _logger.LogInformation(
                "Create On Send skipped; invoice ID {InvoiceId} already submitted for contract {ContractId}.",
                invoice.Id,
                customerContractId);
            return (true, $"Invoice already sent. Invoice ID: {invoice.Id}.", invoice.Id);
        }

        var (success, message, _) = await _waveSyncService.SendCreateOnSendInvoiceAsync(
            year,
            month,
            customerId,
            customerContractId,
            invoice.Id,
            cancellationToken);

        if (!success)
        {
            invoice.Status = BillingRunInvoiceStatus.Failed;
            invoice.ErrorMessage = message;
            await UpdateRunStatusAsync(invoice.BillingRunId, cancellationToken);
            return (false, message, invoice.Id);
        }

        invoice.Status = BillingRunInvoiceStatus.Submitted;
        invoice.ErrorMessage = null;

        await AdvanceContractBillDatesAsync(new[] { customerContractId }, cancellationToken);
        await UpdateRunStatusAsync(invoice.BillingRunId, cancellationToken);

        var contract = await _context.CustomerContracts.FindAsync(new object[] { customerContractId }, cancellationToken);
        var nextBill = contract?.NextBillDate.ToString("MMM d, yyyy") ?? "updated";

        _logger.LogInformation(
            "Create On Send completed. Invoice ID {InvoiceId} for contract {ContractId} period {Period}; next bill {NextBillDate}.",
            invoice.Id,
            customerContractId,
            BillingDueCalculator.PeriodLabel(year, month),
            nextBill);

        return (true, $"{message} Next bill date: {nextBill}.", invoice.Id);
    }

    private async Task<BillingRunInvoice> EnsureContractInvoiceAsync(
        int year,
        int month,
        int customerId,
        int customerContractId,
        List<DueContractItem> dueItems,
        CancellationToken cancellationToken)
    {
        var run = await GetRunAsync(year, month, cancellationToken);
        if (run == null)
        {
            run = new BillingRun
            {
                Year = year,
                Month = month,
                Status = BillingRunStatus.Draft,
                CreatedAt = DateTime.UtcNow
            };
            _context.BillingRuns.Add(run);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Reuse a pending/failed invoice for this contract in this period; otherwise create a new one
        // so a contract can accumulate many invoices over time (and catch-up within a period).
        var invoice = run.Invoices.FirstOrDefault(i =>
            i.Status is BillingRunInvoiceStatus.Pending or BillingRunInvoiceStatus.Failed
            && i.Lines.Any(l => l.CustomerContractId == customerContractId)
            && i.Lines.All(l => l.CustomerContractId == customerContractId));

        if (invoice != null)
        {
            invoice.TotalAmount = dueItems.Sum(i => i.Amount);
            invoice.WaveCustomerId = dueItems.First().WaveCustomerId;
            invoice.ErrorMessage = null;
            invoice.Lines.Clear();
            foreach (var item in dueItems)
            {
                invoice.Lines.Add(new BillingRunInvoiceLine
                {
                    CustomerContractId = item.CustomerContractId,
                    ContractName = item.ContractName,
                    Term = item.Term,
                    RouteName = item.RouteName,
                    Amount = item.Amount
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            return invoice;
        }

        invoice = new BillingRunInvoice
        {
            BillingRunId = run.Id,
            CustomerId = customerId,
            WaveCustomerId = dueItems.First().WaveCustomerId,
            TotalAmount = dueItems.Sum(i => i.Amount),
            Status = BillingRunInvoiceStatus.Pending,
            Lines = dueItems.Select(item => new BillingRunInvoiceLine
            {
                CustomerContractId = item.CustomerContractId,
                ContractName = item.ContractName,
                Term = item.Term,
                RouteName = item.RouteName,
                Amount = item.Amount
            }).ToList()
        };

        run.Invoices.Add(invoice);
        await _context.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    private async Task UpdateRunStatusAsync(int billingRunId, CancellationToken cancellationToken)
    {
        var run = await _context.BillingRuns
            .Include(r => r.Invoices)
            .FirstOrDefaultAsync(r => r.Id == billingRunId, cancellationToken);
        if (run == null)
            return;

        run.SubmittedAt = DateTime.UtcNow;
        run.Status = run.Invoices.All(i => IsCompletedInvoiceStatus(i.Status))
            ? BillingRunStatus.Submitted
            : run.Invoices.Any(i => IsCompletedInvoiceStatus(i.Status))
                ? BillingRunStatus.PartiallySubmitted
                : run.Invoices.Any(i => i.Status == BillingRunInvoiceStatus.Failed)
                    ? BillingRunStatus.Failed
                    : BillingRunStatus.Draft;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(bool Success, string Message)> UpdateInvoiceWaveStatusAsync(
        int invoiceId,
        BillingRunInvoiceStatus status,
        string? waveInvoiceNumber = null,
        string? waveInvoiceId = null,
        string? waveInvoiceUrl = null,
        DateOnly? receivedDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!WaveInvoiceStatuses.IsWaveLifecycle(status))
            return (false, "Status must be Submitted, Received, or Canceled.");

        var invoice = await _context.BillingRunInvoices
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            return (false, "Invoice not found.");

        if (!string.IsNullOrWhiteSpace(waveInvoiceNumber))
            invoice.WaveInvoiceNumber = waveInvoiceNumber.Trim();

        if (!string.IsNullOrWhiteSpace(waveInvoiceId))
            invoice.WaveInvoiceId = waveInvoiceId.Trim();

        if (!string.IsNullOrWhiteSpace(waveInvoiceUrl))
            invoice.WaveInvoiceUrl = waveInvoiceUrl.Trim();

        invoice.Status = status;
        invoice.ErrorMessage = null;

        if (status == BillingRunInvoiceStatus.Received)
            invoice.ReceivedDate = receivedDate ?? DateOnly.FromDateTime(DateTime.Today);
        else if (status == BillingRunInvoiceStatus.Submitted)
            invoice.ReceivedDate = null;

        await UpdateRunStatusAsync(invoice.BillingRunId, cancellationToken);

        _logger.LogInformation(
            "Invoice {InvoiceId} status set to {Status} (Wave #{WaveInvoiceNumber}, received {ReceivedDate}).",
            invoice.Id,
            invoice.Status,
            invoice.WaveInvoiceNumber ?? "—",
            invoice.ReceivedDate?.ToString("yyyy-MM-dd") ?? "—");

        var message = status == BillingRunInvoiceStatus.Received
            ? $"Invoice {invoice.Id} marked Received on {invoice.ReceivedDate:MMM d, yyyy}."
            : $"Invoice {invoice.Id} marked {status}.";
        return (true, message);
    }

    public async Task<List<BillingRunInvoice>> GetSubmittedInvoicesAsync(
        int? year = null,
        int? month = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BillingRunInvoices
            .Include(i => i.Customer)
            .Include(i => i.BillingRun)
            .Include(i => i.Lines)
            .Where(i => i.Status == BillingRunInvoiceStatus.Submitted
                        || i.Status == BillingRunInvoiceStatus.Received
                        || i.Status == BillingRunInvoiceStatus.Canceled);

        if (year.HasValue)
            query = query.Where(i => i.BillingRun.Year == year.Value);

        if (month.HasValue)
            query = query.Where(i => i.BillingRun.Month == month.Value);

        return await query
            .OrderByDescending(i => i.BillingRun.Year)
            .ThenByDescending(i => i.BillingRun.Month)
            .ThenBy(i => i.Customer.CustomerName)
            .ThenByDescending(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    private static bool IsCompletedInvoiceStatus(BillingRunInvoiceStatus status) =>
        status is BillingRunInvoiceStatus.Submitted
            or BillingRunInvoiceStatus.Received
            or BillingRunInvoiceStatus.Canceled
            or BillingRunInvoiceStatus.Skipped;

    private async Task AdvanceContractBillDatesAsync(IEnumerable<int> contractIds, CancellationToken cancellationToken)
    {
        foreach (var contractId in contractIds)
        {
            var contract = await _context.CustomerContracts.FindAsync(new object[] { contractId }, cancellationToken);
            if (contract == null)
                continue;

            contract.NextBillDate = BillingDueCalculator.AdvanceNextBillDate(contract.NextBillDate, contract.Term);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class DueContractItem
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? WaveCustomerId { get; init; }
    public int CustomerContractId { get; init; }
    public string ContractName { get; init; } = string.Empty;
    public BillingFrequency Term { get; init; }
    public DateOnly NextBillDate { get; init; }
    public int ServiceMonthMask { get; init; }
    public int RouteId { get; init; }
    public string RouteName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}
