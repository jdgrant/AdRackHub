using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260916210000_AddContractWaveInvoice")]
    public partial class AddContractWaveInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WaveInvoiceId",
                table: "CustomerBillings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaveInvoiceNumber",
                table: "CustomerBillings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaveInvoicePdfPath",
                table: "CustomerBillings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaveInvoiceId",
                table: "CustomerBillings");

            migrationBuilder.DropColumn(
                name: "WaveInvoiceNumber",
                table: "CustomerBillings");

            migrationBuilder.DropColumn(
                name: "WaveInvoicePdfPath",
                table: "CustomerBillings");
        }
    }
}
