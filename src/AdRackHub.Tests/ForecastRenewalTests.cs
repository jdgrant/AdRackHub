using AdRackHub.Models;
using Xunit;

namespace AdRackHub.Tests;

public class ForecastRenewalTests
{
    static readonly DateOnly ForecastStart = new(2026, 11, 1);

    [Fact]
    public void EndedQuarterlyContractBillsTheFullQuarterOnEveryCycleWhenRenewalIsAssumed()
    {
        var contract = QuarterlyContract(900m, new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31));

        var renewed = Forecast(contract, ignoreEndDate: true);
        var stopped = Forecast(contract, ignoreEndDate: false);

        Assert.Equal(0m, Month(stopped, 2027, 1));
        Assert.Equal(900m, Month(renewed, 2027, 1));
        Assert.Equal(900m, Month(renewed, 2027, 4));
        Assert.Equal(900m, Month(renewed, 2027, 7));
        Assert.Equal(900m, Month(renewed, 2027, 10));
        Assert.Equal(3600m, renewed.Sum(m => m.Amount));
    }

    [Fact]
    public void EndedMonthlyContractBillsEveryMonthWhenRenewalIsAssumed()
    {
        var contract = new CustomerContract
        {
            Id = 2,
            CustomerId = 1,
            ContractName = "Monthly",
            Term = BillingFrequency.Monthly,
            BillingMonthCount = 1,
            ServiceMonthMask = SubscribedMonths.AllMonthsMask,
            NextBillDate = new DateOnly(2027, 1, 1),
            ContractEndDate = new DateOnly(2026, 12, 31),
            ContractRoutes = new List<CustomerContractRoute> { new() { BillingAmount = 100m } }
        };

        var renewed = Forecast(contract, ignoreEndDate: true);

        Assert.Equal(100m, Month(renewed, 2026, 11));
        Assert.Equal(100m, Month(renewed, 2026, 12));
        Assert.Equal(100m, Month(renewed, 2027, 1));
        Assert.Equal(100m, Month(renewed, 2027, 10));
        Assert.Equal(1200m, renewed.Sum(m => m.Amount));
    }

    [Fact]
    public void RenewalStillSkipsMonthsThatAreOutOfService()
    {
        var contract = QuarterlyContract(900m, new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31));
        contract.ServiceMonthMask = SubscribedMonths.BuildMask([5, 6, 7, 8, 9, 10]);

        var renewed = Forecast(contract, ignoreEndDate: true);

        Assert.Equal(0m, Month(renewed, 2027, 1));
        Assert.Equal(600m, Month(renewed, 2027, 4));
        Assert.Equal(900m, Month(renewed, 2027, 7));
    }

    static decimal Month(IReadOnlyList<BillingMonthForecast> months, int year, int month) =>
        months.Single(m => m.Year == year && m.Month == month).Amount;

    [Fact]
    public void AProposalReplacesTheAssumedRenewalForThatCustomer()
    {
        var signed = QuarterlyContract(900m, new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31));
        var proposal = new CustomerContract
        {
            Id = 9,
            CustomerId = signed.CustomerId,
            ContractName = "2027 proposal",
            IsProposal = true,
            Term = BillingFrequency.Annual,
            BillingMonthCount = 12,
            ServiceMonthMask = SubscribedMonths.AllMonthsMask,
            ContractStartDate = new DateOnly(2027, 1, 1),
            NextBillDate = null,
            ContractRoutes = new List<CustomerContractRoute> { new() { BillingAmount = 2400m } }
        };
        var other = QuarterlyContract(500m, new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31));
        other.Id = 3;
        other.CustomerId = 2;
        other.ContractName = "No proposal";

        var assumed = BillingForecastCalculator.Build([signed, proposal, other], ForecastStart, 12, 2027, 1, ignoreEndDate: true);
        var withProposals = BillingForecastCalculator.Build([signed, proposal, other], ForecastStart, 12, 2027, 1, ignoreEndDate: true, preferProposals: true);

        Assert.Equal(1400m, Month(assumed, 2027, 1));
        Assert.DoesNotContain(MonthLines(assumed, 2027, 1), line => line.IsProposal);

        var january = MonthLines(withProposals, 2027, 1);
        Assert.Equal(2900m, january.Sum(line => line.Amount));
        Assert.DoesNotContain(MonthLines(withProposals, 2026, 11), line => line.IsProposal);
        Assert.Contains(january, line => line.IsProposal && line.Amount == 2400m && line.CustomerId == signed.CustomerId);
        Assert.DoesNotContain(january, line => line.CustomerId == signed.CustomerId && !line.IsProposal);
        Assert.Contains(january, line => line.CustomerId == other.CustomerId && line.Amount == 500m);
    }

    static List<BillingForecastLine> MonthLines(IReadOnlyList<BillingMonthForecast> months, int year, int month) =>
        months.Single(m => m.Year == year && m.Month == month).Lines;

    static IReadOnlyList<BillingMonthForecast> Forecast(CustomerContract contract, bool ignoreEndDate) =>
        BillingForecastCalculator.Build(
            [contract],
            ForecastStart,
            12,
            2027,
            1,
            ignoreEndDate: ignoreEndDate);

    static CustomerContract QuarterlyContract(decimal amount, DateOnly nextBill, DateOnly end) => new()
    {
        Id = 1,
        CustomerId = 1,
        ContractName = "Quarterly",
        Term = BillingFrequency.Quarterly,
        BillingMonthCount = 3,
        ServiceMonthMask = SubscribedMonths.AllMonthsMask,
        NextBillDate = nextBill,
        ContractEndDate = end,
        ContractRoutes = new List<CustomerContractRoute> { new() { BillingAmount = amount } }
    };
}
