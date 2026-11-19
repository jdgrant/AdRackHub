using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117160000_SetRestAreaListRates")]
    public class SetRestAreaListRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Monthly list price, matching the rest area rate schedule.
            migrationBuilder.Sql("""
                UPDATE [Routes] SET [Price] = 150.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Rest Area - I-64 East of Lexington';
                UPDATE [Routes] SET [Price] = 80.00,  [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Rest Area - I-64 West & I-71';
                UPDATE [Routes] SET [Price] = 80.00,  [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Rest Area - I-65';
                UPDATE [Routes] SET [Price] = 120.00, [BillingFrequency] = N'Monthly' WHERE [RouteName] = N'Rest Area - I-75';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Routes] SET [Price] = 0.00, [BillingFrequency] = N'Quarterly' WHERE [RouteName] = N'Rest Area - I-64 East of Lexington';
                UPDATE [Routes] SET [Price] = 0.00, [BillingFrequency] = N'Quarterly' WHERE [RouteName] = N'Rest Area - I-64 West & I-71';
                UPDATE [Routes] SET [Price] = 0.00, [BillingFrequency] = N'Quarterly' WHERE [RouteName] = N'Rest Area - I-65';
                UPDATE [Routes] SET [Price] = 0.00, [BillingFrequency] = N'Quarterly' WHERE [RouteName] = N'Rest Area - I-75';
                """);
        }
    }
}
