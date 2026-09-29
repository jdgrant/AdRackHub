using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260923200000_AddCustomerWarehouseLocations")]
    public partial class AddCustomerWarehouseLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerWarehouseLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Warehouse = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Rack = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Bin = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Shelf = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerWarehouseLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerWarehouseLocations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWarehouseLocations_CustomerId_SortOrder",
                table: "CustomerWarehouseLocations",
                columns: new[] { "CustomerId", "SortOrder" });

            migrationBuilder.Sql("""
                INSERT INTO [CustomerWarehouseLocations] ([CustomerId], [Warehouse], [Rack], [Bin], [Shelf], [SortOrder])
                SELECT [Id], [Warehouse], [WarehouseRack], [WarehouseBin], [WarehouseShelf], 0
                FROM [Customers]
                WHERE [WarehouseRack] IS NOT NULL
                   OR [WarehouseBin] IS NOT NULL
                   OR [WarehouseShelf] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerWarehouseLocations");
        }
    }
}
