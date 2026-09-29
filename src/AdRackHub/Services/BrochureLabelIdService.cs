using AdRackHub.Data;
using AdRackHub.Models;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Services;

public class BrochureLabelIdService
{
    private readonly ApplicationDbContext _context;

    public BrochureLabelIdService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AssignMissingAsync(CancellationToken cancellationToken = default)
    {
        var customers = await _context.Customers
            .Where(c => c.Type == CustomerType.Customer)
            .ToListAsync(cancellationToken);

        var used = customers
            .Where(c => BrochureCode.Matches(c.BrochureCode))
            .Select(c => c.BrochureCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = customers
            .Where(c => !BrochureCode.Matches(c.BrochureCode))
            .OrderBy(c => c.CustomerName)
            .ThenBy(c => c.Id)
            .ToList();

        if (missing.Count == 0)
            return;

        foreach (var customer in missing)
        {
            var letter = BrochureCode.Letter(customer.CustomerName);
            var next = 1;
            while (used.Contains(BrochureCode.Format(letter, next)))
                next++;

            customer.BrochureCode = BrochureCode.Format(letter, next);
            used.Add(customer.BrochureCode);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignIfMissingAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer == null
            || customer.Type != CustomerType.Customer
            || BrochureCode.Matches(customer.BrochureCode))
            return;

        await AssignMissingAsync(cancellationToken);
    }
}
