using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrefixRouteNamesExitAndRestArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "… Rest Areas" → "Rest Area - …"
            migrationBuilder.Sql("""
                UPDATE [Routes]
                SET [RouteName] = N'Rest Area - ' + LTRIM(RTRIM(LEFT([RouteName], LEN([RouteName]) - LEN(N' Rest Areas'))))
                WHERE [RouteName] LIKE N'% Rest Areas'
                  AND [RouteName] NOT LIKE N'Rest Area - %';
                """);

            // "… Rest Area" → "Rest Area - …"
            migrationBuilder.Sql("""
                UPDATE [Routes]
                SET [RouteName] = N'Rest Area - ' + LTRIM(RTRIM(LEFT([RouteName], LEN([RouteName]) - LEN(N' Rest Area'))))
                WHERE [RouteName] LIKE N'% Rest Area'
                  AND [RouteName] NOT LIKE N'% Rest Areas'
                  AND [RouteName] NOT LIKE N'Rest Area - %';
                """);

            // Everything else without "Rest Area" → "Exit - …"
            migrationBuilder.Sql("""
                UPDATE [Routes]
                SET [RouteName] = N'Exit - ' + [RouteName]
                WHERE [RouteName] NOT LIKE N'%Rest Area%'
                  AND [RouteName] NOT LIKE N'Exit - %';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Routes]
                SET [RouteName] = SUBSTRING([RouteName], LEN(N'Exit - ') + 1, 400)
                WHERE [RouteName] LIKE N'Exit - %';
                """);

            migrationBuilder.Sql("""
                UPDATE [Routes]
                SET [RouteName] = SUBSTRING([RouteName], LEN(N'Rest Area - ') + 1, 400) + N' Rest Area'
                WHERE [RouteName] LIKE N'Rest Area - %';
                """);
        }
    }
}
