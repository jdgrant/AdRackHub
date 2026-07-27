using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRouteSubscriptionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RatePerMonth",
                table: "CustomerRoutes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SubscribedMonthMask",
                table: "CustomerRoutes",
                type: "int",
                nullable: false,
                defaultValue: 4095);

            migrationBuilder.Sql("""
                UPDATE cr
                SET
                    RatePerMonth = CASE r.BillingFrequency
                        WHEN 'Quarterly' THEN r.Price / 3
                        WHEN 'Annual' THEN r.Price / 12
                        ELSE r.Price
                    END,
                    SubscribedMonthMask = 4095
                FROM CustomerRoutes cr
                INNER JOIN Routes r ON cr.RouteId = r.Id
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatePerMonth",
                table: "CustomerRoutes");

            migrationBuilder.DropColumn(
                name: "SubscribedMonthMask",
                table: "CustomerRoutes");
        }
    }
}
