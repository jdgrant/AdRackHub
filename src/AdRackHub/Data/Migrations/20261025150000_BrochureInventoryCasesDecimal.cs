using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025150000_BrochureInventoryCasesDecimal")]
    public partial class BrochureInventoryCasesDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Cases",
                table: "CustomerBrochureInventories",
                type: "decimal(8,2)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Cases",
                table: "CustomerBrochureInventories",
                type: "int",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,2)",
                oldNullable: true);
        }
    }
}
