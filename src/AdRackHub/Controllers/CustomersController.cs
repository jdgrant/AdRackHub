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
    private readonly HighValueProspectProximityService _highValueProximity;
    private readonly CustomerGeocodeService _geocodeService;
    private readonly CustomerNeedsMoreInfoService _needsMoreInfoService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomersController(
        ApplicationDbContext context,
        CustomerRouteMatrixService matrixService,
        IWebHostEnvironment environment,
        WaveSyncService waveSyncService,
        BrochureScanService brochureScanService,
        HighValueProspectProximityService highValueProximity,
        CustomerGeocodeService geocodeService,
        CustomerNeedsMoreInfoService needsMoreInfoService,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _matrixService = matrixService;
        _environment = environment;
        _waveSyncService = waveSyncService;
        _brochureScanService = brochureScanService;
        _highValueProximity = highValueProximity;
        _geocodeService = geocodeService;
        _needsMoreInfoService = needsMoreInfoService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(string? search, CustomerStatus? status)
    {
        var query = _context.Customers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Contacts)
            .Include(c => c.BrochureScans.OrderByDescending(s => s.UploadedAt).Take(3))
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
            ListType = CustomerType.Customer,
            ContractedCustomersByRoute = await GetContractedCustomersByRouteAsync()
        };

        return View(model);
    }

    private async Task<List<ContractedRouteCustomerGroup>> GetContractedCustomersByRouteAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = await _context.CustomerContractRoutes
            .AsNoTracking()
            .Where(cr => cr.Contract.Customer.Type == CustomerType.Customer
                && (!cr.Contract.ContractEndDate.HasValue || cr.Contract.ContractEndDate >= today))
            .Select(cr => new
            {
                cr.RouteId,
                RouteName = cr.Route.RouteName,
                cr.Contract.CustomerId,
                CustomerName = cr.Contract.Customer.CustomerName
            })
            .ToListAsync();

        return rows
            .GroupBy(r => new { r.RouteId, r.RouteName })
            .OrderBy(g => g.Key.RouteName)
            .Select(g => new ContractedRouteCustomerGroup
            {
                RouteId = g.Key.RouteId,
                RouteName = g.Key.RouteName,
                Customers = g
                    .GroupBy(x => new { x.CustomerId, x.CustomerName })
                    .OrderBy(x => x.Key.CustomerName)
                    .Select(x => new ContractedCustomerOption
                    {
                        CustomerId = x.Key.CustomerId,
                        CustomerName = x.Key.CustomerName
                    })
                    .ToList()
            })
            .Where(g => g.Customers.Count > 0)
            .ToList();
    }

    private static CustomerIndexSummary BuildSummary(IReadOnlyList<Customer> customers)
    {
        var withContracts = customers.Where(c => c.Contracts.Count > 0).ToList();
        var totalRevenue = withContracts.Sum(CustomerRevenue.GetAnnual);
        return new CustomerIndexSummary
        {
            ClientCount = customers.Count,
            ClientsWithContracts = withContracts.Count,
            RouteCount = customers.Sum(c =>
                c.CustomerRoutes.Count(cr => cr.Status == CustomerRouteStatus.Active)),
            TotalRevenue = totalRevenue
        };
    }

    public async Task<IActionResult> Details(int? id, string? tab = null, string? search = null, CustomerStatus? status = null, bool highValue = false, bool needsMoreInfo = false)
    {
        if (id == null) return NotFound();

        var customer = await _context.Customers
            .AsSplitQuery()
            .Include(c => c.Contacts.OrderBy(ct => ct.Name))
            .Include(c => c.CustomerRoutes)
                .ThenInclude(cr => cr.Route)
                    .ThenInclude(r => r.Stops)
            .Include(c => c.CustomerRoutes)
                .ThenInclude(cr => cr.CustomerRouteStops)
                    .ThenInclude(crs => crs.Stop)
            .Include(c => c.Contracts)
                .ThenInclude(b => b.ContractRoutes)
                    .ThenInclude(cbr => cbr.Route)
                        .ThenInclude(r => r.Stops)
            .Include(c => c.BrochureScans.OrderByDescending(s => s.UploadedAt))
            .Include(c => c.BrochureInventories.OrderByDescending(i => i.InventoryDate).ThenByDescending(i => i.CreatedAt))
            .Include(c => c.Notes.OrderByDescending(n => n.CreatedAt))
                .ThenInclude(n => n.SubNotes.OrderBy(s => s.CreatedAt))
            .Include(c => c.AccountManager)
            .Include(c => c.ExpandedProspectRoute)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null) return NotFound();
        ViewBag.ActiveTab = NormalizeDetailTab(tab, CustomerTypeLabels.IsProspectLike(customer.Type));
        ViewBag.AnnualRevenue = CustomerRevenue.GetAnnual(customer);
        if (CustomerTypeLabels.IsProspectLike(customer.Type))
            ViewBag.HighValueProximity = await _highValueProximity.GetNearestHitAsync(customer);
        await PopulateListNavigationAsync(customer, search, status, highValue, needsMoreInfo);
        if (customer.Type == CustomerType.Customer)
            ViewBag.ContractedCustomersByRoute = await GetContractedCustomersByRouteAsync();
        return View(customer);
    }

    private async Task PopulateListNavigationAsync(Customer customer, string? search, CustomerStatus? status, bool highValue, bool needsMoreInfo)
    {
        var saved = CustomerListNavigation.Load(HttpContext.Session);
        var hasQueryFilters = search != null || status.HasValue || highValue || needsMoreInfo
            || Request.Query.ContainsKey("search")
            || Request.Query.ContainsKey("status")
            || Request.Query.ContainsKey("highValue")
            || Request.Query.ContainsKey("needsMoreInfo");

        string? effectiveSearch;
        CustomerStatus? effectiveStatus;
        var effectiveHighValue = false;
        var effectiveNeedsMoreInfo = false;
        List<int> ids;

        if (hasQueryFilters)
        {
            effectiveSearch = search;
            effectiveStatus = status;
            effectiveHighValue = highValue;
            effectiveNeedsMoreInfo = needsMoreInfo;
            ids = await CustomerListNavigation.GetOrderedIdsAsync(
                _context, customer.Type, effectiveSearch, effectiveStatus, effectiveHighValue, effectiveNeedsMoreInfo, saved?.RouteId);
        }
        else if (saved != null && saved.Type == customer.Type && saved.Ids.Count > 0)
        {
            effectiveSearch = saved.Search;
            effectiveStatus = saved.Status;
            effectiveHighValue = saved.HighValue;
            effectiveNeedsMoreInfo = saved.NeedsMoreInfo;
            ids = saved.Ids;
            if (!ids.Contains(customer.Id))
            {
                ids = await CustomerListNavigation.GetOrderedIdsAsync(
                    _context, customer.Type, effectiveSearch, effectiveStatus, effectiveHighValue, effectiveNeedsMoreInfo, saved.RouteId);
            }
        }
        else
        {
            effectiveSearch = null;
            effectiveStatus = null;
            effectiveHighValue = false;
            effectiveNeedsMoreInfo = false;
            ids = await CustomerListNavigation.GetOrderedIdsAsync(_context, customer.Type, null, null);
        }

        CustomerListNavigation.Store(HttpContext.Session, new CustomerListNavState
        {
            Type = customer.Type,
            Search = effectiveSearch,
            Status = effectiveStatus,
            HighValue = effectiveHighValue,
            NeedsMoreInfo = effectiveNeedsMoreInfo,
            RouteId = saved?.RouteId,
            Ids = ids
        });

        var (previousId, nextId, position, total) = CustomerListNavigation.ResolveNeighbors(ids, customer.Id);
        ViewBag.PreviousCustomerId = previousId;
        ViewBag.NextCustomerId = nextId;
        ViewBag.NavPosition = position;
        ViewBag.NavTotal = total;
        ViewBag.ListSearch = effectiveSearch;
        ViewBag.ListStatus = effectiveStatus;
        ViewBag.ListHighValue = effectiveHighValue;
        ViewBag.ListNeedsMoreInfo = effectiveNeedsMoreInfo;
    }

    private static string NormalizeDetailTab(string? tab, bool isProspect)
    {
        var value = (tab ?? "activity").Trim().ToLowerInvariant();
        return value switch
        {
            "contacts" => "contacts",
            "brochures" => "brochures",
            "map" => "map",
            "routes" when !isProspect => "routes",
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
            TempData["Message"] = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "Brochure PDF converted to PNG and uploaded."
                : "Brochure scan uploaded.";
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
    public async Task<IActionResult> SaveWarehouseLocation(int customerId, string? rack, string? bin)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null)
            return NotFound();

        customer.WarehouseRack = WarehouseLocation.NullIfEmpty(rack);
        customer.WarehouseBin = WarehouseLocation.NullIfEmpty(bin);
        await _context.SaveChangesAsync();
        TempData["Message"] = customer.WarehouseLocationLabel == null
            ? "Warehouse location cleared."
            : $"Warehouse location set to {customer.WarehouseLocationLabel}.";
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBrochureInventory(
        int customerId,
        DateOnly? inventoryDate,
        int? quantity,
        string? rack,
        string? bin,
        string? notes)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null)
            return NotFound();

        if (!quantity.HasValue || quantity.Value < 0)
        {
            TempData["Error"] = "Enter a brochure count of zero or greater.";
            return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
        }

        var locationRack = WarehouseLocation.NullIfEmpty(rack) ?? customer.WarehouseRack;
        var locationBin = WarehouseLocation.NullIfEmpty(bin) ?? customer.WarehouseBin;

        _context.CustomerBrochureInventories.Add(new CustomerBrochureInventory
        {
            CustomerId = customerId,
            Quantity = quantity.Value,
            InventoryDate = inventoryDate ?? DateOnly.FromDateTime(DateTime.Today),
            Rack = locationRack,
            Bin = locationBin,
            Notes = WarehouseLocation.NullIfEmpty(notes),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await GetCurrentUserLabelAsync()
        });

        if (WarehouseLocation.NullIfEmpty(rack) != null || WarehouseLocation.NullIfEmpty(bin) != null)
        {
            customer.WarehouseRack = locationRack;
            customer.WarehouseBin = locationBin;
        }

        await _context.SaveChangesAsync();
        TempData["Message"] = "Inventory recorded.";
        return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBrochureInventory(
        int id,
        int customerId,
        DateOnly? inventoryDate,
        int? quantity,
        string? rack,
        string? bin,
        string? notes)
    {
        var inventory = await _context.CustomerBrochureInventories
            .FirstOrDefaultAsync(i => i.Id == id && i.CustomerId == customerId);
        if (inventory == null)
            return NotFound();

        if (!quantity.HasValue || quantity.Value < 0)
        {
            TempData["Error"] = "Enter a brochure count of zero or greater.";
            return RedirectToAction(nameof(Details), new { id = customerId, tab = "brochures" });
        }

        inventory.Quantity = quantity.Value;
        inventory.InventoryDate = inventoryDate ?? inventory.InventoryDate;
        inventory.Rack = WarehouseLocation.NullIfEmpty(rack);
        inventory.Bin = WarehouseLocation.NullIfEmpty(bin);
        inventory.Notes = WarehouseLocation.NullIfEmpty(notes);

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer != null && (inventory.Rack != null || inventory.Bin != null))
        {
            customer.WarehouseRack = inventory.Rack;
            customer.WarehouseBin = inventory.Bin;
        }

        await _context.SaveChangesAsync();
        TempData["Message"] = "Inventory record updated.";
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
    public async Task<IActionResult> AddNote(int customerId, CustomerNoteKind kind, CustomerNoteStatus status, DateOnly? dueDate, string? body)
    {
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "Enter a note before saving.";
            return RedirectToAction(nameof(Details), new { id = customerId, tab = "activity" });
        }

        var resolvedKind = Enum.IsDefined(kind) ? kind : CustomerNoteKind.Note;
        _context.CustomerNotes.Add(new CustomerNote
        {
            CustomerId = customerId,
            Kind = resolvedKind,
            Status = Enum.IsDefined(status) ? status : CustomerNoteStatus.New,
            DueDate = ResolveActivityDueDate(resolvedKind, dueDate),
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
    public async Task<IActionResult> EditNote(int id, int customerId, CustomerNoteKind kind, CustomerNoteStatus status, DateOnly? dueDate, string? body)
    {
        var note = await _context.CustomerNotes.FirstOrDefaultAsync(n => n.Id == id && n.CustomerId == customerId);
        if (note == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "Enter a note before saving.";
            return RedirectToAction(nameof(Details), new { id = customerId, tab = "activity" });
        }

        note.Kind = Enum.IsDefined(kind) ? kind : note.Kind;
        note.Status = Enum.IsDefined(status) ? status : note.Status;
        note.DueDate = ResolveActivityDueDate(note.Kind, dueDate);
        note.Body = body.Trim();
        await _context.SaveChangesAsync();
        TempData["Message"] = "Note updated.";
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
    public async Task<IActionResult> AddSubNote(int noteId, int customerId, string? body)
    {
        var note = await _context.CustomerNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.CustomerId == customerId);
        if (note == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "Enter a sub-note before saving.";
            return Redirect($"{Url.Action(nameof(Details), new { id = customerId, tab = "activity" })}#note-{noteId}");
        }

        _context.CustomerNoteSubNotes.Add(new CustomerNoteSubNote
        {
            CustomerNoteId = noteId,
            Body = body.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = await GetCurrentUserLabelAsync()
        });
        await _context.SaveChangesAsync();
        TempData["Message"] = "Sub-note added.";
        return Redirect($"{Url.Action(nameof(Details), new { id = customerId, tab = "activity" })}#note-{noteId}");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubNote(int id, int noteId, int customerId)
    {
        var subNote = await _context.CustomerNoteSubNotes
            .FirstOrDefaultAsync(s => s.Id == id && s.CustomerNoteId == noteId && s.CustomerNote.CustomerId == customerId);
        if (subNote != null)
        {
            _context.CustomerNoteSubNotes.Remove(subNote);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Sub-note deleted.";
        }
        return Redirect($"{Url.Action(nameof(Details), new { id = customerId, tab = "activity" })}#note-{noteId}");
    }

    private static DateOnly? ResolveActivityDueDate(CustomerNoteKind kind, DateOnly? dueDate)
    {
        if (dueDate.HasValue)
            return dueDate;
        if (kind is CustomerNoteKind.Task or CustomerNoteKind.BrochuresNeeded)
            return DateOnly.FromDateTime(DateTime.Today);
        return null;
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

    public async Task<IActionResult> Create(CustomerType type = CustomerType.Customer, int? routeId = null)
    {
        if (!Enum.IsDefined(type))
            type = CustomerType.Customer;

        var currentUser = await _userManager.GetUserAsync(User);
        await PopulateAccountManagersAsync(currentUser?.Id);
        await PopulateExpandedProspectRoutesAsync(type == CustomerType.ExpandedProspect ? routeId : null);
        return View(new Customer
        {
            Status = CustomerStatus.Active,
            Type = type,
            AccountManagerId = currentUser?.Id,
            InvoiceReceiptMethod = InvoiceReceiptMethod.Mail,
            ExpandedProspectRouteId = type == CustomerType.ExpandedProspect ? routeId : null
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
            customer.NeedsMoreInfo = CustomerContactCompleteness.NeedsMoreInfo(customer);
            _context.Add(customer);
            await _context.SaveChangesAsync();
            if (CustomerAddressHelper.HasGeocodableAddress(customer))
                await _geocodeService.UpdateCoordinatesOnlyAsync(customer.Id);
            await _needsMoreInfoService.RefreshAsync(customer.Id);

            if (customer.Type == CustomerType.Customer)
                await _waveSyncService.PushCustomerToWaveAsync(customer.Id);
            return RedirectToAction(nameof(Details), new { id = customer.Id });
        }
        await PopulateAccountManagersAsync(customer.AccountManagerId);
        await PopulateExpandedProspectRoutesAsync(customer.ExpandedProspectRouteId);
        return View(customer);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();
        await PopulateAccountManagersAsync(customer.AccountManagerId);
        await PopulateExpandedProspectRoutesAsync(customer.ExpandedProspectRouteId);
        return View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer customer)
    {
        if (id != customer.Id) return NotFound();

        if (ModelState.IsValid)
        {
            var existing = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null)
                return NotFound();

            var addressChanged = CustomerAddressHelper.AddressChanged(existing, customer);

            if (addressChanged)
            {
                customer.IsHighValueProspect = existing.IsHighValueProspect;
                customer.IsAtRisk = existing.IsAtRisk;
            }

            if (customer.Type == CustomerType.Customer)
                customer.IsHighValueProspect = false;
            else
                customer.IsAtRisk = false;

            try
            {
                _context.Update(customer);
                await _context.Entry(customer).Collection(c => c.Contacts).LoadAsync();
                customer.NeedsMoreInfo = CustomerContactCompleteness.NeedsMoreInfo(customer);
                await _context.SaveChangesAsync();
                if (addressChanged)
                    await _geocodeService.UpdateCoordinatesOnlyAsync(id);
                await _needsMoreInfoService.RefreshAsync(id);
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
        await PopulateExpandedProspectRoutesAsync(customer.ExpandedProspectRouteId);
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

    private async Task PopulateExpandedProspectRoutesAsync(int? selectedId = null)
    {
        var routes = await _context.Routes
            .AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active
                || (selectedId.HasValue && r.Id == selectedId.Value))
            .OrderBy(r => r.RouteName)
            .Select(r => new { r.Id, r.RouteName })
            .ToListAsync();

        ViewBag.ExpandedProspectRoutes = new SelectList(routes, "Id", "RouteName", selectedId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleType(int id, CustomerType? toType = null, string? returnTo = null)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();

        if (toType.HasValue && Enum.IsDefined(toType.Value) && toType.Value != customer.Type)
            customer.Type = toType.Value;
        else if (!toType.HasValue)
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

        await _needsMoreInfoService.RefreshAsync(customer.Id);

        if (customer.Type == CustomerType.Customer)
            await _waveSyncService.PushCustomerToWaveAsync(customer.Id);

        TempData["Message"] = $"{customer.CustomerName} is now a {CustomerTypeLabels.Singular(customer.Type).ToLowerInvariant()}.";

        if (string.Equals(returnTo, "list", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", CustomerTypeLabels.Controller(customer.Type));

        return RedirectToAction(nameof(Details), new { id = customer.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleHighValue(int id, string? returnTo = null)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();
        if (!CustomerTypeLabels.IsProspectLike(customer.Type))
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
            return RedirectToAction("Index", CustomerTypeLabels.Controller(customer.Type));

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
        ViewBag.InvoiceCount = await _context.BillingRunInvoices.CountAsync(i => i.CustomerId == id.Value);
        return View(customer);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null)
            return RedirectToAction(nameof(Index));

        var listController = CustomerTypeLabels.Controller(customer.Type);
        var typeLabel = CustomerTypeLabels.Singular(customer.Type).ToLowerInvariant();
        var name = customer.CustomerName;

        var invoiceCount = await _context.BillingRunInvoices.CountAsync(i => i.CustomerId == id);
        if (invoiceCount > 0)
        {
            TempData["Error"] = $"{name} has {invoiceCount} invoice{(invoiceCount == 1 ? "" : "s")} and cannot be deleted.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        _context.Customers.Remove(customer);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = $"{name} could not be deleted because related records still depend on it.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        TryDeleteCustomerUploadFolder(id);
        TempData["Message"] = $"Deleted {typeLabel} {name}.";
        return RedirectToAction("Index", listController);
    }

    private void TryDeleteCustomerUploadFolder(int customerId)
    {
        foreach (var root in new[]
                 {
                     BrochureScanService.GetUploadRoot(_environment),
                     BrochureScanService.GetLegacyUploadRoot(_environment)
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var dir = Path.Combine(root, customerId.ToString());
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of leftover scan files.
            }
        }
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
