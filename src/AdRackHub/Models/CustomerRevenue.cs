namespace AdRackHub.Models;

public static class CustomerRevenue
{
    /// <summary>Annual revenue from contracts (preferred) or active route assignments.</summary>
    public static decimal GetAnnual(Customer customer)
    {
        if (customer.Contracts.Count > 0)
        {
            return customer.Contracts.Sum(contract =>
                contract.ContractRoutes.Sum(cr =>
                    AnnualBillingHelper.ToAnnualPrice(
                        AnnualBillingHelper.GetBillingAmount(cr),
                        contract.Term)));
        }

        return customer.CustomerRoutes
            .Where(cr => cr.Status == CustomerRouteStatus.Active)
            .Sum(cr =>
            {
                if (cr.RatePerMonth > 0)
                    return Math.Round(cr.RatePerMonth * 12m, 2, MidpointRounding.AwayFromZero);

                if (cr.Route != null)
                    return AnnualBillingHelper.ToAnnualPrice(cr.Route.Price, cr.Route.BillingFrequency);

                return 0m;
            });
    }
}
