using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260923010000_AddBrochureWarehouse")]
    public partial class AddBrochureWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Warehouse",
                table: "Customers",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Warehouse",
                table: "CustomerBrochureInventories",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE Customers
                SET Warehouse = N'KY'
                WHERE Warehouse IS NULL
                  AND (WarehouseRack IS NOT NULL OR WarehouseBin IS NOT NULL OR [Type] = N'Customer');
                """);

            migrationBuilder.Sql("""
                UPDATE CustomerBrochureInventories
                SET Warehouse = N'KY'
                WHERE Warehouse IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Warehouse", table: "Customers");
            migrationBuilder.DropColumn(name: "Warehouse", table: "CustomerBrochureInventories");
        }
    }
}
