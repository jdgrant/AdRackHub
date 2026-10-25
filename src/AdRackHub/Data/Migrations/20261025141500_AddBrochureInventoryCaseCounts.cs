using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025141500_AddBrochureInventoryCaseCounts")]
    public partial class AddBrochureInventoryCaseCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Cases",
                table: "CustomerBrochureInventories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CaseCount",
                table: "CustomerBrochureInventories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "CustomerBrochureInventories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Count");

            migrationBuilder.Sql(
                """
                UPDATE CustomerBrochureInventories
                SET Kind = N'Received'
                WHERE Notes IS NOT NULL
                  AND Notes LIKE N'%receiv%';

                UPDATE i
                SET
                    Cases = TRY_CONVERT(int, LEFT(n.Normalized, NULLIF(CHARINDEX(N' cases', n.Normalized), 0) - 1)),
                    CaseCount = TRY_CONVERT(int, SUBSTRING(
                        piece.Digits,
                        1,
                        NULLIF(PATINDEX(N'%[^0-9]%', piece.Digits + N'x'), 0) - 1))
                FROM CustomerBrochureInventories i
                CROSS APPLY (
                    SELECT LOWER(REPLACE(REPLACE(i.Notes, N'×', N'x'), N'X', N'x')) AS Normalized
                ) n
                CROSS APPLY (
                    SELECT LTRIM(SUBSTRING(n.Normalized, CHARINDEX(N' x ', n.Normalized) + 3, 20)) AS Digits
                ) piece
                WHERE i.Notes IS NOT NULL
                  AND CHARINDEX(N' cases', n.Normalized) > 1
                  AND CHARINDEX(N' x ', n.Normalized) > CHARINDEX(N' cases', n.Normalized);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CustomerBrochureInventories");

            migrationBuilder.DropColumn(
                name: "CaseCount",
                table: "CustomerBrochureInventories");

            migrationBuilder.DropColumn(
                name: "Cases",
                table: "CustomerBrochureInventories");
        }
    }
}
