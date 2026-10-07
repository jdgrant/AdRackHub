using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261006120000_AddPrintOnlyContracts")]
    public partial class AddPrintOnlyContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "NextBillDate",
                table: "CustomerBillings",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "date");

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractStartDate",
                table: "CustomerBillings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdvertisingSpaces",
                table: "CustomerBillings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE [CustomerBillings]
                SET [ContractStartDate] = [NextBillDate]
                WHERE [ContractStartDate] IS NULL AND [NextBillDate] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [CustomerBillings]
                SET [NextBillDate] = COALESCE([NextBillDate], [ContractStartDate], CAST(GETDATE() AS date))
                WHERE [NextBillDate] IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "AdvertisingSpaces",
                table: "CustomerBillings");

            migrationBuilder.DropColumn(
                name: "ContractStartDate",
                table: "CustomerBillings");

            migrationBuilder.AlterColumn<DateTime>(
                name: "NextBillDate",
                table: "CustomerBillings",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true);
        }
    }
}
