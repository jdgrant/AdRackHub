using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStopRouteFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HighwayExit",
                table: "Stops",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Stops",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RackPlacement",
                table: "Stops",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StepNumber",
                table: "Stops",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stops_RouteId_StepNumber",
                table: "Stops",
                columns: new[] { "RouteId", "StepNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stops_RouteId_StepNumber",
                table: "Stops");

            migrationBuilder.DropColumn(
                name: "HighwayExit",
                table: "Stops");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Stops");

            migrationBuilder.DropColumn(
                name: "RackPlacement",
                table: "Stops");

            migrationBuilder.DropColumn(
                name: "StepNumber",
                table: "Stops");
        }
    }
}
