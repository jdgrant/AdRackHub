namespace AdRackHub.Models;

public static class RouteNaming
{
    public const string ExitPrefix = "Exit - ";
    public const string RestAreaPrefix = "Rest Area - ";

    public static string StripPrefix(string? routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return string.Empty;

        var name = routeName.Trim();
        if (name.StartsWith(RestAreaPrefix, StringComparison.OrdinalIgnoreCase))
            return name[RestAreaPrefix.Length..].Trim();
        if (name.StartsWith(ExitPrefix, StringComparison.OrdinalIgnoreCase))
            return name[ExitPrefix.Length..].Trim();
        return name;
    }

    public static string DisplayName(string? routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return string.Empty;

        var name = routeName.Trim();
        if (name.StartsWith(ExitPrefix, StringComparison.OrdinalIgnoreCase))
            return name[ExitPrefix.Length..].Trim();
        if (name.StartsWith("Exit ", StringComparison.OrdinalIgnoreCase))
            return name[5..].Trim().TrimStart('-', ' ').Trim();
        return name;
    }

    public static string DistributionLabel(string? routeName)
    {
        var stripped = StripPrefix(routeName);
        if (string.IsNullOrWhiteSpace(stripped))
            return "Distribution";

        if (!string.IsNullOrWhiteSpace(routeName)
            && routeName.Trim().StartsWith(RestAreaPrefix, StringComparison.OrdinalIgnoreCase))
            return $"{stripped} Rest Area Distribution";

        return $"{stripped} Distribution";
    }

    public static bool NamesMatch(string? left, string? right) =>
        StripPrefix(left).Equals(StripPrefix(right), StringComparison.OrdinalIgnoreCase);
}
