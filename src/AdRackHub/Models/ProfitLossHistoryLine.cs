using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdRackHub.Models;

/// <summary>
/// One category for one month, loaded from the profit-and-loss workbook.
/// </summary>
public class ProfitLossHistoryLine
{
    public int Id { get; set; }

    public int Year { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    [Required]
    [StringLength(20)]
    public string Section { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string LineKind { get; set; } = ProfitLossLineKinds.Line;

    [Required]
    [StringLength(120)]
    public string Category { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public int SortOrder { get; set; }

    [StringLength(300)]
    public string? SourceFile { get; set; }
}

public static class ProfitLossSections
{
    public const string Income = "Income";
    public const string Expense = "Expense";
    public const string OtherIncome = "OtherIncome";
    public const string OtherExpense = "OtherExpense";
    public const string Result = "Result";
}

public static class ProfitLossLineKinds
{
    public const string Line = "Line";
    public const string Total = "Total";
}
