using System.Globalization;
using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdRackHub.Services;

public sealed class RateSheetRow
{
    public string Name { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public int Racks { get; init; }
    public decimal AnnualRate { get; init; }
    public decimal ShortTermRate { get; init; }
}

public sealed class RateSheetModel
{
    public DateOnly Effective { get; init; }
    public string ServiceLabel { get; init; } = "HOTEL BROCHURE DISTRIBUTION";
    public string NameHeader { get; init; } = "Route";
    public bool ShowCount { get; init; } = true;
    public string CountHeader { get; init; } = "Racks**";
    public float CountColumnWidth { get; init; } = 58;
    public string PrimaryRateHeader { get; init; } = "Annual / mo*";
    public bool ShowShortTermRate { get; init; } = true;
    public string MapTitle { get; init; } = "ROUTE MAP";
    public string MapPath { get; init; } = string.Empty;
    public bool MapOnFirstPage { get; init; }
    public IReadOnlyList<RateSheetRow> Rows { get; init; } = Array.Empty<RateSheetRow>();
    public IReadOnlyList<string> GeographyLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public class RateSheetPdfGenerator
{
    public const decimal IncreasePercent = 3m;

    private static readonly Color Accent = Color.FromHex("#F5A623");
    private static readonly Color Muted = Color.FromHex("#6B7280");
    private static readonly Color Line = Color.FromHex("#E5E7EB");
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// The printed hotel rate is the route's 4-6 month list price, plus 3%.
    /// AnnualRate on each spec is the old May 2026 annual figure and is not printed.
    /// </summary>
    private static readonly RouteRateSpec[] Schedule =
    {
        new("Cincinnati-NKY", "Greater Cincinnati-Northern KY", "Covering the Cincinnati area including Northern Kentucky to Hamilton and Mason", 135m, 175m, 59),
        new("Louisville", "Greater Louisville", "Including I-65, I-64 and I-71 covering Louisville, Southern Indiana, Bullitt, Oldham and Shelby Counties", 135m, 175m, 54),
        new("Lex-Frankfort", "Lexington/Frankfort", "Covering I-64 from Frankfort, Lexington, Georgetown and Winchester", 120m, 160m, 56),
        new("Northern Interstate", "Northern Interstate", "North of Cincinnati to Toledo", 175m, 230m, 74),
        new("Northeast OH", "Northeast Ohio", "I-71 from Cleveland to Delaware and Canton", 110m, 150m, 49),
        new("Central OH", "Central Ohio", "Covering the greater Columbus area including Zanesville and Delaware", 150m, 210m, 58),
        new("I-75", "Southern Interstate I-75", "Covering Kentucky and Tennessee from Richmond to Chattanooga", 120m, 170m, 46),
        new("I-65 & 24", "Southern Interstate I-65", "Covering Kentucky and Tennessee from Shepherdsville to Murfreesboro", 100m, 140m, 39),
        new("Mid-TN", "Mid-Tennessee", "I-40 between Knoxville and Nashville", 55m, 100m, 32)
    };

    private static readonly RouteRateSpec[] RestAreaSchedule =
    {
        new("I-64 East of Lexington", "I-64 East of Lexington", "Mountain Parkway and I-64 from Slade to Grayson", 35m, 40m, 5),
        new("I-64 West & I-71", "I-64 West & I-71", "I-64 near Midway and I-71 near Crestwood", 35m, 40m, 4),
        new("I-65", "I-65", "I-65 near Munfordville and Horse Cave", 35m, 40m, 2),
        new("I-75", "I-75", "I-75 from Georgetown to Florence", 35m, 40m, 4)
    };

    private static readonly string[] HotelNotes =
    {
        "* 5% discount if paid annually. Suggested prices only for new customers or a current customer adding a route. Customers may have been in the service for many years and may be paying lower rates. Prices may differ because of multiple routes contracted by the customer, the number of available pockets in the route, and seasonal rates.",
        "** Number of racks in a route is subject to change."
    };

    private static readonly string[] RestAreaNotes =
    {
        $"* Monthly rate per location. {Money(RestAreaMonthlyRate(1))} for 1–3 locations, {Money(RestAreaMonthlyRate(4))} for 4–5 locations, and {Money(RestAreaMonthlyRate(6))} for 6 or more. Suggested price for new customers or a current customer adding the route. Customers may have been in the service for many years and may be paying lower rates.",
        $"Number of locations is subject to change. A double pocket increases the rate to {Money(IncreasedQuote(55m))}–{Money(IncreasedQuote(65m))} per pocket, depending on how many locations.",
        "Kentucky rest-area distribution is contingent upon Ad-Rack maintaining authorization from the Commonwealth of Kentucky. If that authorization ends, Ad-Rack may provide substitute distribution, a credit, or a prorated refund for services not provided."
    };

    private readonly ApplicationDbContext _context;
    private readonly WaveOptions _options;
    private readonly string _logoPath;
    private readonly string _mapPath;
    private readonly string _restAreaMapPath;

    public RateSheetPdfGenerator(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        IOptions<WaveOptions> options)
    {
        _context = context;
        _options = options.Value;
        _logoPath = Path.Combine(environment.WebRootPath, "images", "ad-rack-logo.png");
        _mapPath = Path.Combine(environment.WebRootPath, "images", "route-map.png");
        _restAreaMapPath = Path.Combine(environment.WebRootPath, "images", "rest-area-map.png");
    }

    public async Task<byte[]> BuildAsync(CancellationToken cancellationToken = default)
    {
        var model = await LoadAsync(cancellationToken);
        return Generate(model);
    }

    public async Task<byte[]> BuildRestAreaAsync(CancellationToken cancellationToken = default)
    {
        var model = await LoadRestAreaAsync(cancellationToken);
        return Generate(model);
    }

    public async Task<RateSheetModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        var routes = await _context.Routes
            .AsNoTracking()
            .Include(route => route.Stops)
            .Where(route => route.Status == RouteStatus.Active)
            .ToListAsync(cancellationToken);

        var rows = new List<RateSheetRow>();
        var geography = new List<string>();
        foreach (var spec in Schedule)
        {
            var route = routes.FirstOrDefault(candidate =>
                candidate.Product == RouteProduct.Exits
                && RouteNaming.NamesMatch(candidate.RouteName, spec.Key));
            var stops = route?.Stops.Where(stop => stop.Status == StopStatus.Active).ToList()
                ?? new List<Stop>();
            var racks = stops.Count > 0 ? stops.Count : spec.FallbackRacks;
            var loadedMonthly = route == null ? 0m : SubscribedMonths.DefaultRatePerMonth(route);
            var shortBase = loadedMonthly > 0 ? loadedMonthly : spec.ShortTermRate;
            rows.Add(new RateSheetRow
            {
                Name = spec.Name,
                Detail = spec.Detail,
                Racks = racks,
                AnnualRate = AnnualBillingHelper.IncreasedMonthlyRate(shortBase, IncreasePercent),
                ShortTermRate = 0
            });

            if (spec.Key is "I-75" or "I-65 & 24" && stops.Count > 0)
            {
                var ky = stops.Count(stop => NormalizeState(stop.State) == "KY");
                var tn = stops.Count(stop => NormalizeState(stop.State) == "TN");
                var label = spec.Key == "I-75" ? "I-75" : "I-65";
                geography.Add($"{label}: {racks} total: {ky} in KY & {tn} in TN");
            }
        }

        return new RateSheetModel
        {
            Effective = DateOnly.FromDateTime(DateTime.Today),
            ServiceLabel = "HOTEL BROCHURE DISTRIBUTION",
            CountHeader = "Racks**",
            CountColumnWidth = 58,
            PrimaryRateHeader = "Rate*",
            ShowShortTermRate = false,
            MapTitle = "ROUTE MAP",
            MapPath = _mapPath,
            Rows = rows,
            GeographyLines = geography,
            Notes = HotelNotes
        };
    }

