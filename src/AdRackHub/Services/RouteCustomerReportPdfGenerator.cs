using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DistributionRoute = AdRackHub.Models.Route;

namespace AdRackHub.Services;

public sealed class RouteCustomerReportSection
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<RouteCustomerReportRow> Rows { get; init; } = Array.Empty<RouteCustomerReportRow>();
}

public sealed class RouteCustomerReportRow
{
    public string BrochureName { get; init; } = string.Empty;
    public string DatesOfDist { get; init; } = string.Empty;
    public string DriversNotes { get; init; } = string.Empty;
}

public class RouteCustomerReportPdfGenerator
{
    private readonly ApplicationDbContext _context;

    public RouteCustomerReportPdfGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<byte[]> BuildHotelAsync(CancellationToken cancellationToken = default)
    {
        var routes = await LoadActiveAssignmentsAsync(cancellationToken);
        var notes = await LoadDriversNotesAsync(cancellationToken);
        var sections = routes
            .Where(r => r.Product == RouteProduct.Exits)
            .OrderBy(r => r.RouteName)
            .Select(r => new RouteCustomerReportSection
            {
                Title = $"{RouteNaming.DisplayName(r.RouteName).ToUpperInvariant()} ROUTE",
                Rows = RowsForRoute(r, notes)
            })
            .Where(s => s.Rows.Count > 0)
            .ToList();

        return Generate(sections, "DATES OF DIST");
    }

    public async Task<byte[]> BuildRestAreaAsync(CancellationToken cancellationToken = default)
    {
        var routes = await LoadActiveAssignmentsAsync(cancellationToken);
        var notes = await LoadDriversNotesAsync(cancellationToken);
        var sections = new List<RouteCustomerReportSection>();

        foreach (var route in routes
            .Where(r => r.Product == RouteProduct.RestArea)
            .OrderBy(r => r.RouteName))
        {
            var stops = route.Stops
                .Where(s => s.Status == StopStatus.Active)
                .OrderBy(s => s.StepNumber ?? int.MaxValue)
                .ThenBy(s => s.StopName);

            foreach (var stop in stops)
            {
                var rows = RowsForStop(route, stop, notes);
                if (rows.Count == 0)
                    continue;

                sections.Add(new RouteCustomerReportSection
                {
                    Title = stop.StopName.Trim().ToUpperInvariant(),
                    Rows = rows
                });
            }
        }

        return Generate(sections, "DIST. PERIOD");
    }

