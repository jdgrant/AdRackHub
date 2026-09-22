using System.Globalization;
using AdRackHub.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdRackHub.Services;

public sealed class InvoicePdfLine
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Quantity { get; init; } = 1;
    public decimal UnitPrice { get; init; }
    public decimal Amount => Quantity * UnitPrice;
}

public sealed class InvoicePdfModel
{
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? ContactName { get; init; }
    public IReadOnlyList<string> BillToLines { get; init; } = Array.Empty<string>();
    public string ServiceLabel { get; init; } = "Brochure Distribution";
    public string? PaymentUrl { get; init; }
    public IReadOnlyList<InvoicePdfLine> Lines { get; init; } = Array.Empty<InvoicePdfLine>();
    public string? Notes { get; init; }
    public decimal Total => Lines.Sum(l => l.Amount);
}

public class InvoicePdfGenerator
{
    private static readonly Color Accent = Color.FromHex("#F5A623");
    private static readonly Color Muted = Color.FromHex("#6B7280");
    private static readonly Color Line = Color.FromHex("#E5E7EB");
    private static readonly Color AmountBand = Color.FromHex("#F3F4F6");
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private readonly WaveOptions _options;
    private readonly string _logoPath;

    public InvoicePdfGenerator(IWebHostEnvironment environment, IOptions<WaveOptions> options)
    {
        _options = options.Value;
        _logoPath = Path.Combine(environment.WebRootPath, "images", "ad-rack-logo.png");
    }

    public InvoicePdfModel Build(
        Customer customer,
        IReadOnlyList<WaveInvoiceLineItem> lineItems,
        string? invoiceNumber,
        DateOnly invoiceDate,
        DateOnly dueDate,
        string? paymentUrl = null)
    {
        var contact = customer.Contacts
            .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .FirstOrDefault();

        return new InvoicePdfModel
        {
            InvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? "—" : invoiceNumber.Trim(),
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            CustomerName = customer.CustomerName,
            ContactName = contact?.PersonName,
            BillToLines = BillToAddressLines(customer, contact),
            ServiceLabel = RouteProductHelper.BrochureDistributionLabel(
                lineItems.Select(item => FirstNonEmpty(item.ProductName, item.Description))),
            PaymentUrl = string.IsNullOrWhiteSpace(paymentUrl) ? null : paymentUrl.Trim(),
            Lines = lineItems.Select(ToPdfLine).ToList(),
            Notes = FirstNonEmpty(_options.InvoiceMemo, _options.InvoiceTerms, WaveOptions.DefaultInvoiceTerms)
        };
    }

