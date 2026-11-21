using System.ComponentModel.DataAnnotations;
using AdRackHub.Models;

namespace AdRackHub.ViewModels;

public class CustomerRouteEditViewModel
{
    public CustomerRoute CustomerRoute { get; set; } = new();
    public List<StopSelectionItem> AvailableStops { get; set; } = new();
    public List<int> SelectedStopIds { get; set; } = new();
    public List<int> SelectedMonthNumbers { get; set; } = Enumerable.Range(1, 12).ToList();
}

public class CustomerContractEditViewModel
{
    public CustomerContract Contract { get; set; } = new();

    [Display(Name = "Submit to billing")]
    public bool SubmitToBilling { get; set; } = true;

    public List<RouteSelectionItem> AvailableRoutes { get; set; } = new();
    public List<int> SelectedRouteIds { get; set; } = new();
    public Dictionary<int, decimal> RouteBillingAmounts { get; set; } = new();
    public List<int> SelectedMonthNumbers { get; set; } = Enumerable.Range(1, 12).ToList();
}

public class RouteSelectionItem
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal DefaultBillingAmount { get; set; }
    public decimal BillingAmount { get; set; }
    public BillingFrequency BillingFrequency { get; set; }
    public bool IsSelected { get; set; }
    public bool AllStops { get; set; } = true;
    public List<StopSelectionItem> Stops { get; set; } = new();
}

public class RouteStopSelection
{
    public bool AllStops { get; set; } = true;
    public List<int> StopIds { get; set; } = new();
}

public class StopSelectionItem
{
    public int StopId { get; set; }
    public string StopName { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

public class DashboardViewModel
{
    public int ActiveCustomerCount { get; set; }
    public int ProspectCount { get; set; }
    public int HighValueProspectCount { get; set; }
    public int AtRiskCustomerCount { get; set; }
    public int ActiveRouteCount { get; set; }
    public int ActiveStopCount { get; set; }

    public List<DashboardCustomerItem> AtRiskCustomers { get; set; } = new();
    public List<DashboardCustomerItem> HighValueProspects { get; set; } = new();
    public List<RouteSummaryItem> Routes { get; set; } = new();
    public int PipelineDays { get; set; } = 5;
    public string PipelineScope { get; set; } = "customers";
    public bool PipelineShowProspects =>
        string.Equals(PipelineScope, "prospects", StringComparison.OrdinalIgnoreCase);
    public List<DashboardPipelineItem> PipelineNew { get; set; } = new();
    public List<DashboardPipelineItem> PipelineWorking { get; set; } = new();
    public List<DashboardPipelineItem> PipelineDone { get; set; } = new();
}

public class DashboardPipelineItem
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public CustomerType CustomerType { get; set; }
    public CustomerNoteKind Kind { get; set; }
    public CustomerNoteStatus Status { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Preview { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
}

public class DashboardCustomerItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CustomerStatus Status { get; set; }
    public CustomerType Type { get; set; }
}

public class RouteSummaryItem
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public decimal AnnualRevenue { get; set; }
    public BillingFrequency BillingFrequency { get; set; }
    public int StopCount { get; set; }
    public int CustomerCount { get; set; }
}

public class CustomerIndexViewModel
{
    public List<Customer> Customers { get; set; } = new();
    public CustomerIndexSummary Summary { get; set; } = new();
    public CustomerType ListType { get; set; } = CustomerType.Customer;
    public List<ContractedRouteCustomerGroup> ContractedCustomersByRoute { get; set; } = new();
}

public class ContractedRouteCustomerGroup
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public List<ContractedCustomerOption> Customers { get; set; } = new();
}

public class ContractedCustomerOption
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
}

public class CustomerBillingsTabViewModel
{
    public const string StatusOutstanding = "outstanding";
    public const string StatusReceived = "received";

