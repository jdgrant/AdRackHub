using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerBillings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    BillName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Term = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBillings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBillings_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBillingRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerBillingId = table.Column<int>(type: "int", nullable: false),
                    RouteId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBillingRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBillingRoutes_CustomerBillings_CustomerBillingId",
                        column: x => x.CustomerBillingId,
                        principalTable: "CustomerBillings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerBillingRoutes_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillingRoutes_CustomerBillingId_RouteId",
                table: "CustomerBillingRoutes",
                columns: new[] { "CustomerBillingId", "RouteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillingRoutes_RouteId",
                table: "CustomerBillingRoutes",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillings_CustomerId_BillName",
                table: "CustomerBillings",
                columns: new[] { "CustomerId", "BillName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerBillingRoutes");

            migrationBuilder.DropTable(
                name: "CustomerBillings");
        }
    }
}
