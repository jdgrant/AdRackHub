using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class ContactsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerGeocodeService _geocodeService;
    private readonly CustomerNeedsMoreInfoService _needsMoreInfoService;

    public ContactsController(
        ApplicationDbContext context,
        CustomerGeocodeService geocodeService,
        CustomerNeedsMoreInfoService needsMoreInfoService)
    {
        _context = context;
        _geocodeService = geocodeService;
        _needsMoreInfoService = needsMoreInfoService;
    }

    public async Task<IActionResult> LinkedIn(string? search, string? type, bool namedOnly = true, bool missingOnly = false)
    {
        var page = await BuildLinkedInPageAsync(search, type, namedOnly, missingOnly);
        return View(page);
    }

    public async Task<IActionResult> LinkedInExport(string? search, string? type, bool namedOnly = true, bool missingOnly = false)
    {
        var page = await BuildLinkedInPageAsync(search, type, namedOnly, missingOnly);
        var stream = new MemoryStream();
        LinkedInContactsExportService.Write(stream, page.Contacts);
        stream.Position = 0;
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            LinkedInContactsExportService.FileName(DateTime.Today));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLinkedInUrl(
        int id,
        string? linkedInUrl,
        string? search,
        string? type,
        bool namedOnly = true,
        bool missingOnly = false)
    {
        var contact = await _context.Contacts.FindAsync(id);
        if (contact == null)
            return NotFound();

        var normalized = LinkedInSearch.NormalizeProfileUrl(linkedInUrl);
        if (!string.IsNullOrWhiteSpace(linkedInUrl) && normalized == null)
        {
            TempData["Error"] = "Paste the profile URL (linkedin.com/in/...), not the search results page.";
            return RedirectToLinkedIn(search, type, namedOnly, missingOnly, id);
        }

        contact.LinkedInUrl = normalized;
        await _context.SaveChangesAsync();
        TempData["Message"] = normalized == null
            ? $"Cleared LinkedIn profile for {contact.DisplayName}."
            : $"Saved LinkedIn profile for {contact.DisplayName}.";
        return RedirectToLinkedIn(search, type, namedOnly, missingOnly, id);
    }

    public async Task<IActionResult> Create(int customerId)
    {
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
            return NotFound();

        return RedirectToAction("Details", "Customers", new { id = customerId, addContact = 1 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Contact contact)
    {
        // Navigation property is not posted from the modal; only CustomerId is.
        ModelState.Remove(nameof(Contact.Customer));
        if (!string.IsNullOrWhiteSpace(contact.LinkedInUrl)
            && LinkedInSearch.NormalizeProfileUrl(contact.LinkedInUrl) == null)
            ModelState.AddModelError(nameof(Contact.LinkedInUrl), "Use a profile URL (linkedin.com/in/...), not a search page.");

        if (contact.CustomerId <= 0 || !await _context.Customers.AnyAsync(c => c.Id == contact.CustomerId))
            ModelState.AddModelError(nameof(Contact.CustomerId), "Customer is required.");

        if (ModelState.IsValid)
        {
            contact.LinkedInUrl = LinkedInSearch.NormalizeProfileUrl(contact.LinkedInUrl);
            ContactTitleParser.ApplyFromLastName(contact);
            _context.Add(contact);
            await _context.SaveChangesAsync();
            await SetReceiptBothIfSendInvoiceEmailAsync(contact.CustomerId);
            if (CustomerAddressHelper.HasAnyAddressPart(contact.Address, contact.City, contact.State, contact.Zip))
                await _geocodeService.UpdateCoordinatesOnlyAsync(contact.CustomerId);
            await _needsMoreInfoService.RefreshAsync(contact.CustomerId);
            TempData["Message"] = $"Contact {contact.DisplayName} added.";
            return RedirectToAction("Details", "Customers", new { id = contact.CustomerId, tab = "contacts" });
        }

        TempData["ShowContactModal"] = true;
        TempData["ContactName"] = contact.Name;
        TempData["ContactFirstName"] = contact.FirstName;
        TempData["ContactLastName"] = contact.LastName;
        TempData["ContactTitle"] = contact.Title;
        TempData["ContactEmail"] = contact.Email;
        TempData["ContactPhone"] = contact.Phone;
        TempData["ContactCellPhone"] = contact.CellPhone;
        TempData["ContactAddress"] = contact.Address;
        TempData["ContactCity"] = contact.City;
        TempData["ContactState"] = contact.State;
        TempData["ContactZip"] = contact.Zip;
        TempData["ContactWebUrl"] = contact.WebUrl;
        TempData["ContactLinkedInUrl"] = contact.LinkedInUrl;
        TempData["ContactRole"] = contact.Role.ToString();
        TempData["ContactSendInvoice"] = contact.SendInvoice ? "true" : "false";
        TempData["ContactError"] = string.Join(" ", ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m)));
        return RedirectToAction("Details", "Customers", new { id = contact.CustomerId > 0 ? contact.CustomerId : 0 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSendInvoice(int id, bool sendInvoice)
    {
        var contact = await _context.Contacts.FindAsync(id);
        if (contact == null)
            return NotFound();

        contact.SendInvoice = sendInvoice;
        await _context.SaveChangesAsync();
        await SetReceiptBothIfSendInvoiceEmailAsync(contact.CustomerId);
        TempData["Message"] = sendInvoice
            ? $"{contact.DisplayName} will receive invoices."
            : $"{contact.DisplayName} will not receive invoices.";

        return RedirectToAction("Details", "Customers", new { id = contact.CustomerId, tab = "contacts" });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var contact = await _context.Contacts.Include(c => c.Customer).FirstOrDefaultAsync(c => c.Id == id);
        if (contact == null) return NotFound();

        ViewBag.CustomerName = contact.Customer?.CustomerName;
        return View(contact);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Contact contact)
    {
        if (id != contact.Id) return NotFound();

        ModelState.Remove(nameof(Contact.Customer));
        if (!string.IsNullOrWhiteSpace(contact.LinkedInUrl)
            && LinkedInSearch.NormalizeProfileUrl(contact.LinkedInUrl) == null)
            ModelState.AddModelError(nameof(Contact.LinkedInUrl), "Use a profile URL (linkedin.com/in/...), not a search page.");

        if (ModelState.IsValid)
        {
            var existing = await _context.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null)
                return NotFound();

            var addressChanged = CustomerAddressHelper.AddressChanged(existing, contact);

            try
            {
                contact.LinkedInUrl = LinkedInSearch.NormalizeProfileUrl(contact.LinkedInUrl);
                ContactTitleParser.ApplyFromLastName(contact);
                _context.Update(contact);
                await _context.SaveChangesAsync();
                await SetReceiptBothIfSendInvoiceEmailAsync(contact.CustomerId);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Contacts.AnyAsync(c => c.Id == id))
                    return NotFound();
                throw;
            }

            if (addressChanged)
                await _geocodeService.UpdateCoordinatesOnlyAsync(contact.CustomerId);
            await _needsMoreInfoService.RefreshAsync(contact.CustomerId);
            return RedirectToAction("Details", "Customers", new { id = contact.CustomerId });
        }

        var customer = await _context.Customers.FindAsync(contact.CustomerId);
        ViewBag.CustomerName = customer?.CustomerName;
        return View(contact);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var contact = await _context.Contacts.Include(c => c.Customer).FirstOrDefaultAsync(c => c.Id == id);
        if (contact == null) return NotFound();
        return View(contact);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var contact = await _context.Contacts.FindAsync(id);
        if (contact != null)
        {
            var customerId = contact.CustomerId;
            _context.Contacts.Remove(contact);
            await _context.SaveChangesAsync();
            await _needsMoreInfoService.RefreshAsync(customerId);
            return RedirectToAction("Details", "Customers", new { id = customerId });
        }
        return RedirectToAction("Index", "Customers");
    }

    private IActionResult RedirectToLinkedIn(string? search, string? type, bool namedOnly, bool missingOnly, int contactId)
    {
        var url = Url.Action(
            nameof(LinkedIn),
            new { search, type, namedOnly, missingOnly }) + $"#row-{contactId}";
        return Redirect(url!);
    }

    private async Task<LinkedInContactsPageViewModel> BuildLinkedInPageAsync(string? search, string? type, bool namedOnly, bool missingOnly)
    {
        var listType = type is "customer" or "prospect" ? type : "all";
        var query = _context.Contacts
            .AsNoTracking()
            .Include(c => c.Customer)
            .Where(c => c.Customer != null);

        query = listType switch
        {
            "customer" => query.Where(c => c.Customer!.Type == CustomerType.Customer),
            "prospect" => query.Where(c => c.Customer!.Type != CustomerType.Customer),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                c.Name.Contains(term)
                || (c.FirstName != null && c.FirstName.Contains(term))
                || (c.LastName != null && c.LastName.Contains(term))
                || (c.Title != null && c.Title.Contains(term))
                || (c.Email != null && c.Email.Contains(term))
                || c.Customer!.CustomerName.Contains(term));
        }

        var contacts = await query
            .OrderBy(c => c.Customer!.CustomerName)
            .ThenBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ThenBy(c => c.Name)
            .ToListAsync();

        var rows = contacts.Select(c =>
        {
            var company = string.IsNullOrWhiteSpace(c.Customer!.CustomerName) ? c.Name : c.Customer.CustomerName;
            return new LinkedInContactRow
            {
                ContactId = c.Id,
                CustomerId = c.CustomerId,
                CustomerName = c.Customer.CustomerName,
                CustomerType = c.Customer.Type,
                BusinessName = c.Name,
                FirstName = c.FirstName,
                LastName = c.LastName,
                Title = string.IsNullOrWhiteSpace(c.Title) ? null : c.Title,
                PersonName = string.IsNullOrWhiteSpace(c.PersonName) ? null : c.PersonName,
                Role = c.Role,
                Email = c.Email,
                Phone = c.Phone,
                PeopleSearchUrl = LinkedInSearch.PeopleUrl(c.PersonName, company),
                CompanySearchUrl = LinkedInSearch.CompanyUrl(company),
                GoogleSearchUrl = LinkedInSearch.GoogleProfileSearchUrl(c.PersonName, company),
                ProfileUrl = string.IsNullOrWhiteSpace(c.LinkedInUrl) ? null : c.LinkedInUrl
            };
        }).ToList();

        var namedCount = rows.Count(r => r.HasPerson);
        if (namedOnly)
            rows = rows.Where(r => r.HasPerson).ToList();

        var withProfile = rows.Count(r => r.HasProfile);
        var missingProfile = rows.Count(r => !r.HasProfile);
        if (missingOnly)
            rows = rows.Where(r => !r.HasProfile).ToList();

        return new LinkedInContactsPageViewModel
        {
            Search = search?.Trim(),
            ListType = listType,
            NamedOnly = namedOnly,
            MissingOnly = missingOnly,
            Contacts = rows,
            NamedCount = namedCount,
            CompanyOnlyCount = contacts.Count - namedCount,
            CustomerCount = rows.Select(r => r.CustomerId).Distinct().Count(),
            WithProfileCount = withProfile,
            MissingProfileCount = missingProfile
        };
    }

    private async Task SetReceiptBothIfSendInvoiceEmailAsync(int customerId)
    {
        var hasSendInvoiceEmail = await _context.Contacts.AnyAsync(ct =>
            ct.CustomerId == customerId
            && ct.SendInvoice
            && ct.Email != null
            && ct.Email.Trim() != "");
        if (!hasSendInvoiceEmail)
            return;

        var customer = await _context.Customers.FindAsync(customerId);
        if (customer == null || customer.InvoiceReceiptMethod == InvoiceReceiptMethod.Both)
            return;

        customer.InvoiceReceiptMethod = InvoiceReceiptMethod.Both;
        await _context.SaveChangesAsync();
    }
}
