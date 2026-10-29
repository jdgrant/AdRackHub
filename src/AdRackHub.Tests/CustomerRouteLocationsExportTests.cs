using AdRackHub.Models;
using AdRackHub.Services;
using ClosedXML.Excel;
using Xunit;

namespace AdRackHub.Tests;

public class CustomerRouteLocationsExportTests
{
    [Fact]
    public void Build_ListsSelectedStopsOnceForEachActiveRoute()
    {
        var north = Route("Exit - North", new[]
        {
            Stop(1, 2, "Second", StopType.Hotel, "I-75 Exit 2"),
            Stop(2, 1, "First", StopType.Attraction, "I-75 Exit 1"),
            Stop(3, null, "Unnumbered", StopType.Other, null),
            Stop(4, 3, "Closed", StopType.Hotel, "I-75 Exit 3", StopStatus.Inactive)
        });
        var south = Route("Exit - South", new[]
        {
            Stop(5, 1, "Welcome", StopType.RestArea, "I-65 Exit 10", address: null, city: "Louisville", state: "KY")
        });

        var customer = new Customer
        {
            Id = 7,
            CustomerName = "Acme / Widgets",
            Contracts =
            {
                Contract("2026", false, null, north, south),
                Contract("Renewal", false, null, north),
                Contract("Proposal", true, null, north),
                Contract("Expired", false, new DateOnly(2020, 1, 1), south)
            },
            CustomerRoutes =
            {
                new CustomerRoute
                {
                    RouteId = north.Id,
                    Status = CustomerRouteStatus.Active,
                    AllStops = false,
                    CustomerRouteStops =
                    {
                        new CustomerRouteStop { StopId = 1, Stop = north.Stops.First(s => s.Id == 1) },
                        new CustomerRouteStop { StopId = 4, Stop = north.Stops.First(s => s.Id == 4) }
                    }
                }
            }
        };

        var outlines = CustomerRouteLocations.Build(customer);

        Assert.Equal(new[] { "Exit - North", "Exit - South" }, outlines.Select(o => o.Route.RouteName));
        Assert.False(outlines[0].AllStops);
        Assert.Equal(new[] { "Second" }, outlines[0].Stops.Select(s => s.StopName));
        Assert.True(outlines[1].AllStops);
        Assert.Equal(new[] { "Welcome" }, outlines[1].Stops.Select(s => s.StopName));
        Assert.Equal("Louisville, KY", CustomerRouteLocations.FormatLocation(outlines[1].Stops[0]));

        using var stream = new MemoryStream();
        CustomerRouteLocationsExportService.Write(stream, outlines);
        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(CustomerRouteLocationsExportService.WorksheetName);

        Assert.Equal(
            new[] { "Route", "#", "Stop", "Type", "Location", "Highway / Exit" },
            Enumerable.Range(1, 6).Select(column => sheet.Cell(1, column).GetString()));
        Assert.Equal("Exit - North", sheet.Cell(2, 1).GetString());
        Assert.Equal(2, sheet.Cell(2, 2).GetValue<int>());
        Assert.Equal("Second", sheet.Cell(2, 3).GetString());
        Assert.Equal("Hotel", sheet.Cell(2, 4).GetString());
        Assert.Equal("—", sheet.Cell(2, 5).GetString());
        Assert.Equal("I-75 Exit 2", sheet.Cell(2, 6).GetString());
        Assert.Equal("Exit - South", sheet.Cell(3, 1).GetString());
        Assert.Equal("Welcome", sheet.Cell(3, 3).GetString());
        Assert.Equal("RestArea", sheet.Cell(3, 4).GetString());
        Assert.Equal("Louisville, KY", sheet.Cell(3, 5).GetString());
        Assert.Equal("Acme - Widgets route locations.xlsx", CustomerRouteLocationsExportService.FileName(customer));
    }

    [Fact]
    public void Build_OrdersAllActiveStopsByStepThenName()
    {
        var route = Route("Exit - Central OH", new[]
        {
            Stop(2, 10, "Later", StopType.Hotel, "I-71 Exit 10"),
            Stop(1, 2, "Earlier", StopType.Hotel, "I-71 Exit 2"),
            Stop(3, null, "Zebra", StopType.Attraction, null),
            Stop(4, null, "Alpha", StopType.Attraction, "— wait")
        });
        var customer = new Customer
        {
            CustomerName = "Sample",
            Contracts = { Contract("Main", false, null, route) }
        };

        var stops = CustomerRouteLocations.Build(customer).Single().Stops.Select(s => s.StopName).ToList();

        Assert.Equal(new[] { "Earlier", "Later", "Alpha", "Zebra" }, stops);
    }

    private static int _nextRouteId = 1;

    private static CustomerContract Contract(
        string name,
        bool isProposal,
        DateOnly? endDate,
        params Route[] routes) =>
        new()
        {
            ContractName = name,
            IsProposal = isProposal,
            ContractEndDate = endDate,
            ContractRoutes = routes.Select(route => new CustomerContractRoute
            {
                RouteId = route.Id,
                Route = route
            }).ToList()
        };

    private static Route Route(string name, IEnumerable<Stop> stops)
    {
        var route = new Route
        {
            Id = _nextRouteId++,
            RouteName = name,
            Stops = stops.ToList()
        };
        foreach (var stop in route.Stops)
            stop.RouteId = route.Id;
        return route;
    }

    private static Stop Stop(
        int id,
        int? step,
        string name,
        StopType type,
        string? highway,
        StopStatus status = StopStatus.Active,
        string? address = null,
        string? city = null,
        string? state = null) =>
        new()
        {
            Id = id,
            StepNumber = step,
            StopName = name,
            StopType = type,
            HighwayExit = highway,
            Status = status,
            Address = address,
            City = city,
            State = state
        };
}
