using System.Text.RegularExpressions;

namespace AdRackHub.Models;

public static class CustomerAddressHelper
{
    private static readonly Regex UsZipPattern = new(@"\b(\d{5})(?:-\d{4})?\b", RegexOptions.Compiled);

    public static string FormatFullAddress(string? address, string? city, string? state, string? zip)
    {
        var line1 = address?.Trim();
        var cityStateZip = string.Join(", ",
            new[] { city, state, zip }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(line1) && !string.IsNullOrWhiteSpace(cityStateZip))
            return $"{line1}, {cityStateZip}";
        if (!string.IsNullOrWhiteSpace(line1))
            return line1;
        if (!string.IsNullOrWhiteSpace(cityStateZip))
            return cityStateZip;
        return string.Empty;
    }

    public static string GetGeocodingQuery(Customer customer)
    {
        var fromCustomer = FormatFullAddress(customer.Address, customer.City, customer.State, customer.Zip);
        if (!string.IsNullOrWhiteSpace(fromCustomer))
        {
            if (!string.IsNullOrWhiteSpace(customer.CustomerName) && string.IsNullOrWhiteSpace(customer.Address))
                return $"{customer.CustomerName.Trim()}, {fromCustomer}";
            return fromCustomer;
        }

        var contact = customer.Contacts?
            .OrderBy(c => c.Id)
            .FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.Address)
                || !string.IsNullOrWhiteSpace(c.City)
                || !string.IsNullOrWhiteSpace(c.State)
                || !string.IsNullOrWhiteSpace(c.Zip));

        if (contact != null)
        {
            var fromContact = FormatFullAddress(contact.Address, contact.City, contact.State, contact.Zip);
            if (!string.IsNullOrWhiteSpace(fromContact))
                return fromContact;
        }

        return customer.CustomerName?.Trim() ?? string.Empty;
    }

    public static bool AddressChanged(Customer before, Customer after) =>
        !FieldsEqual(before.Address, after.Address)
        || !FieldsEqual(before.City, after.City)
        || !FieldsEqual(before.State, after.State)
        || !FieldsEqual(before.Zip, after.Zip);

    public static bool AddressChanged(Contact before, Contact after) =>
        !FieldsEqual(before.Address, after.Address)
        || !FieldsEqual(before.City, after.City)
        || !FieldsEqual(before.State, after.State)
        || !FieldsEqual(before.Zip, after.Zip);

    public static bool HasGeocodableAddress(Customer customer)
    {
        if (HasAnyAddressPart(customer.Address, customer.City, customer.State, customer.Zip))
            return true;

        return customer.Contacts?.Any(c => HasAnyAddressPart(c.Address, c.City, c.State, c.Zip)) == true;
    }

    public static bool HasAnyAddressPart(string? address, string? city, string? state, string? zip) =>
        !string.IsNullOrWhiteSpace(address)
        || !string.IsNullOrWhiteSpace(city)
        || !string.IsNullOrWhiteSpace(state)
        || !string.IsNullOrWhiteSpace(zip);

    public static string? NormalizeUsZip(string? zip)
    {
        if (string.IsNullOrWhiteSpace(zip))
            return null;

        var match = UsZipPattern.Match(zip);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? GetUsZip(Customer customer)
    {
        var zip = NormalizeUsZip(customer.Zip);
        if (zip != null)
            return zip;

        return customer.Contacts?
            .Select(c => NormalizeUsZip(c.Zip))
            .FirstOrDefault(z => z != null);
    }

    private static bool FieldsEqual(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
