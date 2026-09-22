using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerExpandedProspectRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpandedProspectRouteId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ExpandedProspectRouteId",
                table: "Customers",
                column: "ExpandedProspectRouteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Routes_ExpandedProspectRouteId",
                table: "Customers",
                column: "ExpandedProspectRouteId",
                principalTable: "Routes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Routes_ExpandedProspectRouteId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_ExpandedProspectRouteId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ExpandedProspectRouteId",
                table: "Customers");
        }
    }
}
