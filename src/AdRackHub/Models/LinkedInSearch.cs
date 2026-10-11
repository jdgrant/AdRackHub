namespace AdRackHub.Models;

public static class LinkedInSearch
{
    public static string? PeopleUrl(string? personName, string? company)
    {
        if (string.IsNullOrWhiteSpace(personName))
            return null;

        var keywords = string.IsNullOrWhiteSpace(company)
            ? personName.Trim()
            : $"{personName.Trim()} {company.Trim()}";
        return PeopleUrl(keywords);
    }

    public static string PeopleUrl(string keywords) =>
        "https://www.linkedin.com/search/results/people/?keywords=" + Uri.EscapeDataString(keywords.Trim());

    public static string? CompanyUrl(string? company)
    {
        if (string.IsNullOrWhiteSpace(company))
            return null;

        return "https://www.linkedin.com/search/results/companies/?keywords="
            + Uri.EscapeDataString(company.Trim());
    }

    public static string? GoogleProfileSearchUrl(string? personName, string? company)
    {
        if (string.IsNullOrWhiteSpace(personName))
            return null;

        var query = string.IsNullOrWhiteSpace(company)
            ? $"site:linkedin.com/in \"{personName.Trim()}\""
            : $"site:linkedin.com/in \"{personName.Trim()}\" \"{company.Trim()}\"";
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
    }

    public static string? NormalizeProfileUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var raw = value.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && !Uri.TryCreate("https://" + raw.TrimStart('/'), UriKind.Absolute, out uri))
            return null;

        if (!uri.Host.EndsWith("linkedin.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.StartsWith("/in/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/pub/", StringComparison.OrdinalIgnoreCase))
            return "https://www.linkedin.com" + path;

        return null;
    }
}
