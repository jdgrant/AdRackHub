using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260923020000_ShortenBrochureWarehouseCodes")]
    public partial class ShortenBrochureWarehouseCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE Customers SET Warehouse = N'K' WHERE Warehouse = N'KY';
                UPDATE Customers SET Warehouse = N'O' WHERE Warehouse = N'OH';
                UPDATE CustomerBrochureInventories SET Warehouse = N'K' WHERE Warehouse = N'KY';
                UPDATE CustomerBrochureInventories SET Warehouse = N'O' WHERE Warehouse = N'OH';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE Customers SET Warehouse = N'KY' WHERE Warehouse = N'K';
                UPDATE Customers SET Warehouse = N'OH' WHERE Warehouse = N'O';
                UPDATE CustomerBrochureInventories SET Warehouse = N'KY' WHERE Warehouse = N'K';
                UPDATE CustomerBrochureInventories SET Warehouse = N'OH' WHERE Warehouse = N'O';
                """);
        }
    }
}
