using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerNeedsMoreInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsMoreInfo",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE c
                SET NeedsMoreInfo = CASE
                    WHEN NOT (
                            (NULLIF(LTRIM(RTRIM(c.Address)), '') IS NOT NULL
                             AND NULLIF(LTRIM(RTRIM(c.City)), '') IS NOT NULL
                             AND NULLIF(LTRIM(RTRIM(c.State)), '') IS NOT NULL)
                         OR EXISTS (
                            SELECT 1 FROM Contacts ct
                            WHERE ct.CustomerId = c.Id
                              AND NULLIF(LTRIM(RTRIM(ct.Address)), '') IS NOT NULL
                              AND NULLIF(LTRIM(RTRIM(ct.City)), '') IS NOT NULL
                              AND NULLIF(LTRIM(RTRIM(ct.State)), '') IS NOT NULL
                         )
                    ) THEN 1
                    WHEN c.Latitude IS NULL OR c.Longitude IS NULL THEN 1
                    ELSE 0
                END
                FROM Customers c
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NeedsMoreInfo",
                table: "Customers");
        }
    }
}
