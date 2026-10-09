using System.Text.RegularExpressions;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdRackHub.Services;

public sealed class BrochureInventoryReport
{
    public DateOnly AsOf { get; init; }
    public IReadOnlyList<BrochureInventoryWarehouseGroup> Warehouses { get; init; } =
        Array.Empty<BrochureInventoryWarehouseGroup>();
}

public sealed class BrochureInventoryWarehouseGroup
{
    public BrochureWarehouse? Warehouse { get; init; }
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<BrochureInventoryRow> Rows { get; init; } = Array.Empty<BrochureInventoryRow>();
}

public sealed class BrochureInventoryRow
{
    public int CustomerId { get; init; }
    public string BrochureName { get; init; } = string.Empty;
    public string? BrochureCode { get; init; }
    public BrochureWarehouse? Warehouse { get; init; }
    public string? Rack { get; init; }
    public string? Bin { get; init; }
    public BrochureShelf? Shelf { get; init; }
    public string? Location { get; init; }
    public int? Quantity { get; init; }
    public int? ReceivedQuantity { get; init; }
    public DateOnly? InventoryDate { get; init; }
    public int? PerCase { get; init; }
    public string? ContractLabel { get; init; }

    public string LastLabel => FormatLast(Quantity, ReceivedQuantity);

    public static string FormatLast(int? lastCount, int? lastReceived)
    {
        if (!lastCount.HasValue && !lastReceived.HasValue)
            return string.Empty;
        var count = lastCount?.ToString("N0") ?? "—";
        var received = lastReceived?.ToString("N0") ?? "—";
        return $"{count}/{received}";
    }
}

public class BrochureInventoryReportService
{
    private const int ExtraBlankRows = 4;
    private static readonly Color LineColor = Colors.Grey.Darken2;

    private readonly ApplicationDbContext _context;

