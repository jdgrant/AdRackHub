using System.Globalization;
using AdRackHub.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdRackHub.Services;

public class ContractAgreementPdfGenerator
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly Color Accent = Color.FromHex("#F5A623");
    private static readonly Color Muted = Color.FromHex("#6B7280");
    private static readonly Color Rule = Color.FromHex("#E5E7EB");
    private static readonly Color LinkBlue = Color.FromHex("#2563EB");

    private readonly WaveOptions _options;
    private readonly string _logoPath;

    public ContractAgreementPdfGenerator(IWebHostEnvironment environment, IOptions<WaveOptions> options)
    {
        _options = options.Value;
        _logoPath = Path.Combine(environment.WebRootPath, "images", "ad-rack-logo.png");
    }

    public byte[] Generate(CustomerContract contract)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var model = Build(contract);
        return Document.Create(container =>
        {
            if (model.IncludeRestArea)
                container.Page(page => ComposePage(page, model, RouteProduct.RestArea));
            if (model.IncludeExits)
                container.Page(page => ComposePage(page, model, RouteProduct.Exits));
            if (!model.IncludeRestArea && !model.IncludeExits)
                container.Page(page => ComposePage(page, model, RouteProduct.Exits));
        }).GeneratePdf();
    }

    public static string DownloadFileName(CustomerContract contract)
    {
        var customer = Sanitize(contract.Customer?.CustomerName ?? "Contract");
        var products = contract.ContractRoutes
            .Select(cr => RouteProductHelper.FromRouteName(cr.Route?.RouteName))
            .Distinct()
            .ToList();
        var product = products.Count == 1 && products[0] == RouteProduct.RestArea
            ? "Rest Area"
            : products.Count == 1
                ? "Exit"
                : "Ad-Rack";
        return $"{product} Contract - {customer}.pdf";
    }

    public static ContractAgreementPdfModel Build(CustomerContract contract)
    {
        var months = AnnualBillingHelper.BillingMonths(contract);
        var beginning = contract.ContractStartDate ?? contract.NextBillDate;
        var lines = BuildRouteLines(contract);
        var monthlyTotal = lines.Where(l => l.Selected).Sum(l => l.MonthlyAmount);
        return new ContractAgreementPdfModel
        {
            CustomerName = (contract.Customer?.CustomerName ?? contract.ContractName).Trim(),
            MonthlyTotal = monthlyTotal,
            AdvertisingSpaces = contract.AdvertisingSpaces > 0 ? contract.AdvertisingSpaces : 1,
            PeriodLabel = $"{months} month{(months == 1 ? "" : "s")}",
            BeginningLabel = beginning?.ToString("MMMM d, yyyy", Us),
            RestAreaLines = lines.Where(l => l.Product == RouteProduct.RestArea).ToList(),
            ExitLines = lines.Where(l => l.Product == RouteProduct.Exits).ToList()
        };
    }

    private void ComposePage(PageDescriptor page, ContractAgreementPdfModel model, RouteProduct product)
    {
        page.Size(PageSizes.Letter);
        page.MarginLeft(50);
        page.MarginRight(50);
        page.MarginTop(36);
        page.MarginBottom(40);
        page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Grey.Darken4));
        page.Header().Element(header => ComposeHeader(header, product));
        page.Content().PaddingTop(4).Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Rule);
            col.Item().PaddingTop(10).Element(body => DrawPreamble(body, model, product));
            col.Item().PaddingTop(22).Element(routes =>
            {
                if (product == RouteProduct.RestArea)
                    DrawRestAreaRoutes(routes, model.RestAreaLines);
                else
                    DrawExitRoutes(routes, model.ExitLines);
            });
            col.Item().AlignRight().Width(250).PaddingTop(10).Column(totals =>
            {
                totals.Item().Row(total =>
                {
                    total.RelativeItem().Text("Monthly total:").Bold().AlignRight();
                    total.ConstantItem(78).Text(model.MonthlyTotal.ToString("C", Us)).AlignRight();
                });
                totals.Item().PaddingTop(4).LineHorizontal(1).LineColor(Rule);
                totals.Item().PaddingTop(6).Row(due =>
                {
                    due.RelativeItem().Text("Amount due (USD):").Bold().AlignRight();
                    due.ConstantItem(78).Text(model.MonthlyTotal.ToString("C", Us)).Bold().AlignRight();
                });
            });
            col.Item().Extend().AlignBottom().Element(DrawSignatures);
        });
        page.Footer().Element(ComposeFooter);
    }

    private void ComposeHeader(IContainer container, RouteProduct product)
    {
        var subtitle = product == RouteProduct.RestArea
            ? "Rest Area Brochure Distribution"
            : "Hotel Brochure Distribution";

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
                left.Item().Text(CompanyText(_options.InvoiceCompanyWebsite, WaveOptions.DefaultCompanyWebsite))
                    .FontSize(9);
            });

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Spacing(2);
                col.Item().Text("CONTRACT").FontFamily("Arial").FontSize(30).Bold().AlignRight();
                col.Item().Text(subtitle).FontSize(8.5f).FontColor(Muted).AlignRight();
                col.Item().PaddingTop(6).Column(addr =>
                {
                    addr.Spacing(1);
                    addr.Item().Text(CompanyText(_options.InvoiceCompanyName, WaveOptions.DefaultCompanyName))
                        .FontSize(9).Bold().AlignRight();
                    foreach (var line in CompanyAddressLines())
                        addr.Item().Text(line).FontSize(9).AlignRight();
                    addr.Item().Text(CompanyText(_options.InvoiceCompanyPhone, WaveOptions.DefaultCompanyPhone))
                        .FontSize(9).AlignRight();
                });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(block =>
        {
            block.Item().LineHorizontal(1).LineColor(Rule);
            block.Item().PaddingTop(6).Text("Important shipping information")
                .FontSize(9).Bold().FontColor(Colors.Black);
            block.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().PaddingRight(10).Column(ship =>
                {
                    ship.Spacing(1);
                    ship.Item().Text("Ship to").FontSize(9).Bold();
                    ship.Item().PaddingTop(4).Text("AD-RACK / UNIT A-44").FontSize(8).Bold();
                    ship.Item().Text("c/o Public Storage").FontSize(8);
                    ship.Item().Text("3520 Chamberlain Ln").FontSize(8);
                    ship.Item().Text("Louisville, KY 40241").FontSize(8);
                });
                row.RelativeItem().BorderLeft(1).BorderColor(Rule).PaddingHorizontal(10).Column(freight =>
                {
                    freight.Spacing(2);
                    freight.Item().Text("Freight bill").FontSize(9).Bold();
                    freight.Item().Text("Pre-pay, including ground-level inside delivery and a lift gate if needed. Mark the freight bill “Inside ground level delivery required”.")
                        .FontSize(8);
                    freight.Item().Text("Maximum 3 pallets at a time. Replenish upon request by Ad-Rack.")
                        .FontSize(8);
                });
                row.RelativeItem().BorderLeft(1).BorderColor(Rule).PaddingLeft(10).Column(office =>
                {
                    office.Spacing(2);
                    office.Item().Text("Office").FontSize(9).Bold();
                    office.Item().Text(CompanyText(_options.InvoiceCompanyName, WaveOptions.DefaultCompanyName))
                        .FontSize(8).Bold();
                    foreach (var line in CompanyAddressLines())
                        office.Item().Text(line).FontSize(8);
                });
            });
        });
    }

    private static void DrawPreamble(IContainer container, ContractAgreementPdfModel model, RouteProduct product)
    {
        var spaceNoun = product == RouteProduct.RestArea ? "rest area" : "display rack";
        container.Column(col =>
        {
            col.Spacing(10);
            col.Item().Text(text =>
            {
                text.Span("For the sum of ");
                Blank(text, Money(model.MonthlyTotal), 16);
                text.Span(" per month, paid quarterly in advance, Ad-Rack agrees to rent ");
                Blank(text, model.AdvertisingSpaces.ToString(Us), 6);
                text.Span($" advertising space(s) per {spaceNoun} to ");
                Blank(text, model.CustomerName, 28);
                text.Span(".");
            });
            col.Item().Text(text =>
            {
                text.Span("Ad-Rack agrees to stock this space with brochures provided by ");
                Blank(text, model.CustomerName, 40);
                text.Span(".");
            });
            col.Item().Text(text =>
            {
                text.Span("The period of this contract is ");
                Blank(text, model.PeriodLabel, 16);
                text.Span(" beginning ");
                Blank(text, model.BeginningLabel, 22);
                text.Span(".");
            });
            col.Item().Hyperlink(WaveOptions.DefaultTermsUrl).Text(text =>
            {
                text.Span(WaveOptions.DefaultContractTerms + " ");
                text.Span(DisplayTermsUrl(WaveOptions.DefaultTermsUrl)).FontColor(LinkBlue);
            });
        });
    }

    private static void DrawRestAreaRoutes(IContainer container, IReadOnlyList<ContractAgreementRouteLine> lines)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(90);
                columns.ConstantColumn(90);
            });
            table.Header(header =>
            {
                HeaderCell(header, "Items");
                HeaderCell(header, "Rest Areas", alignCenter: true);
                HeaderCell(header, "Monthly", alignRight: true);
            });
            foreach (var line in lines.Where(l => l.Selected))
            {
                ItemNameCell(table, line.Title);
                ItemValueCell(table, line.LocationCount.ToString(Us), alignCenter: true);
                ItemValueCell(table, line.MonthlyAmount.ToString("C", Us), alignRight: true);
            }
        });
    }

    private static void DrawExitRoutes(IContainer container, IReadOnlyList<ContractAgreementRouteLine> lines)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(90);
            });
            table.Header(header =>
            {
                HeaderCell(header, "Items");
                HeaderCell(header, "Monthly", alignRight: true);
            });
            foreach (var line in lines.Where(l => l.Selected))
            {
                ItemNameCell(table, line.Title.Replace("\n", " ", StringComparison.Ordinal));
                ItemValueCell(table, line.MonthlyAmount.ToString("C", Us), alignRight: true);
            }
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string label, bool alignCenter = false, bool alignRight = false)
    {
        var cell = header.Cell().Background(Accent).PaddingVertical(7).PaddingHorizontal(8)
            .Text(label).FontColor(Colors.White).Bold();
        if (alignCenter) cell.AlignCenter();
        if (alignRight) cell.AlignRight();
    }

    private static void ItemNameCell(TableDescriptor table, string title)
    {
        table.Cell().BorderBottom(1).BorderColor(Rule).PaddingVertical(5).PaddingHorizontal(8)
            .Text(title).Bold();
    }

    private static void ItemValueCell(TableDescriptor table, string value, bool alignCenter = false, bool alignRight = false)
    {
        var text = table.Cell().BorderBottom(1).BorderColor(Rule).PaddingVertical(5).PaddingHorizontal(8)
            .Text(value);
        if (alignCenter) text.AlignCenter();
        if (alignRight) text.AlignRight();
    }

    private static void DrawSignatures(IContainer container)
    {
        container.PaddingBottom(16).Row(row =>
        {
            row.RelativeItem().PaddingRight(16).Text(
                    "It is the responsibility of the lessee to supply a sufficient number of brochures to stock the display racks for the duration of this contract.")
                .FontSize(9).FontColor(Muted);
            row.RelativeItem().Column(col =>
            {
                col.Item().LineHorizontal(1).LineColor(Rule);
                col.Item().AlignCenter().PaddingTop(2).Text("Lessee signature / date").FontSize(8).FontColor(Muted);
                col.Item().PaddingTop(14).LineHorizontal(1).LineColor(Rule);
                col.Item().AlignCenter().PaddingTop(2).Text("Lessee printed name").FontSize(8).FontColor(Muted);
                col.Item().PaddingTop(14).LineHorizontal(1).LineColor(Rule);
                col.Item().AlignCenter().PaddingTop(2).Text("Ad-Rack / date").FontSize(8).FontColor(Muted);
            });
        });
    }

    private static void Blank(TextDescriptor text, string? value, int minChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            text.Span(new string('_', Math.Max(minChars, 4)));
            return;
        }

        text.Span($" {value.Trim()} ").Underline().SemiBold();
    }

    private static string? Money(decimal amount, bool includeSymbol = true)
    {
        if (amount <= 0)
            return null;
        var format = includeSymbol
            ? (amount == decimal.Truncate(amount) ? "C0" : "C")
            : (amount == decimal.Truncate(amount) ? "N0" : "N2");
        return amount.ToString(format, Us);
    }

    private IEnumerable<string> CompanyAddressLines()
    {
        yield return CompanyText(_options.InvoiceCompanyAddress1, WaveOptions.DefaultCompanyAddress1);
        var suite = CompanyText(_options.InvoiceCompanyAddress2, WaveOptions.DefaultCompanyAddress2);
        if (!string.IsNullOrWhiteSpace(suite))
            yield return suite;
        yield return CompanyText(_options.InvoiceCompanyCityStateZip, WaveOptions.DefaultCompanyCityStateZip);
    }

    private static string CompanyText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string DisplayTermsUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (uri.Scheme + "://" + uri.Host + uri.AbsolutePath).TrimEnd('/');
        return url.TrimEnd('/');
    }

    private static string Sanitize(string value)
    {
        var chars = value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch).ToArray();
        return new string(chars).Trim();
    }

    private static List<ContractAgreementRouteLine> BuildRouteLines(CustomerContract contract)
    {
        var templates = RestAreaTemplates.Concat(ExitTemplates).ToList();
        var selected = contract.ContractRoutes
            .Select(cr => new
            {
                Route = cr.Route,
                Product = RouteProductHelper.FromRouteName(cr.Route?.RouteName),
                Monthly = AnnualBillingHelper.BillingPeriodAmountToMonthlyRate(
                    AnnualBillingHelper.GetBillingAmount(cr), contract),
                LocationCount = CountLocations(contract, cr)
            })
            .Where(item => item.Route != null)
            .ToList();

        foreach (var item in selected)
        {
            var match = templates.FirstOrDefault(line =>
                line.Product == item.Product && line.Matches(item.Route!.RouteName));
            if (match == null)
            {
                templates.Add(new ContractAgreementRouteLine
                {
                    Product = item.Product,
                    Title = (item.Route!.RouteName ?? "").ToUpperInvariant(),
                    Match = _ => false,
                    Selected = true,
                    MonthlyAmount = item.Monthly,
                    LocationCount = item.LocationCount
                });
                continue;
            }

            match.Selected = true;
            match.MonthlyAmount += item.Monthly;
            match.LocationCount += item.LocationCount;
        }

        return templates;
    }

    private static int CountLocations(CustomerContract contract, CustomerContractRoute contractRoute)
    {
        var routeStops = contractRoute.Route?.Stops?
            .Where(s => s.Status == StopStatus.Active)
            .ToList() ?? new List<Stop>();
        var assignment = contract.Customer?.CustomerRoutes?
            .FirstOrDefault(cr => cr.RouteId == contractRoute.RouteId);
        if (assignment == null || assignment.AllStops)
            return routeStops.Count;
        var selected = assignment.CustomerRouteStops.Select(s => s.StopId).ToHashSet();
        var count = routeStops.Count(s => selected.Contains(s.Id));
        return count > 0 ? count : routeStops.Count;
    }

    private static List<ContractAgreementRouteLine> RestAreaTemplates => new()
    {
        Line(RouteProduct.RestArea, "I-64(EAST OF LEXINGTON): #1,#5,#7,#8,#9", name =>
            ContainsAll(name, "I-64", "East")),
        Line(RouteProduct.RestArea, "I-64(WEST OF LEXINGTON), AND I-71: #3,#4,#17,#18", name =>
            ContainsAll(name, "I-64", "West") || name.Contains("I-71", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.RestArea, "I-75: #22,#23,#24,#25", name =>
            name.Contains("I-75", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.RestArea, "I-65: #15,#16", name =>
            name.Contains("I-65", StringComparison.OrdinalIgnoreCase))
    };

    private static List<ContractAgreementRouteLine> ExitTemplates => new()
    {
        Line(RouteProduct.Exits, "NORTHEAST OHIO ROUTE-\n(CLEVELAND AREA & INTERSTATES)", name =>
            name.Contains("Northeast", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "CENTRAL OHIO ROUTE-\n(COLUMBUS AREA & INTERSTATES)", name =>
            name.Contains("Central OH", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Central Ohio", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "NORTHERN INTERSTATE ROUTE (OH)\n(TOLEDO/DAYTON AREA & INTERSTATES)", name =>
            name.Contains("Northern Interstate", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "GREATER CINCINNATI-NO. KENTUCKY ROUTE", name =>
            name.Contains("Cincinnati", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "GREATER LOUISVILLE, KY ROUTE", name =>
            name.Contains("Louisville", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "LEXINGTON-FRANKFORT, KY ROUTE", name =>
            name.Contains("Lex-Frankfort", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Lexington", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "SOUTHERN INTERSTATE ROUTE (KY, TN)", name =>
            name.Contains("I-75", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("I-65", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("Rest Area", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "1/2 SOUTHERN RT. (KY,TN/I-75 OR I-65, I-24)", name =>
            name.Contains("I-65", StringComparison.OrdinalIgnoreCase)
            || name.Contains("I-24", StringComparison.OrdinalIgnoreCase)),
        Line(RouteProduct.Exits, "MID-TENNESSEE ROUTE (KNOXVILLE, I-40)", name =>
            name.Contains("Mid-TN", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mid-Tennessee", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mid TN", StringComparison.OrdinalIgnoreCase))
    };

    private static ContractAgreementRouteLine Line(RouteProduct product, string title, Func<string, bool> match) =>
        new()
        {
            Product = product,
            Title = title,
            Match = match
        };

    private static bool ContainsAll(string name, params string[] parts) =>
        parts.All(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
}

public sealed class ContractAgreementPdfModel
{
    public string CustomerName { get; init; } = string.Empty;
    public decimal MonthlyTotal { get; init; }
    public int AdvertisingSpaces { get; init; } = 1;
    public string PeriodLabel { get; init; } = string.Empty;
    public string? BeginningLabel { get; init; }
    public IReadOnlyList<ContractAgreementRouteLine> RestAreaLines { get; init; } =
        Array.Empty<ContractAgreementRouteLine>();
    public IReadOnlyList<ContractAgreementRouteLine> ExitLines { get; init; } =
        Array.Empty<ContractAgreementRouteLine>();
    public bool IncludeRestArea => RestAreaLines.Any(l => l.Selected);
    public bool IncludeExits => ExitLines.Any(l => l.Selected);
}

public sealed class ContractAgreementRouteLine
{
    public RouteProduct Product { get; init; }
    public string Title { get; init; } = string.Empty;
    public Func<string, bool> Match { get; init; } = _ => false;
    public bool Selected { get; set; }
    public decimal MonthlyAmount { get; set; }
    public int LocationCount { get; set; }
    public bool Matches(string? routeName) =>
        !string.IsNullOrWhiteSpace(routeName) && Match(routeName);
}
