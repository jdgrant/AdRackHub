using AdRackHub.Models;
using Xunit;

namespace AdRackHub.Tests;

public class BillableMonthTests
{
    [Fact]
    public void SeasonalBillSkipsInactiveMonthsAndDoesNotDependOnTheBillMonth()
    {
        var contract = SeasonalContract();
        var november = new DateOnly(2026, 11, 1);
        var february = new DateOnly(2027, 2, 1);

        var fromNovember = BillingDueCalculator.BillableSpanFor(contract, november);
        var fromFebruary = BillingDueCalculator.BillableSpanFor(contract, february);

        Assert.Equal(6, fromNovember.MonthCount);
        Assert.Equal(new DateOnly(2027, 5, 1), fromNovember.Start);
        Assert.Equal(new DateOnly(2027, 10, 31), fromNovember.End);
        Assert.Equal(fromNovember, fromFebruary);
        Assert.Equal(1380m, BillingDueCalculator.RouteAmount(contract, 2760m, november));
        Assert.Equal(1380m, BillingDueCalculator.RouteAmount(contract, 2760m, february));
    }

    [Fact]
    public void YearRoundQuarterKeepsTheFullPeriodAmount()
    {
        var contract = new CustomerContract
        {
            BillingMonthCount = 3,
            Term = BillingFrequency.Quarterly,
            ServiceMonthMask = SubscribedMonths.AllMonthsMask
        };

        var span = BillingDueCalculator.BillableSpanFor(contract, new DateOnly(2026, 11, 1));

        Assert.Equal(3, span.MonthCount);
        Assert.Equal(300m, BillingDueCalculator.RouteAmount(contract, 300m, new DateOnly(2026, 11, 1)));
    }

    static CustomerContract SeasonalContract() => new()
    {
        BillingMonthCount = 12,
        Term = BillingFrequency.Annual,
        ServiceMonthMask = SubscribedMonths.BuildMask([5, 6, 7, 8, 9, 10]),
        ContractStartDate = new DateOnly(2027, 5, 1),
        ContractEndDate = new DateOnly(2027, 10, 31),
        NextBillDate = new DateOnly(2026, 11, 3)
    };
}