    public async Task<RateSheetModel> LoadRestAreaAsync(CancellationToken cancellationToken = default)
    {
        var routes = await _context.Routes
            .AsNoTracking()
            .Include(route => route.Stops)
            .Where(route => route.Status == RouteStatus.Active)
            .ToListAsync(cancellationToken);

        var stops = new List<(int Number, int Step, string Name, decimal Price)>();
        foreach (var spec in RestAreaSchedule)
        {
            var route = routes.FirstOrDefault(candidate =>
                candidate.Product == RouteProduct.RestArea
                && RouteNaming.NamesMatch(candidate.RouteName, spec.Key));
            var routeStops = route?.Stops.Where(stop => stop.Status == StopStatus.Active).ToList()
                ?? new List<Stop>();
            if (routeStops.Count == 0)
                continue;

            var perStop = RestAreaMonthlyRate(routeStops.Count);
            foreach (var stop in routeStops)
            {
                stops.Add((
                    LocationNumber(stop.StopName) ?? int.MaxValue,
                    stop.StepNumber ?? int.MaxValue,
                    stop.StopName.Trim(),
                    perStop));
            }
        }

        var rows = stops
            .OrderBy(stop => stop.Number)
            .ThenBy(stop => stop.Step)
            .ThenBy(stop => stop.Name)
            .Select(stop => new RateSheetRow
            {
                Name = stop.Name,
                Racks = 1,
                AnnualRate = stop.Price,
                ShortTermRate = 0
            })
            .ToList();

        return new RateSheetModel
        {
            Effective = DateOnly.FromDateTime(DateTime.Today),
            ServiceLabel = "REST AREA BROCHURE DISTRIBUTION",
            NameHeader = "Stop",
            ShowCount = false,
            PrimaryRateHeader = "Per stop*",
            ShowShortTermRate = false,
            MapTitle = "REST AREA MAP",
            MapPath = _restAreaMapPath,
            Rows = rows,
            Notes = RestAreaNotes
        };
    }

