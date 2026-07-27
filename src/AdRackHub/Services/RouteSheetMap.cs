namespace AdRackHub.Services;

public static class RouteSheetMap
{
    public static readonly IReadOnlyDictionary<string, string> SlugToSheetName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["central-oh"] = "Central OH",
            ["northern-interstate"] = "Northern Interstate",
            ["cincinnati-nky"] = "Cincinnati-NKY",
            ["i-65-24"] = "I-65 & 24",
            ["i-75"] = "I-75",
            ["lex-frankfort"] = "Lex-Frankfort",
            ["louisville"] = "Louisville",
            ["mid-tn"] = "Mid-TN",
            ["northeast-oh"] = "Northeast OH"
        };

    public static string? GetSlugForRouteName(string routeName) =>
        SlugToSheetName.FirstOrDefault(pair =>
            pair.Value.Equals(routeName, StringComparison.OrdinalIgnoreCase)).Key;

    public static string GetSheetName(string slug) =>
        SlugToSheetName.TryGetValue(slug, out var sheetName)
            ? sheetName
            : throw new InvalidOperationException($"Unknown route slug '{slug}'.");

    public static string GetRouteName(string slug) => GetSheetName(slug);
}