    public BrochureInventoryReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BrochureInventoryReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.Type == CustomerType.Customer)
            .Include(c => c.WarehouseLocations)
            .Include(c => c.BrochureInventories)
            .OrderBy(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        var asOf = DateOnly.FromDateTime(DateTime.Today);
        var contractLabels = await BuildContractLabelsAsync(
            customers.Select(c => c.Id).ToList(),
            asOf,
            cancellationToken);

        var warehouses = customers
            .SelectMany(customer => SlotsFor(customer).Select(slot => (
                slot.Warehouse,
                Row: new BrochureInventoryRow
                {
                    CustomerId = customer.Id,
                    BrochureName = customer.CustomerName,
                    BrochureCode = WarehouseLocation.NullIfEmpty(customer.BrochureCode),
                    Warehouse = slot.Warehouse,
                    Rack = slot.Rack,
                    Bin = slot.Bin,
                    Shelf = slot.Shelf,
                    Location = slot.Location,
                    Quantity = LatestCountQuantity(customer, slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf, slot.IsFirst),
                    ReceivedQuantity = LatestReceivedQuantity(customer, slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf, slot.IsFirst),
                    InventoryDate = LatestInventoryDate(customer, slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf, slot.IsFirst),
                    PerCase = LatestPerCase(customer, slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf, slot.IsFirst),
                    ContractLabel = contractLabels.GetValueOrDefault(customer.Id)
                })))
            .GroupBy(item => item.Warehouse)
            .OrderBy(group => group.Key.HasValue ? 0 : 1)
            .ThenBy(group => group.Key)
            .Select(group => new BrochureInventoryWarehouseGroup
            {
                Warehouse = group.Key,
                Title = WarehouseTitle(group.Key),
                Rows = group.Select(item => item.Row).ToList()
            })
            .ToList();

        return new BrochureInventoryReport
        {
            AsOf = asOf,
            Warehouses = warehouses
        };
    }

    public static bool IsUnassignedCode(string? warehouse) =>
        string.Equals(warehouse, "U", StringComparison.OrdinalIgnoreCase)
        || string.Equals(warehouse, "unassigned", StringComparison.OrdinalIgnoreCase);

    public static string WarehouseTitleFromCode(string? warehouse)
    {
        if (string.Equals(warehouse, "O", StringComparison.OrdinalIgnoreCase))
            return WarehouseTitle(BrochureWarehouse.O);
        if (IsUnassignedCode(warehouse))
            return WarehouseTitle(null);
        return WarehouseTitle(BrochureWarehouse.K);
    }

    public static IReadOnlyList<BrochureInventoryRow> RowsForWarehouse(
        BrochureInventoryReport report,
        string? warehouse)
    {
        BrochureInventoryWarehouseGroup? group;
        if (string.Equals(warehouse, "O", StringComparison.OrdinalIgnoreCase))
            group = report.Warehouses.FirstOrDefault(g => g.Warehouse == BrochureWarehouse.O);
        else if (IsUnassignedCode(warehouse))
            group = report.Warehouses.FirstOrDefault(g => g.Warehouse == null);
        else
            group = report.Warehouses.FirstOrDefault(g => g.Warehouse == BrochureWarehouse.K);

        return (group?.Rows ?? Array.Empty<BrochureInventoryRow>())
            .Order(Comparer<BrochureInventoryRow>.Create(CompareInventoryRows))
            .ToList();
    }

    public byte[] GeneratePdf(BrochureInventoryReport report, string? warehouse = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var printAll = string.IsNullOrWhiteSpace(warehouse)
            || string.Equals(warehouse, "all", StringComparison.OrdinalIgnoreCase);
        var codes = printAll
            ? new[] { "K", "O", "U" }
            : new[]
            {
                string.Equals(warehouse, "O", StringComparison.OrdinalIgnoreCase) ? "O"
                    : IsUnassignedCode(warehouse) ? "U"
                    : "K"
            };

        var sheets = codes
            .Select(code => (Title: WarehouseTitleFromCode(code), Rows: RowsForWarehouse(report, code)))
            .ToList();
        if (sheets.Count == 0)
            sheets.Add((WarehouseTitle(BrochureWarehouse.K), Array.Empty<BrochureInventoryRow>()));

        return Document.Create(container =>
        {
            foreach (var sheet in sheets)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter.Landscape());
                    page.MarginLeft(32);
                    page.MarginRight(32);
                    page.MarginTop(28);
                    page.MarginBottom(24);
                    page.DefaultTextStyle(text => text
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken4));

                    page.Header().Element(header => DrawHeader(header, sheet.Title, report.AsOf));
                    page.Footer().AlignCenter().DefaultTextStyle(text => text.FontSize(8)).Text(text =>
                    {
                        text.Span("Fill cases, per case, and total. Per case is printed when known.  Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                    page.Content().PaddingTop(8).Element(content => DrawSheet(content, sheet.Rows));
                });
            }
        }).GeneratePdf();
    }

    private static void DrawHeader(IContainer container, string warehouseTitle, DateOnly asOf)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("WAREHOUSE INVENTORY COUNT SHEET").FontSize(13).Bold();
            col.Item().PaddingTop(2).AlignCenter().Text(warehouseTitle.ToUpperInvariant()).FontSize(11).Bold();
            col.Item().PaddingTop(2).AlignCenter().Text($"Last counts as of {asOf:MMM d, yyyy}").FontSize(8);
            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Row(field =>
                {
                    field.AutoItem().Text("Date").Bold();
                    field.RelativeItem().PaddingLeft(6).PaddingTop(8).LineHorizontal(0.6f).LineColor(LineColor);
                });
                row.ConstantItem(24);
                row.RelativeItem().Row(field =>
                {
                    field.AutoItem().Text("Counted by").Bold();
                    field.RelativeItem().PaddingLeft(6).PaddingTop(8).LineHorizontal(0.6f).LineColor(LineColor);
                });
            });
        });
    }

    private static void DrawSheet(IContainer container, IReadOnlyList<BrochureInventoryRow> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3.4f);
                columns.RelativeColumn(2.0f);
                columns.ConstantColumn(110);
                columns.ConstantColumn(82);
                columns.ConstantColumn(58);
                columns.ConstantColumn(58);
                columns.ConstantColumn(64);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "Customer");
                HeaderCell(header.Cell(), "Location");
                HeaderCell(header.Cell(), "Contract");
                HeaderCell(header.Cell(), "Last", true);
                HeaderCell(header.Cell(), "Cases", true);
                HeaderCell(header.Cell(), "Per case", true);
                HeaderCell(header.Cell(), "Total", true);
            });

            string? previousRack = null;
            var started = false;
            foreach (var row in rows)
            {
                var rack = WarehouseLocation.NullIfEmpty(row.Rack) ?? "";
                var rackBreak = started
                    && !string.Equals(rack, previousRack ?? "", StringComparison.OrdinalIgnoreCase);
                started = true;
                previousRack = rack;
                DrawDataRow(
                    table,
                    CustomerLabel(row),
                    row.Location,
                    row.ContractLabel,
                    row.LastLabel,
                    row.PerCase,
                    rackBreak);
            }

            for (var i = 0; i < ExtraBlankRows; i++)
                DrawDataRow(table, "", null, null, "", null, false);

            table.Cell().ColumnSpan(4).PaddingTop(10).Element(cell =>
            {
                cell.AlignRight().PaddingRight(8).PaddingTop(6).Text("WAREHOUSE TOTAL").Bold();
            });
            table.Cell().PaddingTop(10).Element(c => WriteInBox(c));
            table.Cell().PaddingTop(10).Element(c => WriteInBox(c));
            table.Cell().PaddingTop(10).Element(c => WriteInBox(c));
        });
    }

    private static void DrawDataRow(
        TableDescriptor table,
        string name,
        string? location,
        string? contractLabel,
        string lastLabel,
        int? perCase,
        bool rackBreak)
    {
        table.Cell().Element(cell => TextCell(cell, name, rackBreak));
        table.Cell().Element(cell => TextCell(cell, location ?? "", rackBreak));
        table.Cell().Element(cell => TextCell(cell, contractLabel ?? "", rackBreak));
        table.Cell().Element(cell => TextCell(cell, lastLabel, rackBreak, alignRight: true));
        table.Cell().Element(cell => WriteInBox(cell, null, rackBreak));
        table.Cell().Element(cell => WriteInBox(cell, perCase?.ToString("N0"), rackBreak));
        table.Cell().Element(cell => WriteInBox(cell, null, rackBreak));
    }

    private static void HeaderCell(IContainer container, string text, bool alignRight = false)
    {
        var cell = container.BorderBottom(1).BorderColor(LineColor).PaddingBottom(4).PaddingHorizontal(4)
            .AlignBottom();
        if (alignRight)
            cell.AlignRight().Text(text).Bold();
        else
            cell.Text(text).Bold();
    }

    private static IContainer RowEdge(IContainer container, bool rackBreak) =>
        container
            .BorderTop(rackBreak ? 1.8f : 0.4f)
            .BorderColor(rackBreak ? Colors.Grey.Darken3 : Colors.Grey.Lighten1)
            .MinHeight(22)
            .PaddingHorizontal(4)
            .AlignMiddle();

    private static void TextCell(IContainer container, string text, bool rackBreak = false, bool alignRight = false)
    {
        var cell = RowEdge(container, rackBreak);
        if (alignRight)
            cell.AlignRight().Text(text);
        else
            cell.Text(text);
    }

    private static void WriteInBox(IContainer container, string? value = null, bool rackBreak = false)
    {
        container
            .BorderTop(rackBreak ? 1.8f : 0.4f)
            .BorderColor(rackBreak ? Colors.Grey.Darken3 : Colors.Grey.Lighten1)
            .MinHeight(22)
            .PaddingHorizontal(4)
            .PaddingVertical(3)
            .Element(inner => inner
                .Border(0.7f)
                .BorderColor(LineColor)
                .Background(Colors.White)
                .AlignCenter()
                .AlignMiddle()
                .Text(value ?? "")
                .FontColor(Colors.Grey.Medium));
    }

    private static string CustomerLabel(BrochureInventoryRow row)
    {
        if (string.IsNullOrWhiteSpace(row.BrochureCode))
            return row.BrochureName;
        return $"{row.BrochureName} ({row.BrochureCode})";
    }

    private static int CompareInventoryRows(BrochureInventoryRow left, BrochureInventoryRow right)
    {
        var rack = CompareLocationPart(left.Rack, right.Rack);
        if (rack != 0)
            return rack;
        var bin = CompareLocationPart(left.Bin, right.Bin);
        if (bin != 0)
            return bin;
        return string.Compare(left.BrochureName, right.BrochureName, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareLocationPart(string? left, string? right)
    {
        left = WarehouseLocation.NullIfEmpty(left);
        right = WarehouseLocation.NullIfEmpty(right);
        if (left == null && right == null)
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        var i = 0;
        var j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                long a = 0;
                long b = 0;
                while (i < left.Length && char.IsDigit(left[i]))
                    a = a * 10 + (left[i++] - '0');
                while (j < right.Length && char.IsDigit(right[j]))
                    b = b * 10 + (right[j++] - '0');
                var numeric = a.CompareTo(b);
                if (numeric != 0)
                    return numeric;
                continue;
            }

            var text = char.ToUpperInvariant(left[i++]).CompareTo(char.ToUpperInvariant(right[j++]));
            if (text != 0)
                return text;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }

    private static IEnumerable<(BrochureWarehouse? Warehouse, string? Rack, string? Bin, BrochureShelf? Shelf, string? Location, bool IsFirst)> SlotsFor(Customer customer)
    {
        var locations = customer.WarehouseLocations
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .ToList();
        if (locations.Count > 0)
        {
            var first = true;
            foreach (var location in locations)
            {
                yield return (
                    location.Warehouse,
                    location.Rack,
                    location.Bin,
                    location.Shelf,
                    WarehouseLocation.Format(null, location.Rack, location.Bin, location.Shelf),
                    first);
                first = false;
            }

            yield break;
        }

        if (customer.Warehouse == null
            && string.IsNullOrWhiteSpace(customer.WarehouseRack)
            && string.IsNullOrWhiteSpace(customer.WarehouseBin)
            && customer.WarehouseShelf == null)
        {
            yield return (null, null, null, null, null, true);
            yield break;
        }

        yield return (
            customer.Warehouse,
            customer.WarehouseRack,
            customer.WarehouseBin,
            customer.WarehouseShelf,
            WarehouseLocation.Format(
                null,
                customer.WarehouseRack,
                customer.WarehouseBin,
                customer.WarehouseShelf),
            true);
    }

    private static List<CustomerBrochureInventory> OrderedInventories(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot)
    {
        var match = customer.BrochureInventories
            .Where(i => WarehouseLocation.Matches(
                i.Warehouse, i.Rack, i.Bin, i.Shelf,
                warehouse, rack, bin, shelf))
            .OrderByDescending(i => i.InventoryDate)
            .ThenByDescending(i => i.Id)
            .ToList();
        if (match.Count > 0 || !isFirstSlot)
            return match;

        return customer.BrochureInventories
            .OrderByDescending(i => i.InventoryDate)
            .ThenByDescending(i => i.Id)
            .ToList();
    }

    private static CustomerBrochureInventory? LatestCountInventory(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot)
    {
        var items = OrderedInventories(customer, warehouse, rack, bin, shelf, isFirstSlot);
        return items.FirstOrDefault(i => !IsReceivedInventory(i)) ?? items.FirstOrDefault();
    }

    private static int? LatestCountQuantity(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot) =>
        LatestCountInventory(customer, warehouse, rack, bin, shelf, isFirstSlot)?.Quantity;

    private static int? LatestReceivedQuantity(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot)
    {
        var items = OrderedInventories(customer, warehouse, rack, bin, shelf, isFirstSlot);
        var namedReceive = items.FirstOrDefault(IsReceivedInventory);
        if (namedReceive != null)
            return namedReceive.Quantity;

        var lastCount = LatestCountInventory(customer, warehouse, rack, bin, shelf, isFirstSlot);
        return items.FirstOrDefault(i => i.Id != lastCount?.Id)?.Quantity;
    }

    private static bool IsReceivedInventory(CustomerBrochureInventory inventory)
    {
        var notes = inventory.Notes;
        if (string.IsNullOrWhiteSpace(notes))
            return false;
        return notes.Contains("receiv", StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? LatestInventoryDate(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot) =>
        LatestCountInventory(customer, warehouse, rack, bin, shelf, isFirstSlot)?.InventoryDate;

    private static readonly Regex PerCasePattern = new(
        @"cases\s*[×x]\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? PerCaseFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;
        var match = PerCasePattern.Match(notes);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var perCase) || perCase <= 0)
            return null;
        return perCase;
    }

    private static int? LatestPerCase(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot)
    {
        foreach (var inventory in customer.BrochureInventories
            .Where(i => WarehouseLocation.Matches(
                i.Warehouse, i.Rack, i.Bin, i.Shelf,
                warehouse, rack, bin, shelf))
            .OrderByDescending(i => i.InventoryDate)
            .ThenByDescending(i => i.Id))
        {
            var perCase = PerCaseFromNotes(inventory.Notes);
            if (perCase.HasValue)
                return perCase;
        }

        if (!isFirstSlot)
            return null;

        foreach (var inventory in customer.BrochureInventories
            .OrderByDescending(i => i.InventoryDate)
            .ThenByDescending(i => i.Id))
        {
            var perCase = PerCaseFromNotes(inventory.Notes);
            if (perCase.HasValue)
                return perCase;
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<int, string>> BuildContractLabelsAsync(
        IReadOnlyCollection<int> customerIds,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<int, string>();
        if (customerIds.Count == 0)
            return labels;

        var contracts = await _context.CustomerContracts
            .AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId))
            .Select(c => new
            {
                c.CustomerId,
                c.ContractEndDate,
                c.ServiceMonthMask,
                RouteIds = c.ContractRoutes.Select(cr => cr.RouteId).ToList()
            })
            .ToListAsync(cancellationToken);

        foreach (var customerId in customerIds)
        {
            var live = contracts
                .Where(c => c.CustomerId == customerId && IsLiveContract(c.ContractEndDate, c.RouteIds.Count, asOf))
                .ToList();
            if (live.Count == 0)
                continue;

            var distributing = live.Any(c =>
                BillingDueCalculator.IsMonthInService(c.ServiceMonthMask, asOf.Month));
            var endDates = live.Select(c => c.ContractEndDate).ToList();
            var endLabel = endDates.Any(d => !d.HasValue)
                ? "—"
                : endDates.Max()!.Value.ToString("MM/yy");
            var routeCount = live.SelectMany(c => c.RouteIds).Distinct().Count();
            var routeLabel = routeCount == 0 ? "—" : $"Rt {routeCount}";
            labels[customerId] = $"{(distributing ? "A" : "—")} | {endLabel} | {routeLabel}";
        }

        return labels;
    }

    private static bool IsLiveContract(DateOnly? end, int routeCount, DateOnly asOf)
    {
        if (routeCount == 0)
            return false;
        if (end.HasValue && end.Value < asOf)
            return false;
        return true;
    }

    public static string WarehouseTitle(BrochureWarehouse? warehouse) => warehouse switch
    {
        BrochureWarehouse.K => "Kentucky (K)",
        BrochureWarehouse.O => "Ohio (O)",
        _ => "Unassigned"
    };
}
