using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Dashboard)]
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(int days = 5, string? scope = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (days < 0) days = 0;
        if (days > 90) days = 90;
        var showProspects = string.Equals(scope, "prospects", StringComparison.OrdinalIgnoreCase);
        var pipelineEnd = today.AddDays(days);
        var doneStart = today.AddDays(-days);

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

        var pipelineRows = await _context.CustomerNotes
            .AsNoTracking()
            .Where(n => showProspects
                ? n.Customer.Type == CustomerType.Prospect || n.Customer.Type == CustomerType.ExpandedProspect
                : n.Customer.Type == CustomerType.Customer)
            .Where(n =>
                (n.Kind == CustomerNoteKind.BrochuresNeeded
                    && n.Status != CustomerNoteStatus.Done
                    && (n.DueDate == null || n.DueDate <= pipelineEnd))
                || (n.DueDate != null
                    && n.DueDate <= pipelineEnd
                    && (n.Status != CustomerNoteStatus.Done || n.DueDate >= doneStart)))
            .OrderBy(n => n.DueDate.HasValue ? 0 : 1)
            .ThenBy(n => n.DueDate)
            .ThenBy(n => n.Customer.CustomerName)
            .Select(n => new DashboardPipelineItem
            {
                Id = n.Id,
                CustomerId = n.CustomerId,
                CustomerName = n.Customer.CustomerName,
                CustomerType = n.Customer.Type,
                Kind = n.Kind,
                Status = n.Status,
                DueDate = n.DueDate,
                Preview = n.Body,
                IsOverdue = n.Kind != CustomerNoteKind.BrochuresNeeded
                    && n.Status != CustomerNoteStatus.Done
                    && n.DueDate != null
                    && n.DueDate < today
            })
            .ToListAsync();

        foreach (var item in pipelineRows)
            item.Preview = TruncatePreview(item.Preview);

        var routeRows = await _context.Routes
            .AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active)
            .OrderBy(r => r.RouteName)
            .Select(r => new
            {
                r.Id,
                r.RouteName,
                r.Price,
                r.BillingFrequency,
                StopCount = r.Stops.Count(s => s.Status == StopStatus.Active),
                CustomerCount = r.CustomerRoutes.Count(cr => cr.Status == CustomerRouteStatus.Active),
                PricedMonthlySum = r.CustomerRoutes
                    .Where(cr => cr.Status == CustomerRouteStatus.Active && cr.RatePerMonth > 0)
                    .Sum(cr => (decimal?)cr.RatePerMonth) ?? 0m,
                FallbackCount = r.CustomerRoutes
                    .Count(cr => cr.Status == CustomerRouteStatus.Active && cr.RatePerMonth <= 0)
            })
            .ToListAsync();

        var model = new DashboardViewModel
        {
            ActiveCustomerCount = await _context.Customers.CountAsync(c =>
                c.Status == CustomerStatus.Active && c.Type == CustomerType.Customer),
            ProspectCount = await _context.Customers.CountAsync(c =>
                c.Status == CustomerStatus.Active && c.Type == CustomerType.Prospect),
            HighValueProspectCount = highValueProspects.Count,
            AtRiskCustomerCount = atRiskCustomers.Count,
            ActiveRouteCount = routeRows.Count,
            ActiveStopCount = await _context.Stops.CountAsync(s => s.Status == StopStatus.Active),
            AtRiskCustomers = atRiskCustomers,
            HighValueProspects = highValueProspects,
            PipelineDays = days,
            PipelineScope = showProspects ? "prospects" : "customers",
            PipelineNew = pipelineRows.Where(i => i.Status == CustomerNoteStatus.New).ToList(),
            PipelineWorking = pipelineRows.Where(i => i.Status == CustomerNoteStatus.Working).ToList(),
            PipelineDone = pipelineRows.Where(i => i.Status == CustomerNoteStatus.Done).ToList(),
            Routes = routeRows.Select(r => new RouteSummaryItem
            {
                RouteId = r.Id,
                RouteName = r.RouteName,
                AnnualRevenue = (r.PricedMonthlySum * 12m)
                    + (r.FallbackCount * AnnualBillingHelper.ToAnnualPrice(r.Price, r.BillingFrequency)),
                BillingFrequency = r.BillingFrequency,
                StopCount = r.StopCount,
                CustomerCount = r.CustomerCount
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePipelineStatus(int id, CustomerNoteStatus status)
    {
        if (!Enum.IsDefined(status))
            return BadRequest();

        var note = await _context.CustomerNotes.FirstOrDefaultAsync(n => n.Id == id);
        if (note == null)
            return NotFound();

        note.Status = status;
        await _context.SaveChangesAsync();
        return Json(new { ok = true, status = status.ToString() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPipelineSubNote(int id, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return BadRequest(new { ok = false, error = "Enter a sub-note before saving." });

        var note = await _context.CustomerNotes.FirstOrDefaultAsync(n => n.Id == id);
        if (note == null)
            return NotFound();

        _context.CustomerNoteSubNotes.Add(new CustomerNoteSubNote
        {
            CustomerNoteId = id,
            Body = body.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await GetCurrentUserLabelAsync()
        });
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    private async Task<string> GetCurrentUserLabelAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            return user.DisplayName;
        if (!string.IsNullOrWhiteSpace(user?.UserName))
            return user.UserName;
        return User.Identity?.Name ?? "User";
    }

    private static string TruncatePreview(string body)
    {
        var text = string.Join(" ", body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 90 ? text : text[..90] + "…";
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
