using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdRackHub.Tests;

public class ProfitLossHistoryTests
{
    [Fact]
    public void Workbook_LoadsYearSheetsAndSkipsComparisons()
    {
        var lines = ProfitLossHistoryImportService.ReadWorkbook(WorkbookPath());

        Assert.DoesNotContain(lines, line => line.Category.Contains("YoY", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(581804.32m, Sales(lines, 2024));
        Assert.Equal(559414.50m, Sales(lines, 2025));
        Assert.Equal(209794.50m, Sales(lines, 2026));
        Assert.Equal(12, lines.Count(line => line.Year == 2024 && line.Category == "Sales"));
        Assert.Equal(5, lines.Count(line => line.Year == 2026 && line.Category == "Sales"));
        Assert.Equal(47463m, lines.Single(line => line.Year == 2026 && line.Month == 1 && line.Category == "Sales").Amount);
        Assert.Equal(-66.84m, lines.Single(line =>
            line.Year == 2024 && line.Month == 8 && line.Category == "Employee Personal Expense").Amount);
        Assert.Contains(lines, line => line.Section == ProfitLossSections.Expense && line.Category == "Payroll Expenses");
        Assert.Contains(lines, line => line.Section == ProfitLossSections.OtherIncome && line.Category == "Interest Income");
        Assert.Contains(lines, line => line.Section == ProfitLossSections.OtherExpense && line.Category == "Investment Expense");
        Assert.Contains(lines, line => line.Section == ProfitLossSections.Result && line.Category == "Net Income");
        Assert.Equal(7886.79m, lines.Where(line => line.Year == 2024 && line.Category == "Net Income").Sum(line => line.Amount));
        Assert.Equal(63804.37m, lines.Where(line => line.Year == 2025 && line.Category == "Net Income").Sum(line => line.Amount));
        Assert.Equal(54276.58m, lines.Where(line => line.Year == 2026 && line.Category == "Net Income").Sum(line => line.Amount));
    }

    [Fact]
    public async Task Report_ComparesSalesWithBillingAndTheSameMonthsLastYear()
    {
        await using var db = NewDb();
        db.ProfitLossHistoryLines.AddRange(
            Sale(2025, 8, 40m),
            Sale(2025, 9, 100m),
            Sale(2025, 10, 200m),
            Sale(2026, 1, 80m),
            Sale(2026, 5, 250m));
        var customer = new Customer { CustomerName = "North Lodge" };
        var september = new BillingRun { Year = 2026, Month = 9, Status = BillingRunStatus.Submitted };
        var october = new BillingRun { Year = 2026, Month = 10, Status = BillingRunStatus.Submitted };
        var august = new BillingRun { Year = 2026, Month = 8, Status = BillingRunStatus.Submitted };
        var priorYear = new BillingRun { Year = 2025, Month = 9, Status = BillingRunStatus.Submitted };
        db.AddRange(customer, september, october, august, priorYear);
        await db.SaveChangesAsync();
        db.BillingRunInvoices.AddRange(
            new BillingRunInvoice
            {
                BillingRunId = september.Id,
                CustomerId = customer.Id,
                TotalAmount = 70m,
                Status = BillingRunInvoiceStatus.Received
            },
            new BillingRunInvoice
            {
                BillingRunId = october.Id,
                CustomerId = customer.Id,
                TotalAmount = 250m,
                Status = BillingRunInvoiceStatus.Received
            },
            new BillingRunInvoice
            {
                BillingRunId = august.Id,
                CustomerId = customer.Id,
                TotalAmount = 40m,
                Status = BillingRunInvoiceStatus.Received
            },
            new BillingRunInvoice
            {
                BillingRunId = priorYear.Id,
                CustomerId = customer.Id,
                TotalAmount = 999m,
                Status = BillingRunInvoiceStatus.Received
            });
        await db.SaveChangesAsync();

        var page = await new ProfitLossRevenueReportService(db).BuildAsync();
        var september2026 = page.Months.Single(month => month.Month == 9).Years.Single(cell => cell.Year == 2026);
        var october2026 = page.Months.Single(month => month.Month == 10).Years.Single(cell => cell.Year == 2026);
        var total2026 = page.Total.Years.Single(cell => cell.Year == 2026);

        Assert.Equal(new[] { 9, 10, 11, 12 }, page.Months.Select(month => month.Month));
        Assert.Equal(new[] { 2026 }, page.Years);
        Assert.False(september2026.HasMom);
        Assert.Equal(-30m, september2026.YoyAmount);
        Assert.Equal(-0.3m, september2026.YoyPercent);
        Assert.Equal(70m, september2026.Received);
        Assert.Equal(180m, october2026.MomAmount);
        Assert.Equal(50m, october2026.YoyAmount);
        Assert.Equal(250m, october2026.Received);
        Assert.False(total2026.HasSales);
        Assert.Equal("Sep–Oct", total2026.PeriodLabel);
        Assert.Equal(20m, total2026.YoyAmount);
        Assert.Equal(320m, total2026.Received);
        Assert.Equal(new[] { "Sep 2026", "Oct 2026" }, page.BillingOnlyMonths);
        Assert.DoesNotContain(page.Months, month => month.Month < 9);
    }

    [Fact]
    public async Task Report_UsesTheWorkbookTotalsAgainstAPartialYear()
    {
        await using var db = NewDb();
        db.ProfitLossHistoryLines.AddRange(ProfitLossHistoryImportService.ReadWorkbook(WorkbookPath()));
        await db.SaveChangesAsync();

        var page = await new ProfitLossRevenueReportService(db).BuildAsync();
        var september = page.Months.Single(month => month.Month == 9).Years.Single(cell => cell.Year == 2026);
        var total = page.Total.Years.Single(cell => cell.Year == 2026);

        Assert.Equal(new[] { 9, 10, 11, 12 }, page.Months.Select(month => month.Month));
        Assert.Equal(new[] { 2026 }, page.Years);
        Assert.False(september.HasSales);
        Assert.False(september.HasYoy);
        Assert.False(total.HasSales);
        Assert.False(total.HasBilling);
        Assert.DoesNotContain(page.Statement, row => row.Years.Any(cell => cell.HasAmount));
        Assert.DoesNotContain(page.Columns, column => column.IncludeHub);
    }

    private static decimal Sales(IEnumerable<ProfitLossHistoryLine> lines, int year) =>
        lines.Where(line => line.Year == year && line.Section == ProfitLossSections.Income && line.Category == "Sales")
            .Sum(line => line.Amount);

    private static ProfitLossHistoryLine Sale(int year, int month, decimal amount) => new()
    {
        Year = year,
        Month = month,
        Section = ProfitLossSections.Income,
        LineKind = ProfitLossLineKinds.Line,
        Category = "Sales",
        Amount = amount,
        SortOrder = 1
    };

    private static string WorkbookPath()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "AdRackHub", "Data", "Imports",
            ProfitLossHistoryImportService.FileName));
        Assert.True(File.Exists(path), path);
        return path;
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
