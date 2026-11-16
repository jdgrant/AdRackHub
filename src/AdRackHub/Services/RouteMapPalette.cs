namespace AdRackHub.Services;

/// <summary>
/// Pin colors for the Maps page. Routes keep a stable color from alphabetical order.
/// </summary>
public static class RouteMapPalette
{
    public static readonly string[] Colors =
    {
        "#e6194b", "#3cb44b", "#4363d8", "#f58231", "#911eb4",
        "#42d4f4", "#f032e6", "#bfef45", "#469990", "#dcbeff",
        "#9A6324", "#800000", "#aaffc3", "#808000", "#000075",
        "#ffe119", "#ffd8b1", "#e6beff", "#008080", "#aa6e28"
    };

    public const string ProspectColor = "#6c757d";

    public static Dictionary<int, string> Assign(IReadOnlyList<int> routeIdsInNameOrder)
    {
        var colors = new Dictionary<int, string>(routeIdsInNameOrder.Count);
        for (var i = 0; i < routeIdsInNameOrder.Count; i++)
            colors[routeIdsInNameOrder[i]] = Colors[i % Colors.Length];
        return colors;
    }
}
