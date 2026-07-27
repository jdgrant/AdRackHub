using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactWebUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebUrl",
                table: "Contacts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Backfill from KY brochure import notes: "Website: https://..."
            migrationBuilder.Sql("""
                UPDATE c
                SET c.WebUrl = LTRIM(RTRIM(SUBSTRING(n.Body, CHARINDEX(':', n.Body) + 1, 500)))
                FROM Contacts AS c
                INNER JOIN (
                    SELECT CustomerId, Body,
                           ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY Id DESC) AS rn
                    FROM CustomerNotes
                    WHERE Body LIKE 'Website:%'
                ) AS n ON n.CustomerId = c.CustomerId AND n.rn = 1
                WHERE c.WebUrl IS NULL
                  AND c.Role = N'Primary'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebUrl",
                table: "Contacts");
        }
    }
}
