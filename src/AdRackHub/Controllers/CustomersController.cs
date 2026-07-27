using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerRouteMatrixService _matrixService;
    private readonly IWebHostEnvironment _environment;
    private readonly WaveSyncService _waveSyncService;
    private readonly BrochureScanService _brochureScanService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomersController(
        ApplicationDbContext context,
        CustomerRouteMatrixService matrixService,
        IWebHostEnvironment environment,
        WaveSyncService waveSyncService,
        BrochureScanService brochureScanService,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _matrixService = matrixService;
        _environment = environment;
        _waveSyncService = waveSyncService;
        _brochureScanService = brochureScanService;
        _userManager = userManager;
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
            .Where(c => c.Type == CustomerType.Customer);

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

        var customers = await query.OrderBy(c => c.CustomerName).ToListAsync();
        CustomerListNavigation.Store(HttpContext.Session, new CustomerListNavState
        {
            Type = CustomerType.Customer,
            Search = search,
            Status = status,
            Ids = customers.Select(c => c.Id).ToList()
        });

        var model = new CustomerIndexViewModel
        {
            Customers = customers,
            Summary = BuildSummary(customers),
            ListType = CustomerType.Customer
        };

        return View(model);
    }

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
            return customer.Contracts                .SelectMany(b => b.ContractRoutes)
                .Sum(AnnualBillingHelper.GetBillingAmount);
        }

        return customer.CustomerRoutes
            .Where(cr => cr.Status == CustomerRouteStatus.Active)
            .Sum(cr => cr.RatePerMonth > 0 ? cr.RatePerMonth : cr.Route.Price);
    }

    public async Task<IActionResult> Details(int? id, string? tab = null, string? search = null, CustomerStatus? status = null)
    {
        if (id == null) return NotFound();

        var customer = await _context.Customers
            .Include(c => c.Contacts.OrderBy(ct => ct.Name))
            .Include(c => c.CustomerRoutes)
                .ThenInclude(cr => cr.Route)
            .Include(c => c.CustomerRoutes)
                .ThenInclude(cr => cr.CustomerRouteStops)
                    .ThenInclude(crs => crs.Stop)
            .Include(c => c.Contracts)
                .ThenInclude(b => b.ContractRoutes)
                    .ThenInclude(cbr => cbr.Route)
            .Include(c => c.BrochureScans.OrderByDescending(s => s.UploadedAt))
            .Include(c => c.BrochureInventories.OrderByDescending(i => i.InventoryDate).ThenByDescending(i => i.CreatedAt))
            .Include(c => c.Notes.OrderByDescending(n => n.CreatedAt))
            .Include(c => c.Tasks)
            .Include(c => c.AccountManager)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null) return NotFound();
        ViewBag.ActiveTab = NormalizeDetailTab(tab, customer.Type == CustomerType.Prospect);
        ViewBag.AnnualRevenue = CustomerRevenue.GetAnnual(customer);
        await PopulateListNavigationAsync(customer, search, status);
        return View(customer);
    }

    private async Task PopulateListNavigationAsync(Customer customer, string? search, CustomerStatus? status)
    {
        var saved = CustomerListNavigation.Load(HttpContext.Session);
        var hasQueryFilters = search != null || status.HasValue || Request.Query.ContainsKey("search") || Request.Query.ContainsKey("status");

        string? effectiveSearch;
        CustomerStatus? effectiveStatus;
        List<int> ids;

        if (hasQueryFilters)
        {
            effectiveSearch = search;
            effectiveStatus = status;
            ids = await CustomerListNavigation.GetOrderedIdsAsync(_context, customer.Type, effectiveSearch, effectiveStatus);
        }
        else if (saved != null && saved.Type == customer.Type && saved.Ids.Count > 0)
        {
            effectiveSearch = saved.Search;
            effectiveStatus = saved.Status;
            ids = saved.Ids;
            if (!ids.Contains(customer.Id))
            {
                ids = await CustomerListNavigation.GetOrderedIdsAsync(_context, customer.Type, effectiveSearch, effectiveStatus);
            }
        }
        else
        {
            effectiveSearch = null;
            effectiveStatus = null;
            ids = await CustomerListNavigation.GetOrderedIdsAsync(_context, customer.Type, null, null);
        }

        CustomerListNavigation.Store(HttpContext.Session, new CustomerListNavState
        {
            Type = customer.Type,
            Search = effectiveSearch,
            Status = effectiveStatus,
            Ids = ids
        });

        var (previousId, nextId, position, total) = CustomerListNavigation.ResolveNeighbors(ids, customer.Id);
        ViewBag.PreviousCustomerId = previousId;
        ViewBag.NextCustomerId = nextId;
        ViewBag.NavPosition = position;
        ViewBag.NavTotal = total;
        ViewBag.ListSearch = effectiveSearch;
        ViewBag.ListStatus = effectiveStatus;
    }

    private static string NormalizeDetailTab(string? tab, bool isProspect)
    {
        var value = (tab ?? "activity").Trim().ToLowerInvariant();
        return value switch
        {
            "tasks" => "tasks",
            "contacts" => "contacts",
            "brochures" => "brochures",
            "contracts" when !isProspect => "contracts",
            _ => "activity"
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(BrochureScanService.MaxFileSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = BrochureScanService.MaxFileSizeBytes)]
    public async Task<IActionResult> UploadBrochureScan(int id, IFormFile? file, string? notes, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Choose a file to upload.";
            return RedirectToAction(nameof(Details), new { id, tab = "brochures" });
        }

        try
        {
            await _brochureScanService.SaveAsync(id, file, notes, cancellationToken);
            TempData["Message"] = "Brochure scan uploaded.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id, tab = "brochures" });
    }

    public async Task<IActionResult> ViewBrochureScan(int id, bool download = false, CancellationToken cancellationToken = default)
    {
        var scan = await _brochureScanService.GetAsync(id, cancellationToken);
        if (scan == null) return NotFound();

        var filePath = _brochureScanService.ResolveFilePath(scan, _environment);
        if (filePath == null) return NotFound();

        if (download)
            return PhysicalFile(filePath, scan.ContentType, scan.OriginalFileName);

        // Serve inline so thumbnails and the preview modal can display without downloading.
        return PhysicalFile(filePath, scan.ContentType, enableRangeProcessing: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBrochureScan(int id, int customerId, CancellationToken cancellationToken)
    {
        try
        {
            await _brochureScanService.DeleteAsync(id, cancellationToken);
            TempData["Message"] = "Brochure scan deleted.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBrochureInventory(int customerId, DateOnly? inventoryDate, int? quantity, string? notes)
    {
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
            return NotFound();

        if (!quantity.HasValue || quantity.Value < 0)
        {
            TempData["Error"] = "Enter a brochure count of zero or greater.";
            return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
        }

        _context.CustomerBrochureInventories.Add(new CustomerBrochureInventory
        {
            CustomerId = customerId,
            Quantity = quantity.Value,
            InventoryDate = inventoryDate ?? DateOnly.FromDateTime(DateTime.Today),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await GetCurrentUserLabelAsync()
        });
        await _context.SaveChangesAsync();
        TempData["Message"] = "Inventory recorded.";
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBrochureInventory(int id, int customerId)
    {
        var inventory = await _context.CustomerBrochureInventories
            .FirstOrDefaultAsync(i => i.Id == id && i.CustomerId == customerId);
        if (inventory != null)
        {
            _context.CustomerBrochureInventories.Remove(inventory);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Inventory record deleted.";
        }
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(int customerId, CustomerNoteKind kind, string? body)
    {
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "Enter a note before saving.";
            return RedirectToAction(nameof(Details), new { id = customerId, tab = "activity" });
        }

        _context.CustomerNotes.Add(new CustomerNote
        {
            CustomerId = customerId,
            Kind = Enum.IsDefined(kind) ? kind : CustomerNoteKind.Note,
            Body = body.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await GetCurrentUserLabelAsync()
        });
        await _context.SaveChangesAsync();
        TempData["Message"] = "Note added.";
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "activity" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNote(int id, int customerId)
    {
        var note = await _context.CustomerNotes.FirstOrDefaultAsync(n => n.Id == id && n.CustomerId == customerId);
        if (note != null)
        {
            _context.CustomerNotes.Remove(note);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Note deleted.";
        }
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "activity" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTask(int customerId, string? title, string? description, DateOnly? dueDate)
    {
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Enter a task title before saving.";
            return RedirectToAction(nameof(Details), new { id = customerId, tab = "tasks" });
        }

        _context.CustomerTasks.Add(new CustomerTask
        {
            CustomerId = customerId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DueDate = dueDate,
            Status = CustomerTaskStatus.Open,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await GetCurrentUserLabelAsync()
        });
        await _context.SaveChangesAsync();
        TempData["Message"] = "Task added.";
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "tasks" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteTask(int id, int customerId)
    {
        var task = await _context.CustomerTasks.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId);
        if (task != null)
        {
            task.Status = CustomerTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["Message"] = "Task completed.";
        }
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "tasks" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReopenTask(int id, int customerId)
    {
        var task = await _context.CustomerTasks.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId);
        if (task != null)
        {
            task.Status = CustomerTaskStatus.Open;
            task.CompletedAt = null;
            await _context.SaveChangesAsync();
            TempData["Message"] = "Task reopened.";
        }
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "tasks" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTask(int id, int customerId)
    {
        var task = await _context.CustomerTasks.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId);
        if (task != null)
        {
            _context.CustomerTasks.Remove(task);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Task deleted.";
        }
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "tasks" });
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

    public async Task<IActionResult> Create(CustomerType type = CustomerType.Customer)
    {
        if (!Enum.IsDefined(type))
            type = CustomerType.Customer;

        var currentUser = await _userManager.GetUserAsync(User);
        await PopulateAccountManagersAsync(currentUser?.Id);
        return View(new Customer
        {
            Status = CustomerStatus.Active,
            Type = type,
            AccountManagerId = currentUser?.Id
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer customer)
    {
        if (!Enum.IsDefined(customer.Type))
            customer.Type = CustomerType.Customer;

        if (string.IsNullOrWhiteSpace(customer.AccountManagerId))
            customer.AccountManagerId = (await _userManager.GetUserAsync(User))?.Id;

        if (ModelState.IsValid)
        {
            _context.Add(customer);
            await _context.SaveChangesAsync();
            if (customer.Type == CustomerType.Customer)
                await _waveSyncService.PushCustomerToWaveAsync(customer.Id);
            return RedirectToAction(nameof(Details), new { id = customer.Id });
        }
        await PopulateAccountManagersAsync(customer.AccountManagerId);
        return View(customer);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();
        await PopulateAccountManagersAsync(customer.AccountManagerId);
        return View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer customer)
    {
        if (id != customer.Id) return NotFound();

        if (ModelState.IsValid)
        {
            if (customer.Type == CustomerType.Customer)
                customer.IsHighValueProspect = false;
            else
                customer.IsAtRisk = false;

            try
            {
                _context.Update(customer);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Customers.AnyAsync(c => c.Id == id))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Details), new { id = customer.Id });
        }
        await PopulateAccountManagersAsync(customer.AccountManagerId);
        return View(customer);
    }

    private async Task PopulateAccountManagersAsync(string? selectedId = null)
    {
        var users = await _userManager.Users
            .OrderBy(u => u.DisplayName ?? u.Email)
            .ToListAsync();

        ViewBag.AccountManagers = new SelectList(
            users.Select(u => new
            {
                u.Id,
                Name = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : (u.Email ?? u.UserName ?? u.Id)
            }),
            "Id",
            "Name",
            selectedId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleType(int id, string? returnTo = null)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();

        customer.Type = customer.Type == CustomerType.Prospect
            ? CustomerType.Customer
            : CustomerType.Prospect;

        if (customer.Type == CustomerType.Customer)
        {
            customer.IsHighValueProspect = false;
        }
        else
        {
            customer.IsAtRisk = false;
        }

        await _context.SaveChangesAsync();

        if (customer.Type == CustomerType.Customer)
            await _waveSyncService.PushCustomerToWaveAsync(customer.Id);

        TempData["Message"] = customer.Type == CustomerType.Customer
            ? $"{customer.CustomerName} is now a customer."
            : $"{customer.CustomerName} is now a prospect.";

        if (string.Equals(returnTo, "list", StringComparison.OrdinalIgnoreCase))
        {
            // After converting, the record leaves the current list — send them to the new list.
            var listController = customer.Type == CustomerType.Prospect ? "Prospects" : "Customers";
            return RedirectToAction("Index", listController);
        }

        return RedirectToAction(nameof(Details), new { id = customer.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleHighValue(int id, string? returnTo = null)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();
        if (customer.Type != CustomerType.Prospect)
        {
            TempData["Error"] = "Only prospects can be marked high value.";
            return RedirectToAction(nameof(Details), new { id });
        }

        customer.IsHighValueProspect = !customer.IsHighValueProspect;
        await _context.SaveChangesAsync();
        TempData["Message"] = customer.IsHighValueProspect
            ? $"{customer.CustomerName} marked as high value."
            : $"{customer.CustomerName} is no longer high value.";

        if (string.Equals(returnTo, "list", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Prospects");

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAtRisk(int id, string? returnTo = null)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();
        if (customer.Type != CustomerType.Customer)
        {
            TempData["Error"] = "Only customers can be marked at risk.";
            return RedirectToAction(nameof(Details), new { id });
        }

        customer.IsAtRisk = !customer.IsAtRisk;
        await _context.SaveChangesAsync();
        TempData["Message"] = customer.IsAtRisk
            ? $"{customer.CustomerName} marked as at risk."
            : $"{customer.CustomerName} is no longer at risk.";

        if (string.Equals(returnTo, "list", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Customers");

        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null) return NotFound();
        return View(customer);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var customer = await _context.Customers.FindAsync(id);
        var listController = customer?.Type == CustomerType.Prospect ? "Prospects" : "Customers";
        if (customer != null)
        {
            _context.Customers.Remove(customer);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction("Index", listController);
    }

    public IActionResult ImportMatrix()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportMatrix(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("", "Choose an Excel file to import.");
            return View();
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _matrixService.ImportAsync(stream, cancellationToken);
            TempData["Message"] =
                $"Imported matrix: {result.CustomersAdded} customers added, {result.CustomersUpdated} updated, " +
                $"{result.AssignmentsAdded} assignments added, {result.AssignmentsUpdated} updated, " +
                $"{result.ContractsConfigured} bills configured.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View();
        }
    }

    public async Task<IActionResult> DownloadMatrixTemplate(CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await _matrixService.ExportTemplateAsync(stream, _environment.ContentRootPath, cancellationToken);
        stream.Position = 0;
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            CustomerRouteMatrixService.TemplateFileName);
    }

    public async Task<IActionResult> DownloadMatrixSample(CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await _matrixService.ExportSampleAsync(stream, _environment.ContentRootPath, cancellationToken);
        stream.Position = 0;
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            CustomerRouteMatrixService.SampleFileName);
    }
}
