using AdRackHub.Data;
using AdRackHub.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class ContractHistoryImportService
{
    public const string FileName = "ContractInventory.xlsx";
    public const string SheetName = "Import";

    private readonly ApplicationDbContext _context;

    public ContractHistoryImportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ContractHistoryImportResult> ReplaceFromWorkbookAsync(
        string contentRoot,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(contentRoot, "Data", "Imports", FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("Contract history spreadsheet not found.", path);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(SheetName);
        var headers = sheet.Row(1).CellsUsed()
            .ToDictionary(cell => cell.GetString().Trim(), cell => cell.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);

        var customerIds = (await _context.Customers.Select(customer => customer.Id).ToListAsync(cancellationToken)).ToHashSet();
        var routeIds = (await _context.Routes.Select(route => route.Id).ToListAsync(cancellationToken)).ToHashSet();
        var lines = new List<ContractHistoryLine>();
        var skipped = 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var contractName = Text(row, headers, "Contract Name");
            if (string.IsNullOrWhiteSpace(contractName))
            {
                skipped++;
                continue;
            }

            var customerId = WholeNumber(row, headers, "Customer Id");
            var routeId = WholeNumber(row, headers, "Route Id");
            lines.Add(new ContractHistoryLine
            {
                LineNumber = WholeNumber(row, headers, "Line Id") ?? rowNumber - 1,
                CustomerId = customerId is int id && customerIds.Contains(id) ? id : null,
                HubName = Clip(Text(row, headers, "Hub Name"), 200),
                RecordType = Clip(Text(row, headers, "Record Type"), 40),
                RecordStatus = Clip(Text(row, headers, "Record Status"), 40),
                MatchQuality = Clip(Text(row, headers, "Match"), 20),
                ContractName = Clip(contractName, 300) ?? contractName,
                Product = Clip(Text(row, headers, "Product"), 20),
                RouteId = routeId is int route && routeIds.Contains(route) ? route : null,
                RouteName = Clip(Text(row, headers, "Route"), 200),
                MonthlyAmount = Money(row, headers, "Monthly Amount"),
                StartDate = Day(row, headers, "Start Date"),
                EndDate = Day(row, headers, "End Date"),
                Term = Clip(Text(row, headers, "Term"), 80),
                ContractMonthlyTotal = Money(row, headers, "Contract Monthly Total"),
                Signed = Clip(Text(row, headers, "Signed"), 20),
                Year = WholeNumber(row, headers, "Year"),
                SourceFile = Clip(Text(row, headers, "Source File"), 300),
                Notes = Clip(Text(row, headers, "Notes"), 1000),
                Review = Clip(Text(row, headers, "Review"), 1000)
            });
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            foreach (var line in lines)
                line.Id = 0;
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await _context.ContractHistoryLines.ExecuteDeleteAsync(cancellationToken);
            _context.ContractHistoryLines.AddRange(lines);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        return new ContractHistoryImportResult(lines.Count, skipped, lines.Count(line => line.CustomerId != null));
    }

    private static string? Text(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var column))
            return null;
        var cell = row.Cell(column);
        if (cell.IsEmpty())
            return null;
        var value = cell.DataType == XLDataType.DateTime
            ? cell.GetDateTime().ToString("yyyy-MM-dd")
            : cell.GetString();
        var text = value.Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static int? WholeNumber(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var column))
            return null;
        var cell = row.Cell(column);
        if (cell.IsEmpty())
            return null;
        if (cell.DataType == XLDataType.Number && cell.TryGetValue(out double number))
            return (int)Math.Round(number);
        var text = cell.GetString().Trim();
        return int.TryParse(text, out var parsed) ? parsed : null;
    }

    private static decimal? Money(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var column))
            return null;
        var cell = row.Cell(column);
        if (cell.IsEmpty())
            return null;
        if (cell.DataType == XLDataType.Number && cell.TryGetValue(out double number))
            return Math.Round((decimal)number, 2);
        var text = cell.GetString().Trim().TrimStart('$').Replace(",", "");
        return decimal.TryParse(text, out var parsed) ? Math.Round(parsed, 2) : null;
    }

    private static DateOnly? Day(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var column))
            return null;
        var cell = row.Cell(column);
        if (cell.IsEmpty())
            return null;
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue(out DateTime date))
            return DateOnly.FromDateTime(date);
        if (cell.DataType == XLDataType.Number && cell.TryGetValue(out double serial))
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return DateOnly.TryParse(cell.GetString().Trim(), out var parsed) ? parsed : null;
    }
}

public record ContractHistoryImportResult(int Imported, int Skipped, int WithCustomer);
