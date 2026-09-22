using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260916230000_MoveInvoiceReceiptMethodToCustomer")]
    public partial class MoveInvoiceReceiptMethodToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceReceiptMethod",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Mail");

            migrationBuilder.Sql(
                """
                UPDATE [Customers]
                SET [InvoiceReceiptMethod] = N'Mail'
                """);

            migrationBuilder.DropColumn(
                name: "InvoiceReceiptMethod",
                table: "CustomerBillings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceReceiptMethod",
                table: "CustomerBillings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Mail");

            migrationBuilder.Sql(
                """
                UPDATE [CustomerBillings]
                SET [InvoiceReceiptMethod] = N'Mail'
                """);

            migrationBuilder.DropColumn(
                name: "InvoiceReceiptMethod",
                table: "Customers");
        }
    }
}
