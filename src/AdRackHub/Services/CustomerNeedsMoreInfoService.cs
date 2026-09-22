using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class CustomerNeedsMoreInfoService
{
    private readonly ApplicationDbContext _context;

    public CustomerNeedsMoreInfoService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task RefreshAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer == null)
            return;

        var next = CustomerContactCompleteness.NeedsMoreInfo(customer);
        if (customer.NeedsMoreInfo == next)
            return;

        customer.NeedsMoreInfo = next;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var customers = await _context.Customers
            .Include(c => c.Contacts)
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var customer in customers)
        {
            var next = CustomerContactCompleteness.NeedsMoreInfo(customer);
            if (customer.NeedsMoreInfo == next)
                continue;

            customer.NeedsMoreInfo = next;
            changed++;
        }

        if (changed > 0)
            await _context.SaveChangesAsync(cancellationToken);

        return changed;
    }
}
