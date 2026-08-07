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

    public static bool NamesMatch(string? left, string? right) =>
        StripPrefix(left).Equals(StripPrefix(right), StringComparison.OrdinalIgnoreCase);
}