    public int CustomerId { get; set; }
    public string? BillingStatus { get; set; }
    public List<BillingRunInvoice> Invoices { get; set; } = new();
    public int AllCount { get; set; }
    public int NotReceivedCount { get; set; }
    public int ReceivedCount { get; set; }
    public decimal AllAmount { get; set; }
    public decimal NotReceivedAmount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public bool WaveConnected { get; set; }
    public string WaveActionBlockedReason { get; set; } = string.Empty;
    public HashSet<int> InvoicePdfIds { get; set; } = new();
}

public class CustomerIndexSummary
{
    public int ClientCount { get; set; }
    public int RouteCount { get; set; }
    public decimal TotalRevenue { get; set; }

    /// <summary>Clients with at least one contract — used for average revenue.</summary>
    public int ClientsWithContracts { get; set; }
    public int HighValueCount { get; set; }
    public int NeedsMoreInfoCount { get; set; }

    public decimal AverageRevenuePerClient =>
        ClientsWithContracts > 0 ? TotalRevenue / ClientsWithContracts : 0;
}

public class ExpandedProspectsIndexViewModel
{
    public List<ExpandedProspectRouteGroup> Groups { get; set; } = new();
    public List<RouteOptionItem> Routes { get; set; } = new();
    public int? SelectedRouteId { get; set; }
    public int TotalCount { get; set; }
    public int UnassignedCount { get; set; }
    public int RouteCountWithProspects { get; set; }
}

public class ExpandedProspectRouteGroup
{
    public int? RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public List<Customer> Prospects { get; set; } = new();
}

public class BillingTermPickerViewModel
{
    public string MonthCountInputName { get; init; } = string.Empty;
    public string MonthCountInputId { get; init; } = "billingMonthCount";
    public int MonthCount { get; init; } = 3;
    public bool ShowUseDates { get; init; }
}

public class RouteOptionItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class RouteAssignCustomerViewModel
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Customer")]
    public int CustomerId { get; set; }
    public bool AllStops { get; set; } = true;
    public CustomerRouteStatus Status { get; set; } = CustomerRouteStatus.Active;

    [Display(Name = "Billing Term")]
    public BillingFrequency BillingTerm { get; set; } = BillingFrequency.Quarterly;

    [Range(1, 36)]
    [Display(Name = "Number of Months")]
    public int BillingMonthCount { get; set; } = 3;

    [Range(0, double.MaxValue)]
    [Display(Name = "Rate per Month")]
    public decimal RatePerMonth { get; set; }

    public List<int> SelectedMonthNumbers { get; set; } = Enumerable.Range(1, 12).ToList();
    public List<StopSelectionItem> AvailableStops { get; set; } = new();
    public List<int> SelectedStopIds { get; set; } = new();
}

public class ReportsIndexViewModel
{
    public List<BrochureLabelClientRow> LabelClients { get; set; } = new();
    public List<BrochureLabelRouteOption> LabelRoutes { get; set; } = new();
}

public class BrochureLabelRouteOption
{
    public int Id { get; set; }
    public string RouteName { get; set; } = string.Empty;
}

public class BrochureLabelClientRow
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? BrochureCode { get; set; }
    public DateTime? LastUpdated { get; set; }
    public List<int> RouteIds { get; set; } = new();
    public List<string> RouteNames { get; set; } = new();
}

public class ContractHistoryKpiColumn
{
    public int Key { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool Debug { get; set; }
}

public class ContractHistoryKpiPage
{
    public string Product { get; set; } = "all";
    public List<int> Years { get; set; } = new();
    public List<ContractHistoryKpiColumn> Columns { get; set; } = new();
    public int LatestYear { get; set; }
    public int? PriorYear { get; set; }
    public List<ContractHistoryKpiMonthRow> Months { get; set; } = new();
    public ContractHistoryKpiMonthRow YearTotal { get; set; } = new();
    public List<ContractHistoryKpiRouteRow> Routes { get; set; } = new();
    public int LinesWithoutDates { get; set; }

