using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261106180000_MoveEmbeddedContractToContract")]
    public partial class MoveEmbeddedContractToContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddedContract",
                table: "Customers");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedContract",
                table: "CustomerBillings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddedContract",
                table: "CustomerBillings");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedContract",
                table: "Customers",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
