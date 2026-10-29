using AdRackHub.Models;

namespace AdRackHub.Services;

public sealed class ContractedRouteOutline
{
    public required Models.Route Route { get; init; }
    public bool AllStops { get; init; }
    public required IReadOnlyList<Stop> Stops { get; init; }
    public required IReadOnlyList<CustomerContract> Contracts { get; init; }
}

/// <summary>
/// Stops shown on the customer Routes tab: active contracts, then either every
/// active stop on the route or the customer's selected stops.
/// </summary>
public static class CustomerRouteLocations
{
    public static IReadOnlyList<ContractedRouteOutline> Build(Customer customer)
    {
        var activeContractRoutes = customer.Contracts
            .Where(contract => BillingDueCalculator.IsActiveContract(contract))
            .SelectMany(contract => contract.ContractRoutes.Select(contractRoute => new
            {
                Contract = contract,
                ContractRoute = contractRoute
            }))
            .Where(item => item.ContractRoute.Route != null)
            .OrderBy(item => item.ContractRoute.Route.RouteName)
            .ThenBy(item => item.Contract.ContractName)
            .ToList();

        var customerRoutesByRouteId = customer.CustomerRoutes
            .Where(customerRoute => customerRoute.Status == CustomerRouteStatus.Active)
            .GroupBy(customerRoute => customerRoute.RouteId)
            .ToDictionary(group => group.Key, group => group.First());

        return activeContractRoutes
            .GroupBy(item => item.ContractRoute.RouteId)
            .Select(group =>
            {
                var route = group.First().ContractRoute.Route;
                customerRoutesByRouteId.TryGetValue(group.Key, out var customerRoute);
                var allStops = customerRoute?.AllStops ?? true;

                IEnumerable<Stop> stops = customerRoute == null || customerRoute.AllStops
                    ? OrderStops(route.Stops.Where(stop => stop.Status == StopStatus.Active))
                    : OrderStops(customerRoute.CustomerRouteStops
                        .Where(assignment => assignment.Stop is { Status: StopStatus.Active })
                        .Select(assignment => assignment.Stop));

                return new ContractedRouteOutline
                {
                    Route = route,
                    AllStops = allStops,
                    Stops = stops.ToList(),
                    Contracts = group
                        .Select(item => item.Contract)
                        .GroupBy(contract => contract.Id)
                        .Select(contracts => contracts.First())
                        .OrderBy(contract => contract.ContractName)
                        .ToList()
                };
            })
            .OrderBy(outline => outline.Route.RouteName)
            .ToList();
    }

    public static string FormatLocation(Stop stop)
    {
        var parts = new[] { stop.Address, stop.City, stop.State, stop.Zip }
            .Select(part => part?.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var text = string.Join(", ", parts);
        return string.IsNullOrEmpty(text) ? "—" : text;
    }

    private static IEnumerable<Stop> OrderStops(IEnumerable<Stop> stops) =>
        stops
            .OrderBy(stop => stop.StepNumber.HasValue ? 0 : 1)
            .ThenBy(stop => stop.StepNumber)
            .ThenBy(stop => stop.StopName);
}
