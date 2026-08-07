using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactSendInvoiceAndCellPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CellPhone",
                table: "Contacts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendInvoice",
                table: "Contacts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Existing billing contacts should receive invoices by default.
            migrationBuilder.Sql("""
                UPDATE Contacts
                SET SendInvoice = 1
                WHERE Role = N'Billing';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CellPhone",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "SendInvoice",
                table: "Contacts");
        }
    }
}
