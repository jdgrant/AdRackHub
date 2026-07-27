using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyBillingRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillingAnchorMonth",
                table: "CustomerBillings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BillingRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingRunInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BillingRunId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    WaveCustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WaveInvoiceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WaveInvoiceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingRunInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingRunInvoices_BillingRuns_BillingRunId",
                        column: x => x.BillingRunId,
                        principalTable: "BillingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillingRunInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingRunInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BillingRunInvoiceId = table.Column<int>(type: "int", nullable: false),
                    CustomerBillingId = table.Column<int>(type: "int", nullable: false),
                    BillName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Term = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RouteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingRunInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingRunInvoiceLines_BillingRunInvoices_BillingRunInvoiceId",
                        column: x => x.BillingRunInvoiceId,
                        principalTable: "BillingRunInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingRunInvoiceLines_BillingRunInvoiceId",
                table: "BillingRunInvoiceLines",
                column: "BillingRunInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingRunInvoices_BillingRunId_CustomerId",
                table: "BillingRunInvoices",
                columns: new[] { "BillingRunId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingRunInvoices_CustomerId",
                table: "BillingRunInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingRuns_Year_Month",
                table: "BillingRuns",
                columns: new[] { "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingRunInvoiceLines");

            migrationBuilder.DropTable(
                name: "BillingRunInvoices");

            migrationBuilder.DropTable(
                name: "BillingRuns");

            migrationBuilder.DropColumn(
                name: "BillingAnchorMonth",
                table: "CustomerBillings");
        }
    }
}
