using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdRackHub.Services;

public class BrochureLabelPdfGenerator
{
    // Word nametag 6-up: 4.3" x 3.4", 2 across, 3 down, attached, centered.
    // 4.3 x 2 is 8.6" (0.1" wider than letter), so the block is scaled to sit
    // fully on the page with a 1pt black border around each label.
    private const float LabelWidthInches = 4.3f;
    private const float LabelHeightInches = 3.4f;
    private const int Columns = 2;
    private const int Rows = 3;
    private const int LabelsPerPage = Columns * Rows;

    private readonly ApplicationDbContext _context;
    private readonly BrochureScanService _brochureScans;
    private readonly BrochureLabelIdService _labelIds;
    private readonly IWebHostEnvironment _environment;

    public BrochureLabelPdfGenerator(
        ApplicationDbContext context,
        BrochureScanService brochureScans,
        BrochureLabelIdService labelIds,
        IWebHostEnvironment environment)
    {
        _context = context;
        _brochureScans = brochureScans;
        _labelIds = labelIds;
        _environment = environment;
    }

    public async Task<byte[]> BuildAsync(CancellationToken cancellationToken = default)
    {
        await _labelIds.AssignMissingAsync(cancellationToken);

        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.Type == CustomerType.Customer && c.Status == CustomerStatus.Active)
            .Include(c => c.BrochureScans)
            .Include(c => c.WarehouseLocations)
            .Include(c => c.CustomerRoutes)
                .ThenInclude(cr => cr.Route)
            .AsSplitQuery()
            .OrderBy(c => c.BrochureCode)
            .ThenBy(c => c.CustomerName)
            .ToListAsync(cancellationToken);

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

    private IEnumerable<BrochureLabel> ToLabels(Customer customer)
    {
        var scans = customer.BrochureScans
            .Where(s => s.IsImage)
            .OrderBy(s => s.UploadedAt)
            .ThenBy(s => s.Id)
            .ToList();

        var routes = customer.CustomerRoutes
            .Where(cr => cr.Status == CustomerRouteStatus.Active && cr.Route != null)
            .Select(cr => RouteNaming.StripPrefix(cr.Route.RouteName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

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

        // One box per brochure version (scan). If there are more warehouse slots
        // than scans, keep extra copies of the last scan so each slot still gets a tag.
        var copies = Math.Max(1, Math.Max(scans.Count, locationLines.Count));
        for (var i = 0; i < copies; i++)
        {
            var scan = scans.Count == 0 ? null : scans[Math.Min(i, scans.Count - 1)];
            yield return new BrochureLabel
            {
                Name = customer.CustomerName.Trim(),
                Code = customer.BrochureCode ?? string.Empty,
                LocationLines = locationLines,
                Routes = routes,
                ImagePath = scan == null ? null : _brochureScans.ResolveFilePath(scan, _environment)
            };
        }
    }

    private static void EmptyPage(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(48);
        page.Content().Text("No active customers to print labels for.");
    }

    private static void ConfigurePage(PageDescriptor page, IReadOnlyList<BrochureLabel> labels)
    {
        page.Size(PageSizes.Letter);
        page.Margin(12, Unit.Point);
        page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

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

    private static void DrawSlot(IContainer cell, BrochureLabel? label)
    {
        var box = cell
            .Border(1, Unit.Point)
            .BorderColor(Colors.Grey.Lighten1)
            .Padding(8);

        if (label == null)
            return;

        box.Row(content =>
        {
            content.ConstantItem(1.5f, Unit.Inch).PaddingRight(8).Element(thumb =>
            {
                if (!string.IsNullOrWhiteSpace(label.ImagePath) && File.Exists(label.ImagePath))
                {
                    thumb.AlignCenter().AlignMiddle().Image(label.ImagePath).FitArea();
                    return;
                }

                thumb.Border(0.5f)
                    .BorderColor(Colors.Grey.Lighten1)
                    .Background(Colors.Grey.Lighten4)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text("No scan")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Medium);
            });

            content.RelativeItem().AlignMiddle().Column(info =>
            {
                info.Item().Text(label.Name).FontSize(9).Bold();
                if (!string.IsNullOrWhiteSpace(label.Code))
                    info.Item().PaddingTop(2).Text(label.Code).FontSize(18).ExtraBold();

                if (label.LocationLines.Count > 0)
                {
                    info.Item().PaddingTop(6).Text("Locations").FontSize(8).Bold();
                    foreach (var location in label.LocationLines)
                        info.Item().Text(location).FontSize(14).ExtraBold();
                }

                info.Item().PaddingTop(6).Text(text =>
                {
                    text.Span("Routes  ").FontSize(8).Bold();
                    text.Span(label.Routes.Count == 0 ? "None" : string.Join(", ", label.Routes))
                        .FontSize(8);
                });
            });
        });
    }

    private sealed class BrochureLabel
    {
        public string Name { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public IReadOnlyList<string> LocationLines { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Routes { get; init; } = Array.Empty<string>();
        public string? ImagePath { get; init; }
    }
}
