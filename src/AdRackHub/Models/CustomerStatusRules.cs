namespace AdRackHub.Models;

public static class CustomerStatusRules
{
    public static CustomerStatus FromContracts(
        CustomerStatus current,
        CustomerType type,
        IEnumerable<(bool IsProposal, DateOnly? EndDate)> contracts,
        DateOnly? asOf = null)
    {
        if (type != CustomerType.Customer || current == CustomerStatus.Inactive)
            return current;

        asOf ??= DateOnly.FromDateTime(DateTime.Today);
        var items = contracts as ICollection<(bool IsProposal, DateOnly? EndDate)> ?? contracts.ToList();
        var hasSigned = items.Any(c => !c.IsProposal && (!c.EndDate.HasValue || c.EndDate.Value >= asOf.Value));
        var hasProposal = items.Any(c => c.IsProposal);

        if (!hasSigned && hasProposal)
            return CustomerStatus.Proposed;

        if (hasSigned && current == CustomerStatus.Proposed)
            return CustomerStatus.Active;

        return current;
    }
}
