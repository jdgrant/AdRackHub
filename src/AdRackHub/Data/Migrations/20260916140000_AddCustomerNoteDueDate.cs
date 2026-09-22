using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260916140000_AddCustomerNoteDueDate")]
    public partial class AddCustomerNoteDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "CustomerNotes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotes_Status_DueDate",
                table: "CustomerNotes",
                columns: new[] { "Status", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerNotes_Status_DueDate",
                table: "CustomerNotes");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "CustomerNotes");
        }
    }
}
