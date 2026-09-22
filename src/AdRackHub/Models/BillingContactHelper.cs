namespace AdRackHub.Models;

public static class BillingContactHelper
{
    public static bool IsDesignatedInvoiceContact(Contact contact) =>
        contact.SendInvoice || contact.Role == ContactRole.Billing;

    public static bool HasAppropriateBillingContact(IEnumerable<Contact> contacts) =>
        MissingReason(contacts) == null;

    public static string? MissingReason(IEnumerable<Contact> contacts)
    {
        var list = contacts as IList<Contact> ?? contacts.ToList();
        if (list.Count == 0)
            return "No contacts on file";

        if (!list.Any(IsDesignatedInvoiceContact))
            return "No billing contact marked to receive invoices";

        return null;
    }

    public static string? MissingEmailReason(IEnumerable<Contact> contacts, string? customerEmail = null)
    {
        if (MissingReason(contacts) != null)
            return null;

        if (!string.IsNullOrWhiteSpace(customerEmail))
            return null;

        var designated = contacts.Where(IsDesignatedInvoiceContact);
        if (designated.All(c => string.IsNullOrWhiteSpace(c.Email)))
            return "Billing contact has no email";

        return null;
    }

    public static IReadOnlyList<Contact> InvoiceEmailRecipients(IEnumerable<Contact> contacts) =>
        contacts
            .Where(c => c.SendInvoice && !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => c.Email!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
                .ThenBy(c => c.Name)
                .First())
            .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .ToList();

    public static IReadOnlyList<Contact> ContactsWithEmail(IEnumerable<Contact> contacts) =>
        contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => c.Email!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
                .ThenBy(c => c.Name)
                .First())
            .OrderBy(c => c.Role == ContactRole.Billing ? 0 : c.Role == ContactRole.Primary ? 1 : 2)
            .ThenBy(c => c.Name)
            .ToList();
}
