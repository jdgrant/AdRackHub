using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppendRestAreaToRouteNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE r
                SET [RouteName] = r.[RouteName] + N' Rest Area'
                FROM [Routes] AS r
                WHERE EXISTS (
                    SELECT 1
                    FROM [Stops] AS s
                    WHERE s.[RouteId] = r.[Id]
                      AND (
                          s.[StopType] = N'RestArea'
                          OR s.[StopName] LIKE N'%Rest Area%'
                      )
                )
                AND r.[RouteName] NOT LIKE N'%Rest Area%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Routes]
                SET [RouteName] = LEFT([RouteName], LEN([RouteName]) - LEN(N' Rest Area'))
                WHERE [RouteName] LIKE N'% Rest Area'
                  AND [RouteName] NOT LIKE N'%Rest Areas';
                """);
        }
    }
}
