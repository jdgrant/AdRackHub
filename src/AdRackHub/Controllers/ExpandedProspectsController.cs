using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class ExpandedProspectsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ExpandedProspectsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, CustomerStatus? status, int? routeId)
    {
        var routes = await _context.Routes
            .AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active)
            .OrderBy(r => r.RouteName)
            .Select(r => new RouteOptionItem { Id = r.Id, Name = r.RouteName })
            .ToListAsync();

        if (routeId.HasValue && routes.All(r => r.Id != routeId.Value))
            return NotFound();

        var query = _context.Customers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.BrochureScans.OrderByDescending(s => s.UploadedAt).Take(3))
            .Include(c => c.ExpandedProspectRoute)
            .Where(c => c.Type == CustomerType.ExpandedProspect);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                c.CustomerName.Contains(search)
                || (c.Address != null && c.Address.Contains(search))
                || (c.City != null && c.City.Contains(search))
                || (c.State != null && c.State.Contains(search))
                || (c.Zip != null && c.Zip.Contains(search))
                || (c.Phone != null && c.Phone.Contains(search))
                || (c.Email != null && c.Email.Contains(search))
                || (c.WebUrl != null && c.WebUrl.Contains(search))
                || c.Contacts.Any(ct =>
                    ct.Name.Contains(search)
                    || (ct.FirstName != null && ct.FirstName.Contains(search))
                    || (ct.LastName != null && ct.LastName.Contains(search))
                    || (ct.Email != null && ct.Email.Contains(search))
                    || (ct.Phone != null && ct.Phone.Contains(search))));
        }

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        if (routeId.HasValue)
            query = query.Where(c => c.ExpandedProspectRouteId == routeId.Value);

        var prospects = await query
            .OrderBy(c => c.ExpandedProspectRoute != null ? c.ExpandedProspectRoute.RouteName : "zzz")
            .ThenBy(c => c.CustomerName)
            .ToListAsync();

        CustomerListNavigation.Store(HttpContext.Session, new CustomerListNavState
        {
            Type = CustomerType.ExpandedProspect,
            Search = search,
            Status = status,
            RouteId = routeId,
            Ids = prospects.Select(c => c.Id).ToList()
        });

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.RouteId = routeId;

        var byRoute = prospects
            .GroupBy(c => c.ExpandedProspectRouteId)
            .ToDictionary(g => g.Key ?? 0, g => g.ToList());

        var groups = new List<ExpandedProspectRouteGroup>();
        if (routeId.HasValue)
        {
            var route = routes.First(r => r.Id == routeId.Value);
            groups.Add(new ExpandedProspectRouteGroup
            {
                RouteId = route.Id,
                RouteName = route.Name,
                Prospects = byRoute.GetValueOrDefault(route.Id) ?? new List<Customer>()
            });
        }
        else
        {
            foreach (var route in routes)
            {
                groups.Add(new ExpandedProspectRouteGroup
                {
                    RouteId = route.Id,
                    RouteName = route.Name,
                    Prospects = byRoute.GetValueOrDefault(route.Id) ?? new List<Customer>()
                });
            }

            if (byRoute.TryGetValue(0, out var unassigned) && unassigned.Count > 0)
            {
                groups.Add(new ExpandedProspectRouteGroup
                {
                    RouteId = null,
                    RouteName = "No route assigned",
                    Prospects = unassigned
                });
            }

            if (!string.IsNullOrWhiteSpace(search) || status.HasValue)
                groups = groups.Where(g => g.Prospects.Count > 0).ToList();
        }

        var model = new ExpandedProspectsIndexViewModel
        {
            Groups = groups,
            Routes = routes,
            SelectedRouteId = routeId,
            TotalCount = prospects.Count,
            UnassignedCount = prospects.Count(c => c.ExpandedProspectRouteId == null),
            RouteCountWithProspects = prospects
                .Select(c => c.ExpandedProspectRouteId)
                .Where(id => id.HasValue)
                .Distinct()
                .Count()
        };

        return View(model);
    }

    public IActionResult Create(int? routeId) =>
        RedirectToAction("Create", "Customers", new { type = CustomerType.ExpandedProspect, routeId });
}
