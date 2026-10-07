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
    public DateOnly? InventoryDate { get; init; }
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
                    Quantity = LatestQuantity(customer, slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf, slot.IsFirst),
                    InventoryDate = LatestInventoryDate(customer, slot.Warehouse, slot.Rack, slot.Bin, slot.Shelf, slot.IsFirst)
                })))
            .GroupBy(item => item.Warehouse)
            .OrderBy(group => group.Key.HasValue ? 0 : 1)
            .ThenBy(group => group.Key)
            .Select(group => new BrochureInventoryWarehouseGroup
            {
                Warehouse = group.Key,
                Title = WarehouseTitle(group.Key),
                Rows = group
                    .Select(item => item.Row)
                    .OrderBy(row => row.BrochureName)
                    .ThenBy(row => row.Location)
                    .ToList()
            })
            .ToList();

        return new BrochureInventoryReport
        {
            AsOf = DateOnly.FromDateTime(DateTime.Today),
            Warehouses = warehouses
        };
    }

    public byte[] GeneratePdf(BrochureInventoryReport report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var groups = report.Warehouses.Count == 0
            ? new[]
            {
                new BrochureInventoryWarehouseGroup
                {
                    Title = "Unassigned",
                    Rows = Array.Empty<BrochureInventoryRow>()
                }
            }
            : report.Warehouses;

        return Document.Create(container =>
        {
            foreach (var group in groups)
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

                    page.Header().Element(header => DrawHeader(header, group.Title));
                    page.Footer().AlignCenter().DefaultTextStyle(text => text.FontSize(8)).Text(text =>
                    {
                        text.Span("Fill in cases, brochures per case, and total by hand.  Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                    page.Content().PaddingTop(8).Element(content => DrawSheet(content, group));
                });
            }
        }).GeneratePdf();
    }

    private static void DrawHeader(IContainer container, string warehouseTitle)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("WAREHOUSE INVENTORY COUNT SHEET").FontSize(13).Bold();
            col.Item().PaddingTop(2).AlignCenter().Text(warehouseTitle.ToUpperInvariant()).FontSize(11).Bold();
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

    private static void DrawSheet(IContainer container, BrochureInventoryWarehouseGroup group)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3.6f);
                columns.ConstantColumn(52);
                columns.RelativeColumn(2.4f);
                columns.ConstantColumn(70);
                columns.ConstantColumn(70);
                columns.ConstantColumn(78);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "Brochure");
                HeaderCell(header.Cell(), "ID");
                HeaderCell(header.Cell(), "Location");
                HeaderCell(header.Cell(), "Cases", true);
                HeaderCell(header.Cell(), "Per case", true);
                HeaderCell(header.Cell(), "Total", true);
            });

            foreach (var row in group.Rows)
                DrawDataRow(table, row.BrochureName, row.BrochureCode, row.Location);

            for (var i = 0; i < ExtraBlankRows; i++)
                DrawDataRow(table, "", null, null);

            table.Cell().ColumnSpan(3).PaddingTop(10).Element(cell =>
            {
                cell.AlignRight().PaddingRight(8).PaddingTop(6).Text("WAREHOUSE TOTAL").Bold();
            });
            table.Cell().PaddingTop(10).Element(WriteInBox);
            table.Cell().PaddingTop(10).Element(WriteInBox);
            table.Cell().PaddingTop(10).Element(WriteInBox);
        });
    }

    private static void DrawDataRow(
        TableDescriptor table,
        string name,
        string? code,
        string? location)
    {
        table.Cell().Element(cell => TextCell(cell, name));
        table.Cell().Element(cell => TextCell(cell, code ?? ""));
        table.Cell().Element(cell => TextCell(cell, location ?? ""));
        table.Cell().Element(WriteInBox);
        table.Cell().Element(WriteInBox);
        table.Cell().Element(WriteInBox);
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

    private static void TextCell(IContainer container, string text) =>
        container.BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten1)
            .MinHeight(22).PaddingHorizontal(4).AlignMiddle()
            .Text(text);

    private static void WriteInBox(IContainer container) =>
        container.PaddingHorizontal(4).PaddingVertical(3)
            .Border(0.7f).BorderColor(LineColor).Height(18).Background(Colors.White);

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

    private static CustomerBrochureInventory? LatestInventory(
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
            .FirstOrDefault();
        if (match != null)
            return match;

        if (!isFirstSlot)
            return null;

        return customer.BrochureInventories
            .OrderByDescending(i => i.InventoryDate)
            .ThenByDescending(i => i.Id)
            .FirstOrDefault();
    }

    private static int? LatestQuantity(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot) =>
        LatestInventory(customer, warehouse, rack, bin, shelf, isFirstSlot)?.Quantity;

    private static DateOnly? LatestInventoryDate(
        Customer customer,
        BrochureWarehouse? warehouse,
        string? rack,
        string? bin,
        BrochureShelf? shelf,
        bool isFirstSlot) =>
        LatestInventory(customer, warehouse, rack, bin, shelf, isFirstSlot)?.InventoryDate;

    public static string WarehouseTitle(BrochureWarehouse? warehouse) => warehouse switch
    {
        BrochureWarehouse.K => "Kentucky (K)",
        BrochureWarehouse.O => "Ohio (O)",
        _ => "Unassigned"
    };
}
