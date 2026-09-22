using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStopGeoAndProspectStop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Stops",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Stops",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceId",
                table: "Stops",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stops_PlaceId",
                table: "Stops",
                column: "PlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stops_PlaceId",
                table: "Stops");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Stops");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Stops");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "Stops");
        }
    }
}
