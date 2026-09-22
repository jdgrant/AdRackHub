namespace AdRackHub.Models;

public static class CustomerContactCompleteness
{
    public static bool NeedsMoreInfo(Customer customer) =>
        !HasUsableAddress(customer) || !HasCoordinates(customer);

    public static bool HasUsableAddress(Customer customer)
    {
        if (HasStreetCityState(customer.Address, customer.City, customer.State))
            return true;

        return customer.Contacts?.Any(c => HasStreetCityState(c.Address, c.City, c.State)) == true;
    }

    public static bool HasCoordinates(Customer customer) =>
        customer.Latitude.HasValue && customer.Longitude.HasValue;

    public static bool HasStreetCityState(string? address, string? city, string? state) =>
        HasText(address) && HasText(city) && HasText(state);

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