    private async Task<List<DistributionRoute>> LoadActiveAssignmentsAsync(CancellationToken cancellationToken) =>
        await _context.Routes
            .AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active)
            .Include(r => r.Stops)
            .Include(r => r.CustomerRoutes)
                .ThenInclude(cr => cr.Customer)
                    .ThenInclude(c => c.WarehouseLocations)
            .Include(r => r.CustomerRoutes)
                .ThenInclude(cr => cr.CustomerRouteStops)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<(int CustomerId, int RouteId), string>> LoadDriversNotesAsync(
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var contracts = await _context.CustomerContracts
            .AsNoTracking()
            .Where(c => c.DriversNotes != null)
            .Select(c => new
            {
                c.Id,
                c.CustomerId,
                c.DriversNotes,
                c.ContractEndDate,
                RouteIds = c.ContractRoutes.Select(cr => cr.RouteId).ToList()
            })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<(int CustomerId, int RouteId), string>();
        foreach (var contract in contracts.OrderBy(c => c.Id))
        {
            var note = WarehouseLocation.NullIfEmpty(contract.DriversNotes);
            if (note == null)
                continue;
            if (contract.ContractEndDate.HasValue && contract.ContractEndDate.Value < today)
                continue;

            foreach (var routeId in contract.RouteIds)
                map.TryAdd((contract.CustomerId, routeId), note);
        }

        return map;
    }

    private static IReadOnlyList<RouteCustomerReportRow> RowsForRoute(
        DistributionRoute route,
        IReadOnlyDictionary<(int CustomerId, int RouteId), string> driversNotes) =>
        route.CustomerRoutes
            .Where(IsReportCustomer)
            .OrderBy(cr => cr.Customer.CustomerName)
            .Select(cr => ToRow(cr, driversNotes.GetValueOrDefault((cr.CustomerId, route.Id), string.Empty)))
            .ToList();

    private static IReadOnlyList<RouteCustomerReportRow> RowsForStop(
        DistributionRoute route,
        Stop stop,
        IReadOnlyDictionary<(int CustomerId, int RouteId), string> driversNotes) =>
        route.CustomerRoutes
            .Where(IsReportCustomer)
            .Where(cr => AssignedToStop(cr, stop.Id))
            .OrderBy(cr => cr.Customer.CustomerName)
            .Select(cr => ToRow(cr, driversNotes.GetValueOrDefault((cr.CustomerId, route.Id), string.Empty)))
            .ToList();

    private static bool AssignedToStop(CustomerRoute assignment, int stopId) =>
        assignment.AllStops || assignment.CustomerRouteStops.Any(crs => crs.StopId == stopId);

    private static bool IsReportCustomer(CustomerRoute assignment) =>
        assignment.Status == CustomerRouteStatus.Active
        && assignment.Customer.Type == CustomerType.Customer
        && assignment.Customer.Status == CustomerStatus.Active;

    private static RouteCustomerReportRow ToRow(CustomerRoute assignment, string driversNotes)
    {
        var name = assignment.Customer.CustomerName.Trim().ToUpperInvariant();
        var location = WarehouseLocation.FormatBracketMany(
            assignment.Customer.WarehouseLocations,
            assignment.Customer.Warehouse,
            assignment.Customer.WarehouseRack,
            assignment.Customer.WarehouseBin,
            assignment.Customer.WarehouseShelf,
            assignment.Customer.BrochureCode);
        return new()
        {
            BrochureName = location == null ? name : $"{name} {location}",
            DatesOfDist = SubscribedMonths.FormatDistributionPeriod(assignment.SubscribedMonthMask),
            DriversNotes = driversNotes
        };
    }

    private static byte[] Generate(IReadOnlyList<RouteCustomerReportSection> sections, string periodHeader)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var printedOn = DateTime.Today.ToString("M/d/yyyy");

        if (sections.Count == 0)
        {
            sections = new[]
            {
                new RouteCustomerReportSection
                {
                    Title = "NO CUSTOMERS",
                    Rows = Array.Empty<RouteCustomerReportRow>()
                }
            };
        }

        return Document.Create(container =>
        {
            foreach (var section in sections)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.MarginLeft(48);
                    page.MarginRight(48);
                    page.MarginTop(40);
                    page.MarginBottom(36);
                    page.DefaultTextStyle(text => text
                        .FontFamily("Courier New")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken4));

                    page.Header().Column(header =>
                    {
                        header.Item().AlignCenter().Text($"{section.Title}    {printedOn}").Bold();
                        header.Item().PaddingTop(12).Row(row =>
                        {
                            row.RelativeItem(5).Text("BROCHURE NAME").Bold();
                            row.RelativeItem(2).Text(periodHeader).Bold();
                            row.RelativeItem(3).Text("DRIVERS NOTES").Bold();
                        });
                        header.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                    });

                    page.Content().PaddingTop(6).Column(col =>
                    {
                        if (section.Rows.Count == 0)
                        {
                            col.Item().Text("No active customers on this list.");
                            return;
                        }

                        foreach (var row in section.Rows)
                        {
                            col.Item().PaddingVertical(1.5f).Row(line =>
                            {
                                line.RelativeItem(5).Text(row.BrochureName);
                                line.RelativeItem(2).Text(row.DatesOfDist);
                                line.RelativeItem(3).Text(row.DriversNotes);
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }
}
