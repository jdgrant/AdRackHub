namespace AdRackHub.Models;

public readonly record struct UsStateInfo(string Abbreviation, string Name)
{
    public string Slug => Name.ToLowerInvariant().Replace(' ', '-');
    public string ProvinceCode => $"US-{Abbreviation}";
}

/// <summary>
/// Wave's Zapier Province / State/Region dropdown uses the lowercase name slug
/// (kentucky), not the postal abbreviation (KY) or a numeric ID.
/// </summary>
public static class UsState
{
    private static readonly UsStateInfo[] All =
    {
        new("AL", "Alabama"), new("AK", "Alaska"), new("AZ", "Arizona"), new("AR", "Arkansas"),
        new("CA", "California"), new("CO", "Colorado"), new("CT", "Connecticut"), new("DE", "Delaware"),
        new("DC", "District of Columbia"), new("FL", "Florida"), new("GA", "Georgia"), new("HI", "Hawaii"),
        new("ID", "Idaho"), new("IL", "Illinois"), new("IN", "Indiana"), new("IA", "Iowa"),
        new("KS", "Kansas"), new("KY", "Kentucky"), new("LA", "Louisiana"), new("ME", "Maine"),
        new("MD", "Maryland"), new("MA", "Massachusetts"), new("MI", "Michigan"), new("MN", "Minnesota"),
        new("MS", "Mississippi"), new("MO", "Missouri"), new("MT", "Montana"), new("NE", "Nebraska"),
        new("NV", "Nevada"), new("NH", "New Hampshire"), new("NJ", "New Jersey"), new("NM", "New Mexico"),
        new("NY", "New York"), new("NC", "North Carolina"), new("ND", "North Dakota"), new("OH", "Ohio"),
        new("OK", "Oklahoma"), new("OR", "Oregon"), new("PA", "Pennsylvania"), new("RI", "Rhode Island"),
        new("SC", "South Carolina"), new("SD", "South Dakota"), new("TN", "Tennessee"), new("TX", "Texas"),
        new("UT", "Utah"), new("VT", "Vermont"), new("VA", "Virginia"), new("WA", "Washington"),
        new("WV", "West Virginia"), new("WI", "Wisconsin"), new("WY", "Wyoming")
    };

    private static readonly Dictionary<string, UsStateInfo> Lookup = BuildLookup();

    public static UsStateInfo? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var key = value.Trim().ToLowerInvariant();
        if (key.StartsWith("us-", StringComparison.Ordinal))
            key = key[3..];

        return Lookup.TryGetValue(key, out var info) ? info : null;
    }

    public static string? ToAbbreviation(string? value)
    {
        var parsed = Parse(value);
        if (parsed.HasValue)
            return parsed.Value.Abbreviation;
        var raw = value?.Trim();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    private static Dictionary<string, UsStateInfo> BuildLookup()
    {
        var lookup = new Dictionary<string, UsStateInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in All)
        {
            lookup[info.Abbreviation] = info;
            lookup[info.Name] = info;
            lookup[info.Slug] = info;
        }

        return lookup;
    }
}
