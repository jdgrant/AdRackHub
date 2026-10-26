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
}
