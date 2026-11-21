using AdRackHub.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261118230000_AddContractHistoryLines")]
    public class AddContractHistoryLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractHistoryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    HubName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RecordStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    MatchQuality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ContractName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Product = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RouteId = table.Column<int>(type: "int", nullable: true),
                    RouteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MonthlyAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Term = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ContractMonthlyTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Signed = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    SourceFile = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Review = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractHistoryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractHistoryLines_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ContractHistoryLines_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractHistoryLines_CustomerId",
                table: "ContractHistoryLines",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractHistoryLines_RouteId",
                table: "ContractHistoryLines",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractHistoryLines_Year_Product_RouteId",
                table: "ContractHistoryLines",
                columns: new[] { "Year", "Product", "RouteId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ContractHistoryLines");
        }
    }
}
