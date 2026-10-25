using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AdRackHub.Tests;

public class AgentBillingTests
{
    [Fact]
    public async Task DueAndOutstandingMatchTheBillingPages()
    {
        await using var db = NewDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var oldMonth = today.AddDays(-90);
        db.Customers.Add(new Customer
        {
            CustomerName = "Riverside Museum",
            Type = CustomerType.Customer,
            Status = CustomerStatus.Active
        });
        db.Routes.Add(new Route { RouteName = "I-65 South", Price = 100 });
        await db.SaveChangesAsync();

        var customer = db.Customers.Single();
        var route = db.Routes.Single();
        db.CustomerContracts.Add(new CustomerContract
        {
            CustomerId = customer.Id,
            ContractName = "Welcome center",
            Term = BillingFrequency.Quarterly,
            BillingMonthCount = 3,
            ServiceMonthMask = SubscribedMonths.AllMonthsMask,
            NextBillDate = today,
            ContractRoutes =
            [
                new CustomerContractRoute
                {
                    RouteId = route.Id,
                    BillingAmount = 250
                }
            ]
        });
        db.BillingRuns.Add(new BillingRun
        {
            Year = oldMonth.Year,
            Month = oldMonth.Month,
            Status = BillingRunStatus.Submitted,
            Invoices =
            [
                new BillingRunInvoice
                {
                    CustomerId = customer.Id,
                    TotalAmount = 80,
                    Status = BillingRunInvoiceStatus.Submitted,
                    WaveInvoiceNumber = "W-100",
                    Lines =
                    [
                        new BillingRunInvoiceLine
                        {
                            ContractName = "Welcome center",
                            Term = BillingFrequency.Quarterly,
                            BillingMonthCount = 3,
                            RouteName = "I-65 South",
                            Amount = 80
                        }
                    ]
                }
            ]
        });
        await db.SaveChangesAsync();

        var api = new AgentBillingService(new MonthlyBillingService(
            db,
            null!,
            null!,
            NullLogger<MonthlyBillingService>.Instance));

        var due = await api.DueAsync(today.Year, today.Month, null, 1, 50, "https://hub.ad-rack.net", CancellationToken.None);
        Assert.Equal(250, due.Body!.DueTotal);
        var dueItem = Assert.Single(due.Body.Items);
        Assert.Equal("Welcome center", dueItem.ContractName);
        Assert.Equal("I-65 South", Assert.Single(dueItem.Routes));
        Assert.Contains("/Billing?year=", due.Body.HubUrl);

        var outstanding = await api.OutstandingAsync(null, null, null, 1, 50, "https://hub.ad-rack.net", CancellationToken.None);
        Assert.Equal(80, outstanding.Body!.Over60.Amount);
        Assert.Equal(1, outstanding.Body.Over60.InvoiceCount);
        Assert.Equal(80, outstanding.Body.TotalAmount);
        Assert.Contains("/Billing/Outstanding", outstanding.Body.HubUrl);

        var forecast = await api.ForecastAsync(today.Year, today.Month, 12, false, 1, 50, "https://hub.ad-rack.net", CancellationToken.None);
        var current = Assert.Single(forecast.Body!.Months, month => month.Year == today.Year && month.Month == today.Month);
        Assert.Equal(250, current.Amount);
        Assert.Contains(forecast.Body.Lines, line => line.ContractName == "Welcome center" && line.Amount == 250);
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
