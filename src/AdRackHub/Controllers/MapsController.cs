using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.RoutesStops)]
public class MapsController : Controller
{
    private static readonly string[] RouteColors =
    {
        "#e6194b", "#3cb44b", "#4363d8", "#f58231", "#911eb4",
        "#42d4f4", "#f032e6", "#bfef45", "#469990", "#dcbeff",
        "#9A6324", "#800000", "#aaffc3", "#808000", "#000075",
        "#ffe119", "#ffd8b1", "#e6beff", "#008080", "#aa6e28"
    };

    private const string ProspectHotelColor = "#6c757d";

    private readonly ApplicationDbContext _context;

    public MapsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(int? routeId)
    {
        var routes = await _context.Routes
            .OrderBy(r => r.RouteName)
            .Select(r => new OverviewMapRouteOption { Id = r.Id, Name = r.RouteName })
            .ToListAsync();

        var colorByRouteId = routes
            .Select((route, index) => (route.Id, Color: RouteColors[index % RouteColors.Length]))
            .ToDictionary(x => x.Id, x => x.Color);

        var selected = routeId is int id ? routes.FirstOrDefault(r => r.Id == id) : null;
        if (routeId.HasValue && selected == null)
            return NotFound();

        var mappable = _context.Stops
            .Include(s => s.Route)
            .Where(s => s.Status == StopStatus.Active
                && s.Latitude != null
                && s.Longitude != null);

        var routeStopsQuery = mappable.Where(s => s.StopType != StopType.ProspectStop);
        if (selected != null)
            routeStopsQuery = routeStopsQuery.Where(s => s.RouteId == selected.Id);

        var routeStops = await routeStopsQuery
            .OrderBy(s => s.Route!.RouteName)
            .ThenBy(s => s.StepNumber ?? int.MaxValue)
            .ThenBy(s => s.StopName)
            .ToListAsync();

        var markers = routeStops.Select(s => ToMarker(
            s,
            colorByRouteId.GetValueOrDefault(s.RouteId, RouteColors[0]),
            "stop",
            fitBounds: true)).ToList();

        var legend = selected == null
            ? routes
                .Where(r => routeStops.Any(s => s.RouteId == r.Id))
                .Select(r => new OverviewMapLegendItem
                {
                    Label = r.Name,
                    Color = colorByRouteId[r.Id]
                })
                .ToList()
            : new List<OverviewMapLegendItem>
            {
                new() { Label = selected.Name, Color = colorByRouteId[selected.Id] }
            };

        var prospectQuery = mappable.Where(s => s.StopType == StopType.ProspectStop);
        if (selected != null)
            prospectQuery = prospectQuery.Where(s => s.RouteId == selected.Id);

        var prospectHotels = await prospectQuery
            .OrderBy(s => s.StopName)
            .ToListAsync();

        if (prospectHotels.Count > 0)
        {
            markers.AddRange(prospectHotels.Select(s => ToMarker(
                s,
                ProspectHotelColor,
                "prospect",
                fitBounds: false)));

            legend.Add(new OverviewMapLegendItem
            {
                Label = "Prospect hotels",
                Color = ProspectHotelColor
            });
        }

        return View(new OverviewMapViewModel
        {
            SelectedRouteId = selected?.Id,
            SelectedRouteName = selected?.Name,
            Routes = routes,
            Legend = legend,
            Markers = markers
        });
    }

    private OverviewMapMarker ToMarker(Stop stop, string color, string kind, bool fitBounds)
    {
        var cityState = string.Join(", ",
            new[] { stop.City, stop.State }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var street = stop.Address?.Trim();
        return new OverviewMapMarker
        {
            Id = stop.Id,
            Name = stop.StopName,
            Location = stop.Route?.RouteName ?? string.Empty,
            Address = StopAddressHelper.FormatFullAddress(stop),
            Street = string.IsNullOrWhiteSpace(street) ? "—" : street,
            CityState = string.IsNullOrWhiteSpace(cityState) ? "—" : cityState,
            ExitNumber = string.IsNullOrWhiteSpace(stop.HighwayExit) ? "—" : stop.HighwayExit.Trim(),
            RouteName = stop.Route?.RouteName ?? string.Empty,
            Kind = kind,
            Color = color,
            Latitude = stop.Latitude!.Value,
            Longitude = stop.Longitude!.Value,
            FitBounds = fitBounds,
            DetailsUrl = Url.Action("Details", "Stops", new { id = stop.Id }) ?? string.Empty
        };
    }
}
