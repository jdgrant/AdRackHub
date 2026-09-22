using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdRackHub.Services;

public class MonthlyBillingService
{
    private readonly ApplicationDbContext _context;
    private readonly WaveSessionService _waveSession;
    private readonly WaveInvoiceWorkflowService _waveInvoices;
    private readonly ILogger<MonthlyBillingService> _logger;

    public MonthlyBillingService(
        ApplicationDbContext context,
        WaveSessionService waveSession,
        WaveInvoiceWorkflowService waveInvoices,
        ILogger<MonthlyBillingService> logger)
    {
        _context = context;
        _waveSession = waveSession;
        _waveInvoices = waveInvoices;
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
                InvoiceReceiptMethod = c.Customer.InvoiceReceiptMethod,
                CustomerContractId = c.Id,
                ContractName = c.ContractName,
                Term = c.Term,
                BillingMonthCount = AnnualBillingHelper.BillingMonths(c),
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
                    BillingMonthCount = item.BillingMonthCount,
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
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Customer)
                    .ThenInclude(c => c.CustomerRoutes)
                        .ThenInclude(cr => cr.CustomerRouteStops)
                            .ThenInclude(crs => crs.Stop)
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Customer)
                    .ThenInclude(c => c.CustomerRoutes)
                        .ThenInclude(cr => cr.Route)
                            .ThenInclude(route => route.Stops)
            .FirstOrDefaultAsync(r => r.Id == billingRunId, cancellationToken)
            ?? throw new InvalidOperationException("Billing run not found.");

        if (run.Status == BillingRunStatus.Submitted)
            throw new InvalidOperationException("This billing run has already been submitted.");

        if (!_waveSession.IsReady)
        {
            throw new InvalidOperationException(_waveSession.NeedsBusinessReset
                ? WaveSessionService.SessionExpiredReconnectMessage
                : "Connect to Wave on the Admin Wave proof page before sending invoices.");
        }

        var credentials = await _waveSession.GetCredentialsAsync(cancellationToken);
        if (credentials == null
            || string.IsNullOrWhiteSpace(credentials.AccessToken)
            || string.IsNullOrWhiteSpace(credentials.BusinessId))
        {
            throw new InvalidOperationException(_waveSession.NeedsBusinessReset
                ? WaveSessionService.SessionExpiredReconnectMessage
                : "Connect to Wave on the Admin Wave proof page before sending invoices.");
        }

        var invoiceDate = new DateOnly(run.Year, run.Month, 1);
        var memo = $"AdRack route billing — {BillingDueCalculator.PeriodLabel(run.Year, run.Month)}";
        var contractIds = run.Invoices.SelectMany(i => i.Lines.Select(l => l.CustomerContractId)).Distinct().ToList();
        var contracts = await _context.CustomerContracts
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
            .Where(c => contractIds.Contains(c.Id))
            .ToListAsync(cancellationToken);
        var contractsById = contracts.ToDictionary(c => c.Id);

        foreach (var invoice in run.Invoices.Where(i =>
                     i.Status is BillingRunInvoiceStatus.Pending
                         or BillingRunInvoiceStatus.Failed
                         or BillingRunInvoiceStatus.Skipped))
        {
            invoice.Status = BillingRunInvoiceStatus.Pending;
            invoice.ErrorMessage = null;

            var persistContracts = invoice.Lines
                .Select(l => l.CustomerContractId)
                .Distinct()
                .Select(id => contractsById.GetValueOrDefault(id))
                .Where(c => c != null)
                .Cast<CustomerContract>()
                .ToList();

            var lineItems = invoice.Lines.Select(line =>
            {
                var months = line.BillingMonthCount > 0 ? line.BillingMonthCount : AnnualBillingHelper.MonthsInTerm(line.Term);
                contractsById.TryGetValue(line.CustomerContractId, out var billedContract);
                var contractRoute = billedContract?.ContractRoutes
                    .FirstOrDefault(cr => cr.Route != null
                        && RouteNaming.NamesMatch(cr.Route.RouteName, line.RouteName));
                var assignment = invoice.Customer.CustomerRoutes?
                    .FirstOrDefault(cr => cr.Route != null
                        && RouteNaming.NamesMatch(cr.Route.RouteName, line.RouteName));
                var content = InvoiceLineFormatter.Build(
                    line.RouteName,
                    invoiceDate,
                    months,
                    line.Amount,
                    assignment,
                    contractRoute?.Route ?? assignment?.Route);
                return new WaveInvoiceLineItem
                {
                    ProductId = contractRoute?.Route?.WaveProductId ?? assignment?.Route?.WaveProductId,
                    ProductName = RouteNaming.DisplayName(line.RouteName),
                    Description = content.Description,
                    Quantity = 1,
                    UnitPrice = line.Amount
                };
            }).ToList();

            var result = await _waveInvoices.ProcessAsync(
                invoice.Customer,
                persistContracts,
                lineItems,
                invoiceDate,
                memo,
                cancellationToken);

            if (result.Success)
            {
                invoice.Status = BillingRunInvoiceStatus.Submitted;
                invoice.WaveCustomerId = result.WaveCustomerId ?? invoice.Customer.WaveCustomerId;
                invoice.WaveInvoiceId = result.Invoice?.WaveInvoiceId
                    ?? persistContracts.Select(c => c.WaveInvoiceId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
                invoice.WaveInvoiceNumber = result.Invoice?.InvoiceNumber
                    ?? persistContracts.Select(c => c.WaveInvoiceNumber).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                invoice.WaveInvoiceUrl = result.Invoice?.WaveInvoiceUrl;
                invoice.ErrorMessage = null;

                await AdvanceContractBillDatesAsync(invoice.Lines.Select(l => l.CustomerContractId).Distinct(), cancellationToken);
            }
            else if (result.SessionExpired)
            {
                invoice.Status = BillingRunInvoiceStatus.Pending;
                invoice.ErrorMessage = null;
                throw new InvalidOperationException(WaveSessionService.SessionExpiredReconnectMessage);
            }
            else
            {
                invoice.Status = BillingRunInvoiceStatus.Failed;
                invoice.WaveCustomerId = result.WaveCustomerId ?? invoice.Customer.WaveCustomerId;
                invoice.WaveInvoiceId = result.Invoice?.WaveInvoiceId;
                invoice.WaveInvoiceNumber = result.Invoice?.InvoiceNumber;
                invoice.WaveInvoiceUrl = result.Invoice?.WaveInvoiceUrl;
                invoice.ErrorMessage = result.Message;
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
    /// Creates a Wave invoice for one contract (customer, draft, approve, email if Email/Both, save PDF),
    /// marks it submitted, and advances NextBillDate by the contract term.
    /// </summary>
    public async Task<(bool Success, string Message, int? InvoiceId)> CreateOnSendAsync(
        int year,
        int month,
        int customerContractId,
        CancellationToken cancellationToken = default)
    {
        if (!_waveSession.IsReady)
        {
            return (false, _waveSession.NeedsBusinessReset
                ? WaveSessionService.SessionExpiredReconnectMessage
                : "Connect to Wave on the Admin Wave proof page before sending invoices.", null);
        }

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

        var contract = await _context.CustomerContracts
            .Include(c => c.Customer)
                .ThenInclude(c => c.Contacts)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
            .FirstOrDefaultAsync(c => c.Id == customerContractId, cancellationToken);
        if (contract == null)
            return (false, "Contract was not found.", invoice.Id);

        var invoiceDate = new DateOnly(year, month, 1);
        var memo = $"AdRack route billing — {BillingDueCalculator.PeriodLabel(year, month)} — {contract.ContractName}";
        var result = await _waveInvoices.ProcessContractAsync(contract, invoiceDate, memo, cancellationToken);
        if (!result.Success)
        {
            if (result.SessionExpired)
                return (false, result.Message, invoice.Id);

            invoice.Status = BillingRunInvoiceStatus.Failed;
            invoice.ErrorMessage = result.Message;
            invoice.WaveCustomerId = result.WaveCustomerId ?? contract.Customer.WaveCustomerId;
            invoice.WaveInvoiceId = result.Invoice?.WaveInvoiceId;
            invoice.WaveInvoiceNumber = result.Invoice?.InvoiceNumber;
            invoice.WaveInvoiceUrl = result.Invoice?.WaveInvoiceUrl;
            await UpdateRunStatusAsync(invoice.BillingRunId, cancellationToken);
            return (false, result.Message, invoice.Id);
        }

        invoice.Status = BillingRunInvoiceStatus.Submitted;
        invoice.ErrorMessage = null;
        invoice.WaveCustomerId = result.WaveCustomerId ?? contract.Customer.WaveCustomerId;
        invoice.WaveInvoiceId = result.Invoice?.WaveInvoiceId ?? contract.WaveInvoiceId;
        invoice.WaveInvoiceNumber = FirstNonEmpty(
            result.Invoice?.InvoiceNumber,
            contract.WaveInvoiceNumber);
        invoice.WaveInvoiceUrl = result.Invoice?.WaveInvoiceUrl;

        await AdvanceContractBillDatesAsync(new[] { customerContractId }, cancellationToken);
        await UpdateRunStatusAsync(invoice.BillingRunId, cancellationToken);

        var nextBill = contract.NextBillDate.ToString("MMM d, yyyy");

        _logger.LogInformation(
            "Create On Send completed. Invoice ID {InvoiceId} for contract {ContractId} period {Period}; Wave {WaveInvoiceNumber}; next bill {NextBillDate}.",
            invoice.Id,
            customerContractId,
            BillingDueCalculator.PeriodLabel(year, month),
            invoice.WaveInvoiceNumber ?? "—",
            nextBill);

        return (true, $"{result.Message} Invoice ID: {invoice.Id}. Next bill date: {nextBill}.", invoice.Id);
    }

    public async Task<(bool Success, string Message)> ResetInvoiceForResendAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _context.BillingRunInvoices
            .Include(i => i.Customer)
            .Include(i => i.BillingRun)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            return (false, "Invoice not found.");

        if (invoice.Status is BillingRunInvoiceStatus.Pending or BillingRunInvoiceStatus.Failed)
            return (false, $"Invoice {invoice.Id} is already available to send.");

        if (invoice.Status is not (BillingRunInvoiceStatus.Submitted
            or BillingRunInvoiceStatus.Received
            or BillingRunInvoiceStatus.Canceled))
            return (false, $"Invoice {invoice.Id} cannot be reset from {invoice.Status}.");

        await RewindContractBillDatesAsync(invoice.Lines, cancellationToken);

        invoice.Status = BillingRunInvoiceStatus.Pending;
        invoice.ErrorMessage = null;
        invoice.WaveInvoiceId = null;
        invoice.WaveInvoiceNumber = null;
        invoice.WaveInvoiceUrl = null;
        invoice.ReceivedDate = null;

        await UpdateRunStatusAsync(invoice.BillingRunId, cancellationToken);

        var period = BillingDueCalculator.PeriodLabel(invoice.BillingRun.Year, invoice.BillingRun.Month);
        var nextBills = invoice.Lines
            .Select(l => l.CustomerContractId)
            .Distinct()
            .ToList();
        var nextBillLabel = "updated";
        if (nextBills.Count == 1)
        {
            var contract = await _context.CustomerContracts.FindAsync(new object[] { nextBills[0] }, cancellationToken);
            if (contract != null)
                nextBillLabel = contract.NextBillDate.ToString("MMM d, yyyy");
        }

        _logger.LogInformation(
            "Invoice {InvoiceId} reset for resend ({CustomerName}, {Period}); next bill {NextBillDate}.",
            invoice.Id,
            invoice.Customer.CustomerName,
            period,
            nextBillLabel);

        return (true, $"Invoice {invoice.Id} reset for {invoice.Customer.CustomerName}. Next bill date: {nextBillLabel}. Use Create On Send to send again.");
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
                    BillingMonthCount = item.BillingMonthCount,
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
                BillingMonthCount = item.BillingMonthCount,
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

            contract.NextBillDate = BillingDueCalculator.AdvanceNextBillDate(contract.NextBillDate, contract);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task RewindContractBillDatesAsync(
        IEnumerable<BillingRunInvoiceLine> lines,
        CancellationToken cancellationToken)
    {
        foreach (var group in lines.GroupBy(l => l.CustomerContractId))
        {
            var contract = await _context.CustomerContracts.FindAsync(new object[] { group.Key }, cancellationToken);
            if (contract == null)
                continue;

            var months = group.Max(l =>
                l.BillingMonthCount > 0 ? l.BillingMonthCount : AnnualBillingHelper.MonthsInTerm(l.Term));
            contract.NextBillDate = BillingDueCalculator.RewindNextBillDate(contract.NextBillDate, months);
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public class DueContractItem
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? WaveCustomerId { get; init; }
    public InvoiceReceiptMethod InvoiceReceiptMethod { get; init; } = InvoiceReceiptMethod.Mail;
    public int CustomerContractId { get; init; }
    public string ContractName { get; init; } = string.Empty;
    public BillingFrequency Term { get; init; }
    public int BillingMonthCount { get; init; }
    public DateOnly NextBillDate { get; init; }
    public int ServiceMonthMask { get; init; }
    public int RouteId { get; init; }
    public string RouteName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}
