using AdRackHub.Services;
using Xunit;

namespace AdRackHub.Tests;

public class BrochureInventoryTests
{
    [Theory]
    [InlineData("2", 100, null, 200)]
    [InlineData("2", 100, 250, 250)]
    [InlineData(null, null, 10, 10)]
    [InlineData("0", 0, null, 0)]
    [InlineData("6.75", 1200, null, 8100)]
    [InlineData("1.25", 100, null, 125)]
    public void ResolveTotal_UsesAnExplicitTotalOrTheProduct(string? cases, int? caseCount, int? total, int expected)
    {
        decimal? parsedCases = cases == null ? null : decimal.Parse(cases);
        Assert.Equal(expected, BrochureInventoryEntry.ResolveTotal(parsedCases, caseCount, total));
    }

    [Theory]
    [InlineData("2", null, null)]
    [InlineData(null, 100, null)]
    [InlineData("-1", 5, null)]
    [InlineData(null, null, -1)]
    public void ResolveTotal_RejectsAPartialOrNegativeEntry(string? cases, int? caseCount, int? total)
    {
        decimal? parsedCases = cases == null ? null : decimal.Parse(cases);
        Assert.Null(BrochureInventoryEntry.ResolveTotal(parsedCases, caseCount, total));
    }

    [Theory]
    [InlineData("3 cases × 500", "3", 500)]
    [InlineData("12 cases x 250", "12", 250)]
    [InlineData("Received 4 cases × 100 today", "4", 100)]
    [InlineData("Received 10cs@280 NEW", "10", 280)]
    [InlineData("78cs@ 1800- chg out", "78", 1800)]
    [InlineData("Inventory count 6.75@1200", "6.75", 1200)]
    [InlineData("Received 12@1500?", "12", 1500)]
    [InlineData("Received Jan-Mar 80bundles@100", "80", 100)]
    [InlineData("Jim Beam Received 4 NEW @750- use old ok too", "4", 750)]
    public void PackFromNotes_ReadsTheCasePack(string notes, string cases, int caseCount)
    {
        var pack = BrochureInventoryEntry.PackFromNotes(notes);
        Assert.Equal(decimal.Parse(cases), pack.Cases);
        Assert.Equal(caseCount, pack.CaseCount);
    }

    [Theory]
    [InlineData("1.25 cs counted", "1.25")]
    [InlineData("delvd 1/2cs to RA#1", "0.5")]
    [InlineData("Dumped 5 cases OLD when new received", "5")]
    public void PackFromNotes_ReadsCasesWhenTheCountIsMissing(string notes, string cases)
    {
        var pack = BrochureInventoryEntry.PackFromNotes(notes);
        Assert.Equal(decimal.Parse(cases), pack.Cases);
        Assert.Null(pack.CaseCount);
    }

    [Fact]
    public void PackFromNotes_IgnoresNotesWithoutACasePack()
    {
        var blank = BrochureInventoryEntry.PackFromNotes("left on the dock");
        Assert.Null(blank.Cases);
        Assert.Null(blank.CaseCount);
        var missing = BrochureInventoryEntry.PackFromNotes(null);
        Assert.Null(missing.Cases);
        Assert.Null(missing.CaseCount);
    }

    [Fact]
    public void EstimateTare_UsesTheSlottedCartonBlank()
    {
        var tare = BrochureCaseWeight.EstimateTarePounds(12m, 9m, 6m, 95m);

        Assert.Equal(0.43046875m, tare);
    }

    [Fact]
    public void Calculate_TurnsASampleAndAFullCaseIntoACaseCount()
    {
        var tare = BrochureCaseWeight.EstimateTarePounds(12m, 9m, 6m, 95m);
        var result = BrochureCaseWeight.Calculate(new CaseWeightRequest
        {
            SampleCount = 10,
            SampleWeight = 4m,
            SampleIsOunces = true,
            GrossWeight = 12.5m + tare,
            GrossIsOunces = false,
            LengthInches = 12m,
            WidthInches = 9m,
            HeightInches = 6m,
            BoardPoundsPerThousandSquareFeet = 95m,
            Cases = 4
        });

        Assert.True(result.Ok);
        Assert.Null(result.Warning);
        Assert.Equal(0.025m, result.EachPounds);
        Assert.True(result.TareEstimated);
        Assert.Equal(500, result.CaseCount);
        Assert.Equal(4, result.Cases);
        Assert.Equal(2000, result.Total);
    }

    [Fact]
    public void Calculate_PrefersAMeasuredEmptyBox()
    {
        var result = BrochureCaseWeight.Calculate(new CaseWeightRequest
        {
            SampleCount = 10,
            SampleWeight = 0.25m,
            GrossWeight = 12.75m,
            MeasuredTare = 0.25m,
            Cases = 1
        });

        Assert.True(result.Ok);
        Assert.False(result.TareEstimated);
        Assert.Equal(500, result.CaseCount);
        Assert.Equal(500, result.Total);
    }

    [Fact]
    public void Calculate_RejectsACaseThatWeighsLessThanTheBox()
    {
        var result = BrochureCaseWeight.Calculate(new CaseWeightRequest
        {
            SampleCount = 10,
            SampleWeight = 1m,
            GrossWeight = 0.2m,
            MeasuredTare = 0.5m,
            Cases = 1
        });

        Assert.False(result.Ok);
        Assert.Contains("less than the box", result.Error);
    }
}
