namespace AdRackHub.Models;

public static class VisitDisplay
{
    public static string Format(DateTime? visitedAtUtc) =>
        visitedAtUtc?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "Never";
}
