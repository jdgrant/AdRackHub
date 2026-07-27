using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractBillingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContractEndDate",
                table: "CustomerBillings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextBillDate",
                table: "CustomerBillings",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "ServiceMonthMask",
                table: "CustomerBillings",
                type: "int",
                nullable: false,
                defaultValue: 4095);

            migrationBuilder.Sql("""
                UPDATE CustomerBillings
                SET NextBillDate = DATEFROMPARTS(YEAR(GETDATE()), BillingAnchorMonth, 1),
                    ServiceMonthMask = 4095
                WHERE NextBillDate = '2026-01-01' OR ServiceMonthMask = 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractEndDate",
                table: "CustomerBillings");

            migrationBuilder.DropColumn(
                name: "NextBillDate",
                table: "CustomerBillings");

            migrationBuilder.DropColumn(
                name: "ServiceMonthMask",
                table: "CustomerBillings");
        }
    }
}
