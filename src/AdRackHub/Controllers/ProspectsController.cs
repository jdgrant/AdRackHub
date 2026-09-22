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

    public async Task<IActionResult> Index(string? search, CustomerStatus? status, bool highValue = false, bool needsMoreInfo = false)
    {
        var query = _context.Customers
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

        var prospectCount = await query.CountAsync();
        var highValueCount = await query.CountAsync(c => c.IsHighValueProspect);
        var needsMoreInfoCount = await query.CountAsync(c => c.NeedsMoreInfo);

        if (highValue)
            query = query.Where(c => c.IsHighValueProspect);
        if (needsMoreInfo)
            query = query.Where(c => c.NeedsMoreInfo);

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.HighValue = highValue;
        ViewBag.NeedsMoreInfo = needsMoreInfo;

        var prospects = await query
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.BrochureScans.OrderByDescending(s => s.UploadedAt).Take(3))
            .OrderBy(c => c.CustomerName)
            .ToListAsync();
        CustomerListNavigation.Store(HttpContext.Session, new CustomerListNavState
        {
            Type = CustomerType.Prospect,
            Search = search,
            Status = status,
            HighValue = highValue,
            NeedsMoreInfo = needsMoreInfo,
            Ids = prospects.Select(c => c.Id).ToList()
        });

        var model = new CustomerIndexViewModel
        {
            Customers = prospects,
            Summary = new CustomerIndexSummary
            {
                ClientCount = prospectCount,
                HighValueCount = highValueCount,
                NeedsMoreInfoCount = needsMoreInfoCount
            },
            ListType = CustomerType.Prospect
        };

        return View("~/Views/Customers/Index.cshtml", model);
    }

    public IActionResult Create() =>
        RedirectToAction("Create", "Customers", new { type = CustomerType.Prospect });
}
