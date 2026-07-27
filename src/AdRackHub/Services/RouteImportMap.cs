namespace AdRackHub.Services;

public static class RouteImportMap
{
    public static readonly IReadOnlyDictionary<string, string> KeyToRouteName =
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
            ["northeast-oh"] = "Northeast OH",
            ["i-64-east-lex"] = "I-64 East of Lexington Rest Area",
            ["i-75-rest-areas"] = "I-75 Rest Areas",
            ["i-64-west-i-71"] = "I-64 West & I-71 Rest Area",
            ["i-65-rest-areas"] = "I-65 Rest Areas"
        };
}
