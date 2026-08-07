using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleBillingInvoicesPerCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingRunInvoices_BillingRunId_CustomerId",
                table: "BillingRunInvoices");

            migrationBuilder.CreateIndex(
                name: "IX_BillingRunInvoices_BillingRunId_CustomerId",
                table: "BillingRunInvoices",
                columns: new[] { "BillingRunId", "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingRunInvoices_BillingRunId_CustomerId",
                table: "BillingRunInvoices");

            migrationBuilder.CreateIndex(
                name: "IX_BillingRunInvoices_BillingRunId_CustomerId",
                table: "BillingRunInvoices",
                columns: new[] { "BillingRunId", "CustomerId" },
                unique: true);
        }
    }
}
