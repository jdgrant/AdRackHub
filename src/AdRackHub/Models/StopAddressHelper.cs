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

        // Prefer descriptive stop names (rest areas often only have mile-marker names).
        if (!string.IsNullOrWhiteSpace(stop.StopName))
        {
            var name = stop.StopName.Trim();
            if (!string.IsNullOrWhiteSpace(stop.State))
                return $"{name}, {stop.State.Trim()}";
            if (stop.StopType == StopType.RestArea)
                return $"{name}, KY";
            return name;
        }

        if (!string.IsNullOrWhiteSpace(stop.HighwayExit))
            return stop.HighwayExit.Trim();

        return string.Empty;
    }

    public static bool HasMappableLocation(Stop stop)
    {
        if (stop.Latitude != null && stop.Longitude != null)
            return true;

        var fullAddress = FormatFullAddress(stop);
        if (fullAddress != "—")
            return true;

        // Highway/exit is often the only location for rest areas
        return !string.IsNullOrWhiteSpace(stop.HighwayExit);
    }
}
