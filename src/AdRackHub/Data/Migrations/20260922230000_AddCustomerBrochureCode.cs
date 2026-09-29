using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260922230000_AddCustomerBrochureCode")]
    public partial class AddCustomerBrochureCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrochureCode",
                table: "Customers",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BrochureCode",
                table: "Customers",
                column: "BrochureCode",
                unique: true,
                filter: "[BrochureCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_BrochureCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BrochureCode",
                table: "Customers");
        }
    }
}