    public byte[] Generate(RateSheetModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginLeft(50);
                page.MarginRight(50);
                page.MarginTop(36);
                page.MarginBottom(36);
                page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Grey.Darken4));
                page.Header().Element(header => ComposeHeader(header, "RATE SCHEDULE"));
                page.Content().PaddingTop(4).Element(content => ComposeRates(content, model));
                page.Footer().Element(ComposeFooter);
            });

            if (!model.MapOnFirstPage)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.MarginLeft(50);
                    page.MarginRight(50);
                    page.MarginTop(36);
                    page.MarginBottom(36);
                    page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Grey.Darken4));
                    page.Header().Element(header => ComposeHeader(header, model.MapTitle));
                    page.Content().PaddingTop(12).Element(content => ComposeMap(content, model.MapPath));
                    page.Footer().Element(ComposeFooter);
                });
            }
        }).GeneratePdf();
    }

    private void ComposeRates(IContainer container, RateSheetModel model)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Line);
            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(intro =>
                {
                    intro.Spacing(2);
                    intro.Item().Text(model.ServiceLabel).FontSize(8).FontColor(Muted);
                    intro.Item().Text("Suggested monthly rates").Bold();
                });

                row.ConstantItem(220).Column(meta =>
                {
                    meta.Spacing(3);
                    MetaRow(meta, "Effective:", model.Effective.ToString("MMMM yyyy", Us));
                });
            });

            col.Item().PaddingTop(12).Element(table => ComposeTable(table, model));

            if (model.GeographyLines.Count > 0)
            {
                col.Item().PaddingTop(8).Column(lines =>
                {
                    lines.Spacing(2);
                    foreach (var geography in model.GeographyLines)
                        lines.Item().Text(geography).FontSize(9);
                });
            }

            if (model.Notes.Count > 0)
            {
                col.Item().PaddingTop(10).Column(notes =>
                {
                    notes.Spacing(4);
                    notes.Item().Text("Notes").FontSize(9).Bold();
                    foreach (var note in model.Notes)
                        notes.Item().Text(note).FontSize(8.5f).FontColor(Muted);
                });
            }

            if (model.MapOnFirstPage && !string.IsNullOrWhiteSpace(model.MapPath))
                col.Item().PaddingTop(8).ExtendVertical().Element(map => ComposeMap(map, model.MapPath, alignTop: true));
        });
    }

    private static void ComposeTable(IContainer container, RateSheetModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                if (model.ShowCount)
                    columns.ConstantColumn(model.CountColumnWidth);
                columns.ConstantColumn(88);
                if (model.ShowShortTermRate)
                    columns.ConstantColumn(88);
            });

            table.Header(header =>
            {
                HeaderCell(header, model.NameHeader, alignRight: false);
                if (model.ShowCount)
                    HeaderCell(header, model.CountHeader, alignRight: false, center: true);
                HeaderCell(header, model.PrimaryRateHeader, alignRight: true);
                if (model.ShowShortTermRate)
                    HeaderCell(header, "4-6 mo*", alignRight: true);
            });

            foreach (var row in model.Rows)
            {
                table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(8).Column(item =>
                {
                    item.Spacing(1);
                    item.Item().Text(row.Name).Bold();
                    if (!string.IsNullOrWhiteSpace(row.Detail))
                        item.Item().Text(row.Detail).FontSize(8.5f).FontColor(Muted);
                });
                if (model.ShowCount)
                {
                    table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(8)
                        .AlignMiddle().Text(row.Racks.ToString("N0", Us)).AlignCenter();
                }
                table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(8)
                    .AlignMiddle().Text(Money(row.AnnualRate)).AlignRight();
                if (model.ShowShortTermRate)
                {
                    table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(8)
                        .AlignMiddle().Text(Money(row.ShortTermRate)).AlignRight();
                }
            }
        });
    }

    private void ComposeMap(IContainer container, string mapPath, bool alignTop = false)
    {
        if (!File.Exists(mapPath))
        {
            container.Text("Map is not available.").FontColor(Muted);
            return;
        }

        if (alignTop)
        {
            // Full content width, shorter than the source image so Kentucky is not stretched tall.
            container.AlignTop().Height(230).Image(mapPath).FitUnproportionally();
            return;
        }

        container.AlignCenter().AlignMiddle().Image(mapPath).FitArea();
    }

    private static int? LocationNumber(string? stopName)
    {
        if (string.IsNullOrWhiteSpace(stopName))
            return null;

        var name = stopName.Trim();
        var index = 0;
        while (index < name.Length && char.IsDigit(name[index]))
            index++;
        if (index == 0 || !int.TryParse(name[..index], out var number))
            return null;
        return number;
    }

    private void ComposeHeader(IContainer container, string title)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignLeft().Column(left =>
            {
                left.Spacing(4);
                left.Item().MaxHeight(64).Element(logo =>
                {
                    if (File.Exists(_logoPath))
                        logo.Image(_logoPath).FitArea();
                    else
                        logo.Text("AD-RACK").FontSize(22).Bold().FontColor(Accent);
                });
                left.Item().Text(Company(_options.InvoiceCompanyWebsite, WaveOptions.DefaultCompanyWebsite))
                    .FontSize(9);
            });

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Spacing(2);
                col.Item().Text(title).FontFamily("Arial").FontSize(26).Bold().AlignRight();
                col.Item().PaddingTop(6).Column(addr =>
                {
                    addr.Spacing(1);
                    addr.Item().Text(Company(_options.InvoiceCompanyName, WaveOptions.DefaultCompanyName))
                        .FontSize(9).Bold().AlignRight();
                    foreach (var line in CompanyAddressLines())
                        addr.Item().Text(line).FontSize(9).AlignRight();
                    addr.Item().Text(Company(_options.InvoiceCompanyPhone, WaveOptions.DefaultCompanyPhone))
                        .FontSize(9).AlignRight();
                });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Line);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(Company(_options.InvoiceCompanyName, WaveOptions.DefaultCompanyName))
                    .FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string label, bool alignRight, bool center = false)
    {
        var cell = header.Cell().Background(Accent).PaddingVertical(7).PaddingHorizontal(8)
            .Text(label).FontColor(Colors.White).Bold();
        if (center)
            cell.AlignCenter();
        else if (alignRight)
            cell.AlignRight();
    }

    private static void MetaRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).Bold().AlignRight();
            row.ConstantItem(110).PaddingLeft(8).Text(value);
        });
    }

    private IEnumerable<string> CompanyAddressLines()
    {
        yield return Company(_options.InvoiceCompanyAddress1, WaveOptions.DefaultCompanyAddress1);
        var suite = Company(_options.InvoiceCompanyAddress2, WaveOptions.DefaultCompanyAddress2);
        if (!string.IsNullOrWhiteSpace(suite))
            yield return suite;
        yield return Company(_options.InvoiceCompanyCityStateZip, WaveOptions.DefaultCompanyCityStateZip);
    }

    private static string NormalizeState(string? state)
    {
        var value = (state ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;
        if (value.Equals("Kentucky", StringComparison.OrdinalIgnoreCase))
            return "KY";
        if (value.Equals("Tennessee", StringComparison.OrdinalIgnoreCase))
            return "TN";
        if (value.Equals("Ohio", StringComparison.OrdinalIgnoreCase))
            return "OH";
        if (value.Equals("Indiana", StringComparison.OrdinalIgnoreCase))
            return "IN";
        return value.ToUpperInvariant();
    }

    private static string Company(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static decimal IncreasedQuote(decimal quote) =>
        AnnualBillingHelper.IncreasedMonthlyRate(quote, IncreasePercent);

    private static decimal RestAreaMonthlyRate(int locations)
    {
        var quote = locations <= 3 ? 40m : locations <= 5 ? 35m : 30m;
        return IncreasedQuote(quote);
    }

    private static string Money(decimal amount) => amount.ToString("C0", Us);

    private sealed record RouteRateSpec(
        string Key,
        string Name,
        string? Detail,
        decimal AnnualRate,
        decimal ShortTermRate,
        int FallbackRacks);
}
