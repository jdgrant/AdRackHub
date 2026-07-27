using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Dashboard)]
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var atRiskCustomers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.Type == CustomerType.Customer && c.IsAtRisk)
            .OrderBy(c => c.CustomerName)
            .Select(c => new DashboardCustomerItem
            {
                Id = c.Id,
                Name = c.CustomerName,
                Status = c.Status,
                Type = c.Type
            })
            .ToListAsync();

        var highValueProspects = await _context.Customers
            .AsNoTracking()
            .Where(c => c.Type == CustomerType.Prospect && c.IsHighValueProspect)
            .OrderBy(c => c.CustomerName)
            .Select(c => new DashboardCustomerItem
            {
                Id = c.Id,
                Name = c.CustomerName,
                Status = c.Status,
                Type = c.Type
            })
            .ToListAsync();

        var openTasks = await _context.CustomerTasks
            .AsNoTracking()
            .Where(t => t.Status == CustomerTaskStatus.Open)
            .OrderBy(t => t.DueDate.HasValue ? 0 : 1)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Customer.CustomerName)
            .Take(25)
            .Select(t => new DashboardTaskItem
            {
                Id = t.Id,
                CustomerId = t.CustomerId,
                CustomerName = t.Customer.CustomerName,
                CustomerType = t.Customer.Type,
                Title = t.Title,
                DueDate = t.DueDate,
                IsOverdue = t.DueDate.HasValue && t.DueDate.Value < today
            })
            .ToListAsync();

        var routes = await _context.Routes
            .AsNoTracking()
            .Include(r => r.Stops)
            .Include(r => r.CustomerRoutes)
            .Where(r => r.Status == RouteStatus.Active)
            .OrderBy(r => r.RouteName)
            .ToListAsync();

        var model = new DashboardViewModel
        {
            ActiveCustomerCount = await _context.Customers.CountAsync(c =>
                c.Status == CustomerStatus.Active && c.Type == CustomerType.Customer),
            ProspectCount = await _context.Customers.CountAsync(c =>
                c.Status == CustomerStatus.Active && c.Type == CustomerType.Prospect),
            HighValueProspectCount = highValueProspects.Count,
            AtRiskCustomerCount = atRiskCustomers.Count,
            OpenTaskCount = await _context.CustomerTasks.CountAsync(t => t.Status == CustomerTaskStatus.Open),
            OverdueTaskCount = await _context.CustomerTasks.CountAsync(t =>
                t.Status == CustomerTaskStatus.Open && t.DueDate.HasValue && t.DueDate.Value < today),
            ActiveRouteCount = routes.Count,
            ActiveStopCount = await _context.Stops.CountAsync(s => s.Status == StopStatus.Active),
            AtRiskCustomers = atRiskCustomers,
            HighValueProspects = highValueProspects,
            OpenTasks = openTasks,
            Routes = routes.Select(r => new RouteSummaryItem
            {
                RouteId = r.Id,
                RouteName = r.RouteName,
                AnnualRevenue = r.CustomerRoutes
                    .Where(cr => cr.Status == CustomerRouteStatus.Active)
                    .Sum(cr => cr.RatePerMonth > 0
                        ? cr.RatePerMonth * 12m
                        : AnnualBillingHelper.ToAnnualPrice(r.Price, r.BillingFrequency)),
                BillingFrequency = r.BillingFrequency,
                StopCount = r.Stops.Count(s => s.Status == StopStatus.Active),
                CustomerCount = r.CustomerRoutes.Count(cr => cr.Status == CustomerRouteStatus.Active)
            }).ToList()
        };

        return View(model);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
