using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdRackHub.Services;

public class ToteLabelPdfGenerator
{
    // Same width as warehouse nametags (4.3"), 2" tall, 2 across.
    private const float LabelWidthInches = 4.3f;
    private const float LabelHeightInches = 2f;
    private const int Columns = 2;
    private const int Rows = 5;
    private const int LabelsPerPage = Columns * Rows;

    private readonly ApplicationDbContext _context;

    public ToteLabelPdfGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<byte[]> BuildAsync(
        IReadOnlyCollection<int>? customerIds = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Customers
            .AsNoTracking()
            .Where(c => c.Type == CustomerType.Customer && c.Status == CustomerStatus.Active)
            .Include(c => c.WarehouseLocations)
            .Include(c => c.Contracts)
            .AsSplitQuery();

        List<Customer> customers;
        if (customerIds == null)
        {
            customers = await query
                .OrderBy(c => c.CustomerName)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var selectedIds = customerIds.Where(id => id > 0).Distinct().ToList();
            var selectedSet = selectedIds.ToHashSet();
            var loaded = selectedSet.Count == 0
                ? new List<Customer>()
                : await query.Where(c => selectedSet.Contains(c.Id)).ToListAsync(cancellationToken);
            var byId = loaded.ToDictionary(c => c.Id);
            customers = selectedIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
        }

        var labels = customers
            .SelectMany(ToLabels)
            .ToList();

        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            if (labels.Count == 0)
            {
                container.Page(EmptyPage);
                return;
            }

            for (var offset = 0; offset < labels.Count; offset += LabelsPerPage)
            {
                var pageLabels = labels.Skip(offset).Take(LabelsPerPage).ToList();
                container.Page(page => ConfigurePage(page, pageLabels));
            }
        }).GeneratePdf();
    }

    private static IEnumerable<ToteLabel> ToLabels(Customer customer)
    {
        var locationLines = customer.WarehouseLocations
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .Select(l => WarehouseLocation.FormatLabelLine(l.Warehouse, l.Rack, l.Bin, l.Shelf))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Cast<string>()
            .ToList();
        if (locationLines.Count == 0)
        {
            var line = WarehouseLocation.FormatLabelLine(
                customer.Warehouse,
                customer.WarehouseRack,
                customer.WarehouseBin,
                customer.WarehouseShelf);
            if (!string.IsNullOrWhiteSpace(line))
                locationLines.Add(line);
        }

        var driversNotes = DriversNotesLine(customer);
        if (locationLines.Count == 0)
        {
            yield return new ToteLabel
            {
                Name = customer.CustomerName.Trim(),
                Location = null,
                DriversNotes = driversNotes
            };
            yield break;
        }

        foreach (var location in locationLines)
        {
            yield return new ToteLabel
            {
                Name = customer.CustomerName.Trim(),
                Location = location,
                DriversNotes = driversNotes
            };
        }
    }

    private static void EmptyPage(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(48);
        page.Content().Text("No selected customers to print tote labels for.");
    }

    private static void ConfigurePage(PageDescriptor page, IReadOnlyList<ToteLabel> labels)
    {
        page.Size(PageSizes.Letter);
        page.Margin(12, Unit.Point);
        page.DefaultTextStyle(text => text.FontSize(12).FontColor(Colors.Black));

        page.Content()
            .AlignCenter()
            .AlignMiddle()
            .ScaleToFit()
            .Width(LabelWidthInches * Columns, Unit.Inch)
            .Height(LabelHeightInches * Rows, Unit.Inch)
            .Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(LabelWidthInches, Unit.Inch);
                    columns.ConstantColumn(LabelWidthInches, Unit.Inch);
                });

                for (var i = 0; i < LabelsPerPage; i++)
                {
                    table.Cell()
                        .Width(LabelWidthInches, Unit.Inch)
                        .Height(LabelHeightInches, Unit.Inch)
                        .Element(cell => DrawSlot(cell, labels.ElementAtOrDefault(i)));
                }
            });
    }

    private static void DrawSlot(IContainer cell, ToteLabel? label)
    {
        var box = cell
            .Border(1, Unit.Point)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingHorizontal(12)
            .PaddingVertical(8);

        if (label == null)
            return;

        var notes = WarehouseLocation.NullIfEmpty(label.DriversNotes);
        box.Layers(layers =>
        {
            layers.PrimaryLayer()
                .PaddingBottom(notes == null ? 0 : 18)
                .AlignCenter()
                .AlignMiddle()
                .Column(info =>
                {
                    info.Item().AlignCenter().Text(label.Name).FontSize(16).Bold();
                    if (!string.IsNullOrWhiteSpace(label.Location))
                        info.Item().PaddingTop(8).AlignCenter().Text(label.Location).FontSize(22).ExtraBold();
                });

            if (notes != null)
            {
                layers.Layer().AlignBottom().AlignCenter().Text(notes)
                    .FontSize(9);
            }
        });
    }

    private static string? DriversNotesLine(Customer customer)
    {
        var notes = customer.Contracts
            .Where(c => BillingDueCalculator.IsActiveContract(c))
            .Select(c => WarehouseLocation.NullIfEmpty(c.DriversNotes))
            .Where(note => note != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return notes.Count == 0 ? null : string.Join(" · ", notes);
    }

    private sealed class ToteLabel
    {
        public string Name { get; init; } = string.Empty;
        public string? Location { get; init; }
        public string? DriversNotes { get; init; }
    }
}
