namespace AdRackHub.Models;

public static class StopAddressHelper
{
    public static string FormatFullAddress(Stop stop)
    {
        var line1 = stop.Address?.Trim();
        var cityStateZip = string.Join(", ",
            new[] { stop.City, stop.State, stop.Zip }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(line1) && !string.IsNullOrWhiteSpace(cityStateZip))
            return $"{line1}, {cityStateZip}";
        if (!string.IsNullOrWhiteSpace(line1))
            return line1;
        if (!string.IsNullOrWhiteSpace(cityStateZip))
            return cityStateZip;
        return "—";
    }

    public static string GetGeocodingQuery(Stop stop)
    {
        var fullAddress = FormatFullAddress(stop);
        if (fullAddress != "—")
            return fullAddress;

        if (!string.IsNullOrWhiteSpace(stop.HighwayExit))
            return stop.HighwayExit.Trim();

        return stop.StopName.Trim();
    }

    public static bool HasMappableLocation(Stop stop)
    {
        var fullAddress = FormatFullAddress(stop);
        if (fullAddress != "—")
            return true;

        // Highway/exit is often the only location for rest areas
        return !string.IsNullOrWhiteSpace(stop.HighwayExit);
    }
}
