using AdRackHub.Data;
using AdRackHub.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class BrochureWarehouseSheetService
{
    public const string FileName = "brochure-warehouse.xlsx";
    public const string WorksheetName = "Warehouse";
    public const string NameColumn = "Brochure Name";
    public const string WarehouseColumn = "Warehouse";
    public const string RackColumn = "Rack";
    public const string BinColumn = "Bin";
    public const string ShelfColumn = "Top/Bottom";
    public const string QtyColumn = "Qty";

    private readonly ApplicationDbContext _context;

    public BrochureWarehouseSheetService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task ExportAsync(Stream outputStream, CancellationToken cancellationToken = default)
    {
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.Type == CustomerType.Customer)
            .Include(c => c.BrochureInventories)
            .Include(c => c.WarehouseLocations)
            .OrderBy(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        WriteInstructions(workbook);
        var sheet = workbook.Worksheets.Add(WorksheetName);

        sheet.Cell(1, 1).Value = NameColumn;
        sheet.Cell(1, 2).Value = WarehouseColumn;
        sheet.Cell(1, 3).Value = RackColumn;
        sheet.Cell(1, 4).Value = BinColumn;
        sheet.Cell(1, 5).Value = ShelfColumn;
        sheet.Cell(1, 6).Value = QtyColumn;
        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        var row = 2;
        foreach (var customer in customers)
        {
            var latest = customer.BrochureInventories
                .OrderByDescending(i => i.InventoryDate)
                .ThenByDescending(i => i.Id)
                .FirstOrDefault();
            var locations = customer.WarehouseLocations
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Id)
                .ToList();
            if (locations.Count == 0)
            {
                WriteLocationRow(sheet, row, customer.CustomerName, customer.Warehouse, customer.WarehouseRack, customer.WarehouseBin, customer.WarehouseShelf, latest?.Quantity);
                row++;
                continue;
            }

            foreach (var location in locations)
            {
                var qty = customer.BrochureInventories
                    .Where(i => WarehouseLocation.Matches(
                        i.Warehouse, i.Rack, i.Bin, i.Shelf,
                        location.Warehouse, location.Rack, location.Bin, location.Shelf))
                    .OrderByDescending(i => i.InventoryDate)
                    .ThenByDescending(i => i.Id)
                    .FirstOrDefault()?.Quantity ?? (location == locations[0] ? latest?.Quantity : null);
                WriteLocationRow(sheet, row, customer.CustomerName, location.Warehouse, location.Rack, location.Bin, location.Shelf, qty);
                row++;
            }
        }

        var lastRow = Math.Max(row - 1, 2);
        var extraRows = lastRow + 40;
        var warehouseRange = sheet.Range(2, 2, extraRows, 2);
        warehouseRange.CreateDataValidation().List("\"K,O\"", true);

        var shelfRange = sheet.Range(2, 5, extraRows, 5);
        shelfRange.CreateDataValidation().List("\"Top,Bottom\"", true);

        var qtyRange = sheet.Range(2, 6, extraRows, 6);
        var qtyValidation = qtyRange.CreateDataValidation();
        qtyValidation.WholeNumber.Between(0, 1_000_000);
        qtyValidation.IgnoreBlanks = true;

        sheet.Range(1, 1, extraRows, 6).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 36);
        workbook.SaveAs(outputStream);
    }

    public async Task<BrochureWarehouseSheetImportResult> ImportAsync(
        Stream inputStream,
        string? loggedBy,
        CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook(inputStream);
        var sheet = workbook.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, WorksheetName, StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.First();

        var header = ReadHeader(sheet);
        var result = new BrochureWarehouseSheetImportResult();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var createdBy = WarehouseLocation.NullIfEmpty(loggedBy) ?? "Import";

        var customers = await _context.Customers
            .Where(c => c.Type == CustomerType.Customer)
            .Include(c => c.WarehouseLocations)
            .ToListAsync(cancellationToken);

        var byName = customers
            .GroupBy(c => Normalize(c.CustomerName))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var name = GetString(sheet.Cell(rowNumber, header.Name));
            if (name == null)
                continue;

            var warehouseText = GetString(sheet.Cell(rowNumber, header.Warehouse));
            var rack = WarehouseLocation.NullIfEmpty(GetString(sheet.Cell(rowNumber, header.Rack)));
            var bin = WarehouseLocation.NullIfEmpty(GetString(sheet.Cell(rowNumber, header.Bin)));
            var shelfText = GetString(sheet.Cell(rowNumber, header.Shelf));
            var qtyCell = sheet.Cell(rowNumber, header.Qty);
            var hasQty = TryReadQty(qtyCell, out var qty);
            var hasLocationInput = warehouseText != null || rack != null || bin != null || shelfText != null;

            if (!hasLocationInput && !hasQty)
                continue;

            result.SourceRows++;

            if (!byName.TryGetValue(Normalize(name), out var customer))
            {
                result.Unmatched.Add(name);
                continue;
            }

            var warehouse = WarehouseLocation.Parse(warehouseText);
            var shelf = WarehouseLocation.ParseShelf(shelfText);
            var locationChanged = false;
            if (hasLocationInput)
            {
                if (warehouseText != null || rack != null || bin != null || shelfText != null)
                {
                    WarehouseLocation.Upsert(customer, warehouse, rack, bin, shelf);
                    locationChanged = true;
                    result.LocationsUpdated++;
                }
            }

            if (hasQty)
            {
                var location = customer.WarehouseLocations
                    .OrderByDescending(l => WarehouseLocation.Matches(
                        l.Warehouse, l.Rack, l.Bin, l.Shelf, warehouse, rack, bin, shelf))
                    .ThenBy(l => l.SortOrder)
                    .ThenBy(l => l.Id)
                    .FirstOrDefault();
                _context.CustomerBrochureInventories.Add(new CustomerBrochureInventory
                {
                    CustomerId = customer.Id,
                    Quantity = qty,
                    InventoryDate = today,
                    Warehouse = location?.Warehouse ?? warehouse ?? customer.Warehouse,
                    Rack = location?.Rack ?? rack ?? customer.WarehouseRack,
                    Bin = location?.Bin ?? bin ?? customer.WarehouseBin,
                    Shelf = location?.Shelf ?? shelf ?? customer.WarehouseShelf,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                });
                result.InventoryLogged++;
            }
            else if (!locationChanged)
            {
                result.Unchanged++;
            }
        }

        if (result.LocationsUpdated > 0 || result.InventoryLogged > 0)
            await _context.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task<BrochureWarehouseSheetImportResult> ImportWalkthroughAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook(inputStream);
        var sheet = workbook.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, "Customers", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.Name, WorksheetName, StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.First();

        var header = ReadWalkthroughHeader(sheet);
        var result = new BrochureWarehouseSheetImportResult();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

        var customers = await _context.Customers
            .Include(c => c.WarehouseLocations)
            .ToListAsync(cancellationToken);

        var byCode = customers
            .Where(c => !string.IsNullOrWhiteSpace(c.BrochureCode))
            .GroupBy(c => Normalize(c.BrochureCode!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byName = customers
            .GroupBy(c => Normalize(c.CustomerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var desired = new Dictionary<int, List<WalkthroughSlot>>();

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var name = GetString(sheet.Cell(rowNumber, header.Name));
            var code = header.Code.HasValue ? GetString(sheet.Cell(rowNumber, header.Code.Value)) : null;
            if (name == null && code == null)
                continue;
            if (IsPlaceholderName(name))
                continue;

            var warehouseText = GetString(sheet.Cell(rowNumber, header.Warehouse));
            var rack = WarehouseLocation.NullIfEmpty(GetString(sheet.Cell(rowNumber, header.Rack)));
            var bin = WarehouseLocation.NullIfEmpty(GetString(sheet.Cell(rowNumber, header.Bin)));
            var shelf = WarehouseLocation.ParseShelf(GetString(sheet.Cell(rowNumber, header.Shelf)));
            if (rack == null && bin == null)
                continue;

            result.SourceRows++;

            Customer? customer = null;
            if (code != null)
                byCode.TryGetValue(code, out customer);
            if (customer == null && name != null)
            {
                byName.TryGetValue(Normalize(name), out customer);
                if (customer == null && Aliases.TryGetValue(Normalize(name), out var alias))
                    byName.TryGetValue(alias, out customer);
            }
            if (customer == null)
            {
                result.Unmatched.Add(string.IsNullOrWhiteSpace(code) ? name! : $"{code} {name}".Trim());
                continue;
            }

            if (!desired.TryGetValue(customer.Id, out var slots))
            {
                slots = new List<WalkthroughSlot>();
                desired[customer.Id] = slots;
            }

            var warehouse = WarehouseLocation.DefaultKy(WarehouseLocation.Parse(warehouseText), rack, bin);
            var slot = new WalkthroughSlot(warehouse, rack, bin, shelf);
            if (!slots.Any(existing => WarehouseLocation.Matches(
                    existing.Warehouse, existing.Rack, existing.Bin, existing.Shelf,
                    slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf)))
            {
                slots.Add(slot);
            }
        }

        foreach (var (customerId, slots) in desired)
        {
            var customer = customers.First(c => c.Id == customerId);
            var removed = ReplaceLocations(customer, slots);
            result.LocationsUpdated += slots.Count;
            result.LocationsRemoved += removed;
        }

        if (result.LocationsUpdated > 0 || result.LocationsRemoved > 0)
            await _context.SaveChangesAsync(cancellationToken);

        result.CustomersUpdated = desired.Count;
        return result;
    }

    private static int ReplaceLocations(Customer customer, IReadOnlyList<WalkthroughSlot> slots)
    {
        var removed = 0;
        foreach (var existing in customer.WarehouseLocations.ToList())
        {
            var keep = slots.Any(slot => WarehouseLocation.Matches(
                existing.Warehouse, existing.Rack, existing.Bin, existing.Shelf,
                slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf));
            if (keep)
                continue;
            customer.WarehouseLocations.Remove(existing);
            removed++;
        }

        var sortOrder = 0;
        foreach (var slot in slots)
        {
            var location = WarehouseLocation.Upsert(
                customer,
                slot.Warehouse,
                slot.Rack,
                slot.Bin,
                slot.Shelf);
            if (location != null)
                location.SortOrder = sortOrder++;
        }

        WarehouseLocation.SyncPrimary(customer);
        return removed;
    }

    private static bool IsPlaceholderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var text = name.Trim();
        return text.Equals("Empty slot", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Empty pallet", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteLocationRow(
        IXLWorksheet sheet,
        int row,
        string name,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        int? qty)
    {
        sheet.Cell(row, 1).Value = name;
        sheet.Cell(row, 2).Value = WarehouseLocation.Code(warehouse);
        sheet.Cell(row, 3).Value = rack;
        sheet.Cell(row, 4).Value = bin;
        sheet.Cell(row, 5).Value = WarehouseLocation.ShelfCode(shelf);
        if (qty.HasValue)
            sheet.Cell(row, 6).Value = qty.Value;
    }

    private static Header ReadHeader(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 6;
        for (var col = 1; col <= lastCol; col++)
        {
            var label = GetString(sheet.Cell(1, col));
            if (label != null)
                map[Normalize(label)] = col;
        }

        return new Header(
            Require(map, NameColumn, "brochure name", "name"),
            Require(map, WarehouseColumn, "warehouse"),
            Require(map, RackColumn, "rack"),
            Require(map, BinColumn, "bin"),
            Require(map, ShelfColumn, "top/bottom", "shelf", "top bottom", "position"),
            Require(map, QtyColumn, "qty", "quantity", "count", "cases"));
    }

    private static WalkthroughHeader ReadWalkthroughHeader(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 6;
        for (var col = 1; col <= lastCol; col++)
        {
            var label = GetString(sheet.Cell(1, col));
            if (label != null)
                map[Normalize(label)] = col;
        }

        return new WalkthroughHeader(
            Require(map, NameColumn, "brochure name", "name"),
            Optional(map, "brochure id", "id", "brochure code"),
            Require(map, WarehouseColumn, "warehouse"),
            Require(map, RackColumn, "rack"),
            Require(map, BinColumn, "bin"),
            Require(map, ShelfColumn, "top/bottom", "shelf", "top bottom", "position"));
    }

    private static int? Optional(IReadOnlyDictionary<string, int> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(Normalize(key), out var col))
                return col;
        }

        return null;
    }

    private static int Require(IReadOnlyDictionary<string, int> map, string primary, params string[] aliases)
    {
        foreach (var key in aliases.Prepend(primary))
        {
            if (map.TryGetValue(Normalize(key), out var col))
                return col;
        }

        throw new InvalidOperationException($"The spreadsheet needs a {primary} column.");
    }

    private static bool TryReadQty(IXLCell cell, out int qty)
    {
        qty = 0;
        if (cell.IsEmpty())
            return false;
        if (cell.DataType == XLDataType.Number)
        {
            qty = (int)Math.Round(cell.GetDouble(), MidpointRounding.AwayFromZero);
            if (qty < 0)
                throw new InvalidOperationException($"Qty cannot be negative in row {cell.Address.RowNumber}.");
            return true;
        }

        var text = GetString(cell);
        if (text == null)
            return false;
        if (!int.TryParse(text, out qty) || qty < 0)
            throw new InvalidOperationException($"Qty must be a whole number in row {cell.Address.RowNumber}.");
        return true;
    }

    private static string? GetString(IXLCell cell)
    {
        if (cell.IsEmpty())
            return null;
        return cell.DataType == XLDataType.Number
            ? WarehouseLocation.NullIfEmpty(cell.GetDouble().ToString("0.###"))
            : WarehouseLocation.NullIfEmpty(cell.GetString());
    }

    private static string Normalize(string value) => value.Trim();

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Holiday World Seasonal Nine"] = "Holiday World",
    };

    private static void WriteInstructions(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("Instructions");
        sheet.Cell(1, 1).Value = "Brochure warehouse inventory";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(3, 1).Value = "One row per rack/bin. Repeat the same brochure name on extra rows for additional locations. Do not rename the brochure names — they match existing customers.";
        sheet.Cell(4, 1).Value = "Warehouse: K (Kentucky) or O (Ohio). Blank warehouse with a rack/bin is saved as K.";
        sheet.Cell(5, 1).Value = "Top/Bottom: Top or Bottom for where the brochures sit in the bin.";
        sheet.Cell(6, 1).Value = "Qty is the current count for that location. Leave Qty blank to update location only. Enter 0 if the rack is empty.";
        sheet.Cell(7, 1).Value = "Import adds or updates each row’s location without removing other rack/bin locations already saved.";
        sheet.Column(1).Width = 110;
    }

    private sealed record Header(int Name, int Warehouse, int Rack, int Bin, int Shelf, int Qty);

    private sealed record WalkthroughHeader(int Name, int? Code, int Warehouse, int Rack, int Bin, int Shelf);

    private sealed record WalkthroughSlot(BrochureWarehouse? Warehouse, string? Rack, string? Bin, BrochureShelf? Shelf);
}

public class BrochureWarehouseSheetImportResult
{
    public int SourceRows { get; set; }
    public int LocationsUpdated { get; set; }
    public int LocationsRemoved { get; set; }
    public int CustomersUpdated { get; set; }
    public int InventoryLogged { get; set; }
    public int Unchanged { get; set; }
    public List<string> Unmatched { get; } = new();
}
