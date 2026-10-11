using System.Text.RegularExpressions;

namespace AdRackHub.Models;

public static class ContactTitleParser
{
    public static bool ApplyFromLastName(Contact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.LastName) || !contact.LastName.Contains('('))
            return false;

        var (title, lastName) = Split(contact.LastName);
        var changed = false;
        if (!string.IsNullOrWhiteSpace(lastName) && lastName != contact.LastName)
        {
            contact.LastName = lastName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(contact.Title) && !string.IsNullOrWhiteSpace(title))
        {
            contact.Title = title;
            changed = true;
        }

        return changed;
    }

    public static (string? Title, string Remainder) Split(string value)
    {
        var start = value.IndexOf('(');
        if (start < 0)
            return (null, Collapse(value));

        var end = value.IndexOf(')', start + 1);
        string inside;
        string after;
        if (end < 0)
        {
            inside = value[(start + 1)..];
            after = "";
        }
        else
        {
            inside = value[(start + 1)..end];
            after = value[(end + 1)..];
        }

        inside = CleanFragment(inside);
        after = CleanFragment(after);
        var before = Collapse(value[..start].TrimEnd('-', ' '));
        var title = string.Join(" — ", new[] { inside, after }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (title.Length > 200)
            title = title[..200].Trim();
        return (string.IsNullOrWhiteSpace(title) ? null : title, before);
    }

    private static string CleanFragment(string value)
    {
        var cleaned = value.Trim().Trim('(', ')', '-', ',', ';', ' ').Trim();
        return Collapse(cleaned);
    }

    private static string Collapse(string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Regex.Replace(value.Trim(), @"\s+", " ");
}
