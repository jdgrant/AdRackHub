namespace AdRackHub.Services;

public static class RouteImportMap
{
    public static readonly IReadOnlyDictionary<string, string> KeyToRouteName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["central-oh"] = "Exit - Central OH",
            ["northern-interstate"] = "Exit - Northern Interstate",
            ["cincinnati-nky"] = "Exit - Cincinnati-NKY",
            ["i-65-24"] = "Exit - I-65 & 24",
            ["i-75"] = "Exit - I-75",
            ["lex-frankfort"] = "Exit - Lex-Frankfort",
            ["louisville"] = "Exit - Louisville",
            ["mid-tn"] = "Exit - Mid-TN",
            ["northeast-oh"] = "Exit - Northeast OH",
            ["i-64-east-lex"] = "Rest Area - I-64 East of Lexington",
            ["i-75-rest-areas"] = "Rest Area - I-75",
            ["i-64-west-i-71"] = "Rest Area - I-64 West & I-71",
            ["i-65-rest-areas"] = "Rest Area - I-65"
        };
}
