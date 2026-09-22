using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractBillingMonthCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillingMonthCount",
                table: "CustomerBillings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.Sql(@"
UPDATE [CustomerBillings]
SET [BillingMonthCount] = CASE [Term]
    WHEN N'Monthly' THEN 1
    WHEN N'Quarterly' THEN 3
    WHEN N'EveryFourMonths' THEN 4
    WHEN N'Annual' THEN 12
    ELSE 3
END;");

            migrationBuilder.AddColumn<int>(
                name: "BillingMonthCount",
                table: "BillingRunInvoiceLines",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.Sql(@"
UPDATE [BillingRunInvoiceLines]
SET [BillingMonthCount] = CASE [Term]
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
                table: "CustomerBillings");

            migrationBuilder.DropColumn(
                name: "BillingMonthCount",
                table: "BillingRunInvoiceLines");
        }
    }
}
