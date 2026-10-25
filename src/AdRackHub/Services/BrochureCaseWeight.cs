namespace AdRackHub.Services;

public sealed record BoardGradeOption(string Id, string Label, decimal? PoundsPerThousandSquareFeet);

public sealed class CaseWeightRequest
{
    public int SampleCount { get; init; }
    public decimal SampleWeight { get; init; }
    public bool SampleIsOunces { get; init; }
    public decimal GrossWeight { get; init; }
    public bool GrossIsOunces { get; init; }
    public decimal? LengthInches { get; init; }
    public decimal? WidthInches { get; init; }
    public decimal? HeightInches { get; init; }
    public decimal? BoardPoundsPerThousandSquareFeet { get; init; }
    public decimal? MeasuredTare { get; init; }
    public bool TareIsOunces { get; init; }
    public int Cases { get; init; } = 1;
}

public sealed class CaseWeightResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? Warning { get; init; }
    public decimal EachPounds { get; init; }
    public decimal TarePounds { get; init; }
    public bool TareEstimated { get; init; }
    public decimal NetPounds { get; init; }
    public decimal UnroundedCount { get; init; }
    public int CaseCount { get; init; }
    public int Cases { get; init; }
    public int Total { get; init; }
}

/// <summary>
/// Estimates brochures in a case from a sample weight and a full-case weight.
/// Box tare uses a regular slotted carton blank when an empty box was not weighed.
/// </summary>
public static class BrochureCaseWeight
{
    public const decimal JointInches = 1.5m;

    public const string FormulaSummary =
        "The box is treated as a regular slotted carton. The cardboard sheet is twice the length plus twice the width, plus a 1.5 in glue joint, by the height plus the width (the top and bottom flaps are each half the width). Tare in pounds is that area in square feet, times the board weight in pounds per 1,000 square feet, divided by 1,000.";

    public static IReadOnlyList<BoardGradeOption> Boards { get; } =
    [
        new("ect32", "32 ECT — light brochure carton (95 lb / 1,000 sq ft)", 95m),
        new("test200", "200# test — standard carton (126 lb / 1,000 sq ft)", 126m),
        new("ect44", "44 ECT (112 lb / 1,000 sq ft)", 112m),
        new("custom", "Custom board weight", null)
    ];

    public static decimal ToPounds(decimal weight, bool ounces) =>
        ounces ? weight / 16m : weight;

    /// <summary>
    /// RSC tare from outside dimensions in inches.
    /// 32 ECT is 26/26/26 paper with C-flute take-up and glue, about 95 lb per 1,000 sq ft.
    /// 200# test (42/26/42) is about 126. 44 ECT is about 112.
    /// </summary>
    public static decimal EstimateTarePounds(
        decimal lengthInches,
        decimal widthInches,
        decimal heightInches,
        decimal poundsPerThousandSquareFeet)
    {
        if (lengthInches <= 0 || widthInches <= 0 || heightInches <= 0 || poundsPerThousandSquareFeet <= 0)
            throw new ArgumentOutOfRangeException(nameof(lengthInches), "Box size and board weight must be greater than zero.");

        var sheetLength = 2m * (lengthInches + widthInches) + JointInches;
        var sheetWidth = heightInches + widthInches;
        var areaSquareFeet = sheetLength * sheetWidth / 144m;
        return areaSquareFeet * poundsPerThousandSquareFeet / 1000m;
    }

    public static CaseWeightResult Calculate(CaseWeightRequest request)
    {
        if (request.SampleCount <= 0)
            return Fail("Enter how many brochures you weighed.");
        if (request.SampleWeight <= 0)
            return Fail("Enter the weight of those brochures.");
        if (request.GrossWeight <= 0)
            return Fail("Enter the full case weight.");
        if (request.Cases <= 0)
            return Fail("Enter at least one case.");

        var eachPounds = ToPounds(request.SampleWeight, request.SampleIsOunces) / request.SampleCount;
        if (eachPounds <= 0)
            return Fail("Enter the weight of those brochures.");

        decimal tarePounds;
        var tareEstimated = false;
        if (request.MeasuredTare is > 0)
        {
            tarePounds = ToPounds(request.MeasuredTare.Value, request.TareIsOunces);
        }
        else if (request.LengthInches is > 0
                 && request.WidthInches is > 0
                 && request.HeightInches is > 0
                 && request.BoardPoundsPerThousandSquareFeet is > 0)
        {
            tarePounds = EstimateTarePounds(
                request.LengthInches.Value,
                request.WidthInches.Value,
                request.HeightInches.Value,
                request.BoardPoundsPerThousandSquareFeet.Value);
            tareEstimated = true;
        }
        else
        {
            return Fail("Enter the box length, width, and height in inches, or the weight of an empty box.");
        }

        var grossPounds = ToPounds(request.GrossWeight, request.GrossIsOunces);
        var netPounds = grossPounds - tarePounds;
        if (netPounds <= 0)
            return Fail("The case weighs less than the box. Check the weights or the box size.");
        if (netPounds < eachPounds)
            return Fail("That sample is heavier than the brochures in the case. Check the weights.");

        var unrounded = netPounds / eachPounds;
        var caseCount = (int)Math.Round(unrounded, MidpointRounding.AwayFromZero);
        if (caseCount <= 0)
            return Fail("The case is lighter than one brochure. Check the weights.");

        var product = (long)request.Cases * caseCount;
        if (product > int.MaxValue)
            return Fail("That many brochures does not fit in a total.");

        var drift = Math.Abs(unrounded - caseCount);
        string? warning = drift > 0.35m
            ? $"The weight works out to {unrounded:0.0} brochures, rounded to {caseCount:N0}. If that looks off, weigh an empty box or a larger sample."
            : null;

        return new CaseWeightResult
        {
            Ok = true,
            Warning = warning,
            EachPounds = eachPounds,
            TarePounds = tarePounds,
            TareEstimated = tareEstimated,
            NetPounds = netPounds,
            UnroundedCount = unrounded,
            CaseCount = caseCount,
            Cases = request.Cases,
            Total = (int)product
        };
    }

    private static CaseWeightResult Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}
