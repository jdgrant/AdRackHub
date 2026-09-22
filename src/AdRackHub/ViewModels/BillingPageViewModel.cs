using AdRackHub.Models;
using AdRackHub.Services;

namespace AdRackHub.ViewModels;

public class BillingPageViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public bool WaveConfigured { get; set; }
    public bool CreateOnSendConfigured { get; set; }
    public bool WaveSessionExpired { get; set; }
    public BillingRun? Run { get; set; }
    public List<DueContractItem> DueContracts { get; set; } = new();
    public List<BillingRunInvoice> SubmittedInvoices { get; set; } = new();
    public HashSet<int> ContractsWithInvoicePdf { get; set; } = new();
    public HashSet<int> InvoicesWithInvoicePdf { get; set; } = new();
    public List<int> BatchPdfContractIds { get; set; } = new();
    public List<MissingBillingContactAlert> MissingBillingContacts { get; set; } = new();

    public string? MissingBillingContactIssue(int customerId) =>
        MissingBillingContacts.FirstOrDefault(c => c.CustomerId == customerId && !c.IsEmailWarning)?.Issue;

    public string? MissingBillingEmailIssue(int customerId) =>
        MissingBillingContacts.FirstOrDefault(c => c.CustomerId == customerId && c.IsEmailWarning)?.Issue;

    public IReadOnlyList<MissingBillingContactAlert> MissingBillingEmailAlerts =>
        MissingBillingContacts.Where(c => c.IsEmailWarning).ToList();

    public IReadOnlyList<DueContractSummary> DueContractSummaries => DueContracts
        .GroupBy(i => i.CustomerContractId)
        .Select(g =>
        {
            var first = g.First();
            return new DueContractSummary
            {
                CustomerId = first.CustomerId,
                CustomerName = first.CustomerName,
                WaveCustomerId = first.WaveCustomerId,
                InvoiceReceiptMethod = first.InvoiceReceiptMethod,
                CustomerContractId = first.CustomerContractId,
                ContractName = first.ContractName,
                Term = first.Term,
                BillingMonthCount = first.BillingMonthCount,
                RouteNames = g.Select(i => i.RouteName).Distinct().OrderBy(n => n).ToList(),
                Products = g.Select(i => RouteProductHelper.LabelForRouteName(i.RouteName)).Distinct().OrderBy(p => p).ToList(),
                Amount = g.Sum(i => i.Amount)
            };
        })
        .OrderBy(s => s.CustomerName)
        .ThenBy(s => s.ContractName)
        .ToList();

    public decimal DueTotal => DueContracts.Sum(i => i.Amount);
    public int DueCustomerCount => DueContracts.Select(i => i.CustomerId).Distinct().Count();
    public int DueContractCount => DueContracts.Select(i => i.CustomerContractId).Distinct().Count();

    public bool HasDueContracts => DueContracts.Any();
    public bool HasDueItems => HasDueContracts;
    public bool CanPrepare => Run == null && HasDueContracts;
    public bool CanSendToWave => WaveConfigured && HasDueContracts && !IsFullySubmitted
        && (Run == null || Run.Invoices.Any(i =>
            i.Status is BillingRunInvoiceStatus.Pending or BillingRunInvoiceStatus.Failed));
    public bool IsFullySubmitted => Run != null
        && Run.Invoices.Any()
        && Run.Invoices.All(i => i.Status is BillingRunInvoiceStatus.Submitted
            or BillingRunInvoiceStatus.Received
            or BillingRunInvoiceStatus.Canceled
            or BillingRunInvoiceStatus.Skipped);
}

public class DueContractSummary
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? WaveCustomerId { get; init; }
    public InvoiceReceiptMethod InvoiceReceiptMethod { get; init; } = InvoiceReceiptMethod.Mail;
    public int CustomerContractId { get; init; }
    public string ContractName { get; init; } = string.Empty;
    public BillingFrequency Term { get; init; }
    public int BillingMonthCount { get; init; }
    public List<string> RouteNames { get; init; } = new();
    public List<string> Products { get; init; } = new();
    public decimal Amount { get; init; }
}

public class MissingBillingContactAlert
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string Issue { get; init; } = string.Empty;
    public bool IsEmailWarning { get; init; }
    public int DueContractCount { get; init; }
    public decimal DueAmount { get; init; }
}
