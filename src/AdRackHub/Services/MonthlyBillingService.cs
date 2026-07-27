using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class MonthlyBillingService
{
    private readonly ApplicationDbContext _context;
    private readonly WaveApiService _waveApiService;
    private readonly WaveSyncService _waveSyncService;

    public MonthlyBillingService(
        ApplicationDbContext context,
        WaveApiService waveApiService,
        WaveSyncService waveSyncService)
    {
        _context = context;
        _waveApiService = waveApiService;
        _waveSyncService = waveSyncService;
    }

    public Task<List<DueContractItem>> GetDueContractsAsync(int year, int month, CancellationToken cancellationToken = default) =>
        GetDueBillingsAsync(year, month, cancellationToken);

    public async Task<List<DueContractItem>> GetDueBillingsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var contracts = await _context.CustomerContracts
            .Include(c => c.Customer)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
            .Where(c => c.Customer.Status == CustomerStatus.Active && c.Customer.Type == CustomerType.Customer)
            .Where(c => c.ContractRoutes.Any())
            .ToListAsync(cancellationToken);

        return contracts
            .Where(c => BillingDueCalculator.IsContractDue(c, year, month))
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

    public async Task<BillingRun?> GetRunAsync(int year, int month, CancellationToken cancellationToken = default) =>
        await _context.BillingRuns
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Customer)
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Lines)
            .FirstOrDefaultAsync(r => r.Year == year && r.Month == month, cancellationToken);

    public Task<List<ConfiguredContractRow>> GetAllConfiguredContractsAsync(int year, int month, CancellationToken cancellationToken = default) =>
        GetAllConfiguredBillsAsync(year, month, cancellationToken);

    public async Task<List<ConfiguredContractRow>> GetAllConfiguredBillsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var contracts = await _context.CustomerContracts
            .Include(c => c.Customer)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
            .Where(c => c.Customer.Status == CustomerStatus.Active && c.Customer.Type == CustomerType.Customer)
            .OrderBy(c => c.Customer.CustomerName)
            .ThenBy(c => c.ContractName)
            .ToListAsync(cancellationToken);

        return contracts.Select(c => new ConfiguredContractRow
        {
            CustomerContractId = c.Id,
            CustomerId = c.CustomerId,
            CustomerName = c.Customer.CustomerName,
            WaveCustomerId = c.Customer.WaveCustomerId,
            ContractName = c.ContractName,
            Term = c.Term,
            BillingAnchorMonth = c.BillingAnchorMonth,
            ServiceMonthMask = c.ServiceMonthMask,
            ContractEndDate = c.ContractEndDate,
            NextBillDate = c.NextBillDate,
            RouteNames = c.ContractRoutes.Select(cr => cr.Route.RouteName).OrderBy(n => n).ToList(),
            Total = c.ContractRoutes.Sum(AnnualBillingHelper.GetBillingAmount),
            IsDueThisPeriod = c.ContractRoutes.Any() && BillingDueCalculator.IsContractDue(c, year, month)
        }).ToList();
    }

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
        run.Status = run.Invoices.All(i => i.Status is BillingRunInvoiceStatus.Submitted or BillingRunInvoiceStatus.Skipped)
            ? BillingRunStatus.Submitted
            : run.Invoices.Any(i => i.Status == BillingRunInvoiceStatus.Submitted)
                ? BillingRunStatus.PartiallySubmitted
                : BillingRunStatus.Failed;

        await _context.SaveChangesAsync(cancellationToken);
        return run;
    }

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
