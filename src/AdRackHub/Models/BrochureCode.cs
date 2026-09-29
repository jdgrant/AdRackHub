using System.Text.RegularExpressions;

namespace AdRackHub.Models;

public static class BrochureCode
{
    public static char Letter(string? customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            return '#';

        foreach (var ch in customerName.Trim())
        {
            if (char.IsLetter(ch))
                return char.ToUpperInvariant(ch);
        }

        return '#';
    }

    public static string Format(char letter, int sequence) =>
        $"{letter}{sequence:000}";

    public static int? Sequence(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length < 4)
            return null;
        return int.TryParse(code[1..], out var value) ? value : null;
    }

    public static bool Matches(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Regex.IsMatch(code, @"^[A-Z#]\d{3}$");
}
