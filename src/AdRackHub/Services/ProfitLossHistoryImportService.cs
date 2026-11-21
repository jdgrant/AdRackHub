using System.Globalization;
using AdRackHub.Data;
using AdRackHub.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class ProfitLossHistoryImportService
{
    public const string FileName = "ADRACK_Detailed_PL_2024_2026_Comparisons.xlsx";

    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly HashSet<string> TotalCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Total Income",
        "Gross Profit",
        "Total Expense",
        "Net Ordinary Income",
        "Total Other Income",
        "Total Other Expense",
        "Net Other Income",
        "Net Income"
    };

    private static readonly HashSet<string> ResultCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Net Ordinary Income",
        "Net Other Income",
        "Net Income"
    };

    private readonly ApplicationDbContext _context;

    public ProfitLossHistoryImportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProfitLossImportResult> ReplaceFromWorkbookAsync(
        string contentRoot,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(contentRoot, "Data", "Imports", FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("Profit and loss spreadsheet not found.", path);

        var lines = ReadWorkbook(path);
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            foreach (var line in lines)
                line.Id = 0;
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await _context.ProfitLossHistoryLines.ExecuteDeleteAsync(cancellationToken);
            _context.ProfitLossHistoryLines.AddRange(lines);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return new ProfitLossImportResult(
            lines.Count,
            lines.Select(line => line.Year).Distinct().Count(),
            lines.Select(line => (line.Year, line.Month)).Distinct().Count());
    }

    public static List<ProfitLossHistoryLine> ReadWorkbook(string path)
    {
        using var workbook = new XLWorkbook(path);
        var lines = new List<ProfitLossHistoryLine>();
        var sort = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sourceFile = Path.GetFileName(path);
        foreach (var sheet in workbook.Worksheets)
        {
            if (!TrySheetYear(sheet.Name, out var year))
                continue;
            ReadSheet(sheet, year, sourceFile, sort, lines);
        }

        return lines;
    }

    public static bool TrySheetYear(string name, out int year)
    {
        var text = name.Trim();
        const string suffix = " YTD";
        if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            text = text[..^suffix.Length].Trim();
        if (text.Length != 4
            || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out year))
        {
            year = 0;
            return false;
        }

        return year is >= 2000 and <= 2100;
    }

    private static void ReadSheet(
        IXLWorksheet sheet,
        int year,
        string sourceFile,
        Dictionary<string, int> sort,
        List<ProfitLossHistoryLine> lines)
    {
        var categoryColumn = 1;
        var monthColumns = new Dictionary<int, int>();
        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            var label = cell.GetString().Trim();
            if (label.Equals("Category", StringComparison.OrdinalIgnoreCase))
            {
                categoryColumn = cell.Address.ColumnNumber;
                continue;
            }

            var month = Array.FindIndex(MonthNames, name => name.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (month >= 0)
                monthColumns[month + 1] = cell.Address.ColumnNumber;
        }

        if (monthColumns.Count == 0)
            return;

        var section = "";
        var otherExpenses = false;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var category = row.Cell(categoryColumn).GetString().Trim();
            if (string.IsNullOrWhiteSpace(category))
                continue;
            if (IsSectionHeader(category, ref section, ref otherExpenses))
                continue;
            if (string.IsNullOrEmpty(section))
                continue;

            var lineSection = SectionFor(category, section, otherExpenses);
            if (!sort.ContainsKey(category))
                sort[category] = sort.Count + 1;
            var kind = TotalCategories.Contains(category) ? ProfitLossLineKinds.Total : ProfitLossLineKinds.Line;
            foreach (var (month, column) in monthColumns)
            {
                if (!TryMoney(row.Cell(column), out var amount))
                    continue;
                lines.Add(new ProfitLossHistoryLine
                {
                    Year = year,
                    Month = month,
                    Section = lineSection,
                    LineKind = kind,
                    Category = category,
                    Amount = amount,
                    SortOrder = sort[category],
                    SourceFile = sourceFile
                });
            }

            if (category.Equals("Total Other Income", StringComparison.OrdinalIgnoreCase))
                otherExpenses = true;
        }
    }

    private static bool IsSectionHeader(string category, ref string section, ref bool otherExpenses)
    {
        if (category.Equals("INCOME", StringComparison.OrdinalIgnoreCase))
        {
            section = ProfitLossSections.Income;
            return true;
        }

        if (category.Equals("EXPENSES", StringComparison.OrdinalIgnoreCase))
        {
            section = ProfitLossSections.Expense;
            return true;
        }

        if (category.Equals("OTHER INCOME / EXPENSE", StringComparison.OrdinalIgnoreCase))
        {
            section = ProfitLossSections.OtherIncome;
            otherExpenses = false;
            return true;
        }

        return false;
    }

    private static string SectionFor(string category, string section, bool otherExpenses)
    {
        if (ResultCategories.Contains(category))
            return ProfitLossSections.Result;
        if (section == ProfitLossSections.OtherIncome && otherExpenses)
            return ProfitLossSections.OtherExpense;
        return section;
    }

    private static bool TryMoney(IXLCell cell, out decimal amount)
    {
        amount = 0;
        var value = cell.Value;
        if (value.IsBlank)
            return false;
        if (!value.IsNumber)
            return false;
        amount = Math.Round((decimal)value.GetNumber(), 2, MidpointRounding.AwayFromZero);
        return true;
    }
}

public record ProfitLossImportResult(int Lines, int Years, int Months);
