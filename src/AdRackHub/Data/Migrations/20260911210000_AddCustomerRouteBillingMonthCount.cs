using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260911210000_AddCustomerRouteBillingMonthCount")]
    public partial class AddCustomerRouteBillingMonthCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillingMonthCount",
                table: "CustomerRoutes",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.Sql(@"
UPDATE [CustomerRoutes]
SET [BillingMonthCount] = CASE [BillingTerm]
    WHEN N'Monthly' THEN 1
    WHEN N'Quarterly' THEN 3
    WHEN N'EveryFourMonths' THEN 4
    WHEN N'Annual' THEN 12
    ELSE 3
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingMonthCount",
                table: "CustomerRoutes");
        }
    }
}
