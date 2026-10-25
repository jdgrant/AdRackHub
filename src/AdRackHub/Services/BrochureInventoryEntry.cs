using System.Globalization;
using System.Text.RegularExpressions;

namespace AdRackHub.Services;

public static class BrochureInventoryEntry
{
    private static readonly Regex TimesPattern = new(
        @"(\d+(?:\.\d+)?)\s*cases\s*[×x]\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnitAtPattern = new(
        @"(\d+(?:\.\d+)?)\s*(?:cs|bundles?)\s*@\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LooseAtPattern = new(
        @"(\d+(?:\.\d+)?)(?:\s+[A-Za-z][\w./-]*){0,4}\s*@\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FractionPattern = new(
        @"(\d+)\s*/\s*(\d+)\s*c(?:s|ase)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CsOnlyPattern = new(
        @"(\d+(?:\.\d+)?)\s*cs\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CasesWordPattern = new(
        @"(\d+(?:\.\d+)?)\s*cases?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? ResolveTotal(decimal? cases, int? caseCount, int? total)
    {
        if (cases is < 0 || caseCount is < 0 || total is < 0)
            return null;
        if (total.HasValue)
            return total;
        if (!cases.HasValue || !caseCount.HasValue)
            return null;

        var product = decimal.Round(cases.Value * caseCount.Value, 0, MidpointRounding.AwayFromZero);
        if (product > int.MaxValue)
            return null;
        return (int)product;
    }

    public static (decimal? Cases, int? CaseCount) PackFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return (null, null);

        var times = TimesPattern.Match(notes);
        if (times.Success)
            return Pair(times);

        var unitAt = UnitAtPattern.Match(notes);
        if (unitAt.Success)
            return Pair(unitAt);

        var looseAt = LooseAtPattern.Match(notes);
        if (looseAt.Success)
            return Pair(looseAt);

        var fraction = FractionPattern.Match(notes);
        if (fraction.Success
            && int.TryParse(fraction.Groups[1].Value, out var numerator)
            && int.TryParse(fraction.Groups[2].Value, out var denominator)
            && denominator > 0)
            return (decimal.Round((decimal)numerator / denominator, 2), null);

        var csOnly = CsOnlyPattern.Match(notes);
        if (csOnly.Success && decimal.TryParse(csOnly.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var csOnlyCases))
            return (csOnlyCases, null);

        var casesWord = CasesWordPattern.Match(notes);
        if (casesWord.Success && decimal.TryParse(casesWord.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var wordCases))
            return (wordCases, null);

        return (null, null);
    }

    public static int? CaseCountFromNotes(string? notes) => PackFromNotes(notes).CaseCount;

    public static string FormatCases(decimal? cases) =>
        cases?.ToString("0.##") ?? "";

    private static (decimal? Cases, int? CaseCount) Pair(Match match)
    {
        decimal? cases = decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedCases)
            ? parsedCases
            : null;
        int? caseCount = int.TryParse(match.Groups[2].Value, out var parsedCount) && parsedCount > 0
            ? parsedCount
            : null;
        return (cases, caseCount);
    }
}
