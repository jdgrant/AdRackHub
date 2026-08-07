using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class ContactsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ContactsController(ApplicationDbContext context)
    {
        _context = context;
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

        if (contact.CustomerId <= 0 || !await _context.Customers.AnyAsync(c => c.Id == contact.CustomerId))
            ModelState.AddModelError(nameof(Contact.CustomerId), "Customer is required.");

        if (ModelState.IsValid)
        {
            _context.Add(contact);
            await _context.SaveChangesAsync();
            TempData["Message"] = $"Contact {contact.DisplayName} added.";
            return RedirectToAction("Details", "Customers", new { id = contact.CustomerId, tab = "contacts" });
        }

        TempData["ShowContactModal"] = true;
        TempData["ContactName"] = contact.Name;
        TempData["ContactFirstName"] = contact.FirstName;
        TempData["ContactLastName"] = contact.LastName;
        TempData["ContactEmail"] = contact.Email;
        TempData["ContactPhone"] = contact.Phone;
        TempData["ContactCellPhone"] = contact.CellPhone;
        TempData["ContactAddress"] = contact.Address;
        TempData["ContactCity"] = contact.City;
        TempData["ContactState"] = contact.State;
        TempData["ContactZip"] = contact.Zip;
        TempData["ContactWebUrl"] = contact.WebUrl;
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

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(contact);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Contacts.AnyAsync(c => c.Id == id))
                    return NotFound();
                throw;
            }
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
            return RedirectToAction("Details", "Customers", new { id = customerId });
        }
        return RedirectToAction("Index", "Customers");
    }
}
