using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRouteListRates2026 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sheet: "ANNUAL RATE PER MO" stored as monthly list price (BillingFrequency = Monthly).
            migrationBuilder.Sql("""
                UPDATE [Routes] SET [Price] = 135.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Cincinnati-NKY';
                UPDATE [Routes] SET [Price] = 135.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Louisville';
                UPDATE [Routes] SET [Price] = 120.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Lex-Frankfort';
                UPDATE [Routes] SET [Price] = 175.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Northern Interstate';
                UPDATE [Routes] SET [Price] = 110.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Northeast OH';
                UPDATE [Routes] SET [Price] = 150.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Central OH';
                UPDATE [Routes] SET [Price] = 120.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'I-75';
                UPDATE [Routes] SET [Price] = 100.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'I-65 & 24';
                UPDATE [Routes] SET [Price] = 55.00,  [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Mid-TN';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Prior list rates were not versioned; no automatic restore.
        }
    }
}
