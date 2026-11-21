using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

/// <summary>
/// One priced route on a past contract, loaded from the contract history spreadsheet.
/// </summary>
public class ContractHistoryLine
{
    public int Id { get; set; }

    public int LineNumber { get; set; }

    public int? CustomerId { get; set; }

    [StringLength(200)]
    public string? HubName { get; set; }

    [StringLength(40)]
    public string? RecordType { get; set; }

    [StringLength(40)]
    public string? RecordStatus { get; set; }

    [StringLength(20)]
    public string? MatchQuality { get; set; }

    [Required]
    [StringLength(300)]
    public string ContractName { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Product { get; set; }

    public int? RouteId { get; set; }

    [StringLength(200)]
    public string? RouteName { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MonthlyAmount { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [StringLength(80)]
    public string? Term { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? ContractMonthlyTotal { get; set; }

    [StringLength(20)]
    public string? Signed { get; set; }

    public int? Year { get; set; }

    [StringLength(300)]
    public string? SourceFile { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    [StringLength(1000)]
    public string? Review { get; set; }

    public Customer? Customer { get; set; }

    public Route? Route { get; set; }
}