    public bool IsHotel => Product == "hotel";
    public bool IsRest => Product == "rest";
}

public class ContractHistoryKpiRouteRow
{
    public string RouteName { get; set; } = string.Empty;
    public List<ContractHistoryKpiMonthRow> Months { get; set; } = new();
    public ContractHistoryKpiMonthRow YearTotal { get; set; } = new();
}

public class ContractHistoryKpiMonthRow
{
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public List<ContractHistoryKpiCell> Years { get; set; } = new();
    public int? ClientChange { get; set; }
    public decimal? RevenueChange { get; set; }
}

public class ContractHistoryKpiCell
{
    public int Clients { get; set; }
    public decimal Revenue { get; set; }
}

public class ContractHistoryKpiTable
{
    public List<int> Years { get; set; } = new();
    public List<ContractHistoryKpiColumn> Columns { get; set; } = new();
    public int LatestYear { get; set; }
    public int? PriorYear { get; set; }
    public int CurrentMonth { get; set; }
    public List<ContractHistoryKpiMonthRow> Months { get; set; } = new();
    public ContractHistoryKpiMonthRow YearTotal { get; set; } = new();
}

public class ProfitLossRevenuePage
{
    public string? SourceFile { get; set; }
    public int LineCount { get; set; }
    public List<int> Years { get; set; } = new();
    public List<ProfitLossRevenueColumn> Columns { get; set; } = new();
    public int LatestYear { get; set; }
    public int CurrentMonth { get; set; }
    public List<ProfitLossYearSummary> Summaries { get; set; } = new();
    public List<ProfitLossRevenueMonthRow> Months { get; set; } = new();
    public ProfitLossRevenueMonthRow Total { get; set; } = new();
    public List<string> BillingOnlyMonths { get; set; } = new();
    public List<ProfitLossStatementRow> Statement { get; set; } = new();
}

public class ProfitLossRevenueColumn
{
    public int Year { get; set; }
    public bool Latest { get; set; }
    public bool IncludeYoy { get; set; }
    public bool IncludeHub { get; set; }
}

public class ProfitLossYearSummary
{
    public int Year { get; set; }
    public bool HasSales { get; set; }
    public decimal Sales { get; set; }
    public string? PeriodLabel { get; set; }
    public decimal? VersusPriorAmount { get; set; }
    public decimal? VersusPriorPercent { get; set; }
    public bool HasBilling { get; set; }
    public decimal Billed { get; set; }
    public decimal Received { get; set; }
}

public class ProfitLossRevenueMonthRow
{
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public List<ProfitLossRevenueYearCell> Years { get; set; } = new();
}

public class ProfitLossRevenueYearCell
{
    public int Year { get; set; }
    public bool IncludeYoy { get; set; }
    public bool IncludeHub { get; set; }
    public bool HasSales { get; set; }
    public decimal Sales { get; set; }
    public bool HasMom { get; set; }
    public decimal? MomAmount { get; set; }
    public decimal? MomPercent { get; set; }
    public bool HasYoy { get; set; }
    public decimal? YoyAmount { get; set; }
    public decimal? YoyPercent { get; set; }
    public string? PeriodLabel { get; set; }
    public string? ComparisonLabel { get; set; }
    public bool HasBilling { get; set; }
    public decimal Billed { get; set; }
    public decimal Received { get; set; }
}

public class ProfitLossStatementRow
{
    public string Label { get; set; } = string.Empty;
    public List<ProfitLossStatementCell> Years { get; set; } = new();
}

public class ProfitLossStatementCell
{
    public int Year { get; set; }
    public bool HasAmount { get; set; }
    public decimal Amount { get; set; }
    public string? PeriodLabel { get; set; }
    public bool HasComparison { get; set; }
    public decimal? VersusPriorAmount { get; set; }
    public decimal? VersusPriorPercent { get; set; }
}
