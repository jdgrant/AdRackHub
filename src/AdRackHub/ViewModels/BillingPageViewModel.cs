using AdRackHub.Models;
using AdRackHub.Services;

namespace AdRackHub.ViewModels;

public class BillingPageViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public bool WaveConfigured { get; set; }
    public BillingRun? Run { get; set; }
    public List<DueContractItem> DueContracts { get; set; } = new();
    public List<ConfiguredContractRow> AllContracts { get; set; } = new();

    public decimal DueTotal => DueContracts.Sum(i => i.Amount);
    public int DueCustomerCount => DueContracts.Select(i => i.CustomerId).Distinct().Count();
    public int DueContractCount => DueContracts.Select(i => i.CustomerContractId).Distinct().Count();

    public bool HasDueContracts => DueContracts.Any();
    public bool HasDueItems => HasDueContracts;
    public bool CanPrepare => Run == null && HasDueContracts;
    public bool CanSendToWave => WaveConfigured && HasDueContracts && !IsFullySubmitted
        && (Run == null || Run.Invoices.Any(i =>
            i.Status is BillingRunInvoiceStatus.Pending or BillingRunInvoiceStatus.Failed));
    public bool IsFullySubmitted => Run?.Status == BillingRunStatus.Submitted;
}

public class ConfiguredContractRow
{
    public int CustomerContractId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? WaveCustomerId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public BillingFrequency Term { get; set; }
    public int BillingAnchorMonth { get; set; }
    public int ServiceMonthMask { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public DateOnly NextBillDate { get; set; }
    public List<string> RouteNames { get; set; } = new();
    public decimal Total { get; set; }
    public bool IsDueThisPeriod { get; set; }
}