    public byte[] Generate(InvoicePdfModel model)
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
                page.MarginBottom(40);
                page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Grey.Darken4));
                page.Header().Element(header => ComposeHeader(header, model));
                page.Content().PaddingTop(4).Element(content => ComposeBody(content, model));
            });
        }).GeneratePdf();
    }

    public static InvoicePdfModel Sample() =>
        new()
        {
            InvoiceNumber = "5",
            InvoiceDate = new DateOnly(2026, 9, 1),
            DueDate = new DateOnly(2026, 10, 1),
            CustomerName = "test customer",
            ContactName = "Jon Grant",
            BillToLines = new[] { "1104 MANNING CT", "La Grange, KY 40031", "United States" },
            ServiceLabel = "Hotel Brochure Distribution",
            PaymentUrl = "https://link.waveapps.com/drzg2t-te6axe",
            Notes = WaveOptions.DefaultInvoiceTerms,
            Lines = new[]
            {
                new InvoicePdfLine
                {
                    Name = "Central OH",
                    Description = "Central OH Distribution — September 1, 2026 through November 30, 2026",
                    Quantity = 1,
                    UnitPrice = 30
                },
                new InvoicePdfLine
                {
                    Name = "Cincinnati-NKY",
                    Description = "Cincinnati-NKY Distribution — September 1, 2026 through November 30, 2026",
                    Quantity = 1,
                    UnitPrice = 30
                },
                new InvoicePdfLine
                {
                    Name = "I-65 and 24",
                    Description = "I-65 & 24 Distribution — September 1, 2026 through November 30, 2026",
                    Quantity = 1,
                    UnitPrice = 30
                }
            }
        };

    private void ComposeHeader(IContainer container, InvoicePdfModel model)
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
                left.Item().Text(CompanyText(_options.InvoiceCompanyWebsite, WaveOptions.DefaultCompanyWebsite))
                    .FontSize(9);
            });

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Spacing(2);
                col.Item().Text("INVOICE").FontFamily("Arial").FontSize(30).Bold().AlignRight();
                col.Item().Text(CompanyText(_options.InvoiceAddressNotice, WaveOptions.DefaultAddressNotice))
                    .FontSize(8.5f).FontColor(Muted).AlignRight();
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

    private void ComposeBody(IContainer container, InvoicePdfModel model)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Line);
            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Column(bill =>
                {
                    bill.Spacing(2);
                    bill.Item().Text("BILL TO").FontSize(8).FontColor(Muted);
                    bill.Item().Text(model.CustomerName).Bold();
                    if (!string.IsNullOrWhiteSpace(model.ContactName))
                        bill.Item().Text(model.ContactName);
                    foreach (var line in model.BillToLines)
                        bill.Item().Text(line);
                    if (!string.IsNullOrWhiteSpace(model.ServiceLabel))
                        bill.Item().PaddingTop(8).Text(model.ServiceLabel);
                });

                row.ConstantItem(250).Column(meta =>
                {
                    meta.Spacing(3);
                    MetaRow(meta, "Invoice Number:", model.InvoiceNumber);
                    MetaRow(meta, "Invoice Date:", model.InvoiceDate.ToString("MMMM d, yyyy", Us));
                    MetaRow(meta, "Payment Due:", model.DueDate.ToString("MMMM d, yyyy", Us));
                    meta.Item().Background(AmountBand).PaddingVertical(4).PaddingHorizontal(6).Row(amount =>
                    {
                        amount.RelativeItem().Text("Amount Due (USD):").Bold().AlignRight();
                        amount.ConstantItem(78).Text(Money(model.Total)).Bold().AlignRight();
                    });
                    if (!string.IsNullOrWhiteSpace(model.PaymentUrl))
                    {
                        meta.Item().PaddingTop(6).AlignRight().Column(pay =>
                        {
                            pay.Item().AlignRight().Hyperlink(model.PaymentUrl)
                                .Text("Pay Securely Online").FontColor(Color.FromHex("#2563EB")).FontSize(10);
                            pay.Item().AlignRight().Hyperlink(model.PaymentUrl)
                                .Text(DisplayPaymentUrl(model.PaymentUrl)).FontColor(Color.FromHex("#2563EB")).FontSize(9);
                        });
                    }
                });
            });

            col.Item().PaddingTop(18).Element(table => ComposeItems(table, model));

            col.Item().AlignRight().Width(250).PaddingTop(12).Column(totals =>
            {
                totals.Item().Row(total =>
                {
                    total.RelativeItem().Text("Total:").Bold().AlignRight();
                    total.ConstantItem(78).Text(Money(model.Total)).AlignRight();
                });
                totals.Item().PaddingTop(4).LineHorizontal(1).LineColor(Line);
                totals.Item().PaddingTop(6).Row(due =>
                {
                    due.RelativeItem().Text("Amount Due (USD):").Bold().AlignRight();
                    due.ConstantItem(78).Text(Money(model.Total)).Bold().AlignRight();
                });
            });

            if (!string.IsNullOrWhiteSpace(model.PaymentUrl))
            {
                col.Item().AlignRight().Width(220).PaddingTop(16).Background(AmountBand).Padding(12).Column(pay =>
                {
                    pay.Spacing(6);
                    pay.Item().AlignCenter().Hyperlink(model.PaymentUrl)
                        .Text("Pay Securely Online").FontColor(Color.FromHex("#2563EB")).SemiBold();
                    pay.Item().AlignCenter().Hyperlink(model.PaymentUrl)
                        .Text(DisplayPaymentUrl(model.PaymentUrl)).FontColor(Color.FromHex("#2563EB")).FontSize(9);
                });
            }

            if (!string.IsNullOrWhiteSpace(model.Notes))
            {
                col.Item().PaddingTop(28).Column(notes =>
                {
                    notes.Spacing(4);
                    notes.Item().Text("Notes / Terms").FontSize(9).Bold();
                    notes.Item().Text(model.Notes).FontSize(9).FontColor(Muted);
                });
            }
        });
    }

    private static void ComposeItems(IContainer container, InvoicePdfModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(70);
                columns.ConstantColumn(80);
                columns.ConstantColumn(80);
            });

            table.Header(header =>
            {
                header.Cell().Background(Accent).PaddingVertical(7).PaddingHorizontal(8)
                    .Text("Items").FontColor(Colors.White).Bold();
                header.Cell().Background(Accent).PaddingVertical(7).PaddingHorizontal(8)
                    .Text("Quantity").FontColor(Colors.White).Bold().AlignCenter();
                header.Cell().Background(Accent).PaddingVertical(7).PaddingHorizontal(8)
                    .Text("Price").FontColor(Colors.White).Bold().AlignRight();
                header.Cell().Background(Accent).PaddingVertical(7).PaddingHorizontal(8)
                    .Text("Amount").FontColor(Colors.White).Bold().AlignRight();
            });

            foreach (var line in model.Lines)
            {
                table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(8).PaddingHorizontal(8).Column(item =>
                {
                    item.Spacing(2);
                    item.Item().Text(line.Name).Bold();
                    if (!string.IsNullOrWhiteSpace(line.Description) &&
                        !string.Equals(line.Description, line.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Item().Text(line.Description).FontSize(9).FontColor(Muted);
                    }
                });
                table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(8).PaddingHorizontal(8)
                    .Text(Quantity(line.Quantity)).AlignCenter();
                table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(8).PaddingHorizontal(8)
                    .Text(Money(line.UnitPrice)).AlignRight();
                table.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(8).PaddingHorizontal(8)
                    .Text(Money(line.Amount)).AlignRight();
            }
        });
    }

    private static void MetaRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).Bold().AlignRight();
            row.ConstantItem(130).PaddingLeft(8).Text(value);
        });
    }

    private IEnumerable<string> CompanyAddressLines()
    {
        yield return CompanyText(_options.InvoiceCompanyAddress1, WaveOptions.DefaultCompanyAddress1);
        var suite = CompanyText(_options.InvoiceCompanyAddress2, WaveOptions.DefaultCompanyAddress2);
        if (!string.IsNullOrWhiteSpace(suite))
            yield return suite;
        yield return CompanyText(_options.InvoiceCompanyCityStateZip, WaveOptions.DefaultCompanyCityStateZip);
    }

    private static InvoicePdfLine ToPdfLine(WaveInvoiceLineItem item) =>
        new()
        {
            Name = FirstNonEmpty(item.ProductName, item.Description) ?? "Item",
            Description = item.Description,
            Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
            UnitPrice = item.UnitPrice
        };

    private static List<string> BillToAddressLines(Customer customer, Contact? contact)
    {
        var lines = new List<string>();
        var street = FirstNonEmpty(contact?.Address, customer.Address);
        if (!string.IsNullOrWhiteSpace(street))
            lines.Add(street);

        var city = FirstNonEmpty(contact?.City, customer.City);
        var stateRaw = FirstNonEmpty(contact?.State, customer.State);
        var zip = FirstNonEmpty(contact?.Zip, customer.Zip);
        var state = UsState.ToAbbreviation(stateRaw);
        var cityLine = string.Join(", ", new[] { city, string.Join(" ", new[] { state, zip }.Where(s => !string.IsNullOrWhiteSpace(s))) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(cityLine))
            lines.Add(cityLine);
        lines.Add("United States");
        return lines;
    }

    private static string DisplayPaymentUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (uri.Host + uri.PathAndQuery).TrimEnd('/');
        return url.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string CompanyText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Money(decimal amount) => amount.ToString("C", Us);

    private static string Quantity(decimal quantity) =>
        quantity == decimal.Truncate(quantity) ? decimal.Truncate(quantity).ToString("0", Us) : quantity.ToString("0.##", Us);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
