using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class ProspectsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ProspectsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, CustomerStatus? status)
    {
        var query = _context.Customers
            .Include(c => c.Contacts)
            .Include(c => c.BrochureScans)
            .Include(c => c.CustomerRoutes)
                .ThenInclude(cr => cr.Route)
            .Include(c => c.Contracts)
                .ThenInclude(b => b.ContractRoutes)
                    .ThenInclude(cbr => cbr.Route)
            .Where(c => c.Type == CustomerType.Prospect);

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
                    || (ct.Phone != null && ct.Phone.Contains(search))
                    || (ct.City != null && ct.City.Contains(search))
                    || (ct.Address != null && ct.Address.Contains(search))
                    || (ct.WebUrl != null && ct.WebUrl.Contains(search))));
        }

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        ViewBag.Search = search;
        ViewBag.Status = status;

        var prospects = await query.OrderBy(c => c.CustomerName).ToListAsync();
        CustomerListNavigation.Store(HttpContext.Session, new CustomerListNavState
        {
            Type = CustomerType.Prospect,
            Search = search,
            Status = status,
            Ids = prospects.Select(c => c.Id).ToList()
        });

        var model = new CustomerIndexViewModel
        {
            Customers = prospects,
            Summary = BuildSummary(prospects),
            ListType = CustomerType.Prospect
        };

        return View("~/Views/Customers/Index.cshtml", model);
    }

    public IActionResult Create() =>
        RedirectToAction("Create", "Customers", new { type = CustomerType.Prospect });

    private static CustomerIndexSummary BuildSummary(IReadOnlyList<Customer> customers)
    {
        var totalRevenue = customers.Sum(GetCustomerRevenue);
        return new CustomerIndexSummary
        {
            ClientCount = customers.Count,
            RouteCount = customers.Sum(c =>
                c.CustomerRoutes.Count(cr => cr.Status == CustomerRouteStatus.Active)),
            TotalRevenue = totalRevenue
        };
    }

    private static decimal GetCustomerRevenue(Customer customer)
    {
        if (customer.Contracts.Any())
        {
            return customer.Contracts
                .SelectMany(b => b.ContractRoutes)
                .Sum(AnnualBillingHelper.GetBillingAmount);
        }

        return customer.CustomerRoutes
            .Where(cr => cr.Status == CustomerRouteStatus.Active)
            .Sum(cr => cr.RatePerMonth > 0 ? cr.RatePerMonth : cr.Route.Price);
    }
}
