using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRouteBillingTerm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingTerm",
                table: "CustomerRoutes",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Quarterly");

            migrationBuilder.Sql("""
                UPDATE cr
                SET BillingTerm = COALESCE(
                    (SELECT TOP 1 b.Term FROM CustomerBillings b WHERE b.CustomerId = cr.CustomerId),
                    r.BillingFrequency,
                    'Quarterly')
                FROM CustomerRoutes cr
                INNER JOIN Routes r ON cr.RouteId = r.Id
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingTerm",
                table: "CustomerRoutes");
        }
    }
}
