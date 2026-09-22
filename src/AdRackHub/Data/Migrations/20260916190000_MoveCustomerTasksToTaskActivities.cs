using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260916190000_MoveCustomerTasksToTaskActivities")]
    public partial class MoveCustomerTasksToTaskActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO dbo.CustomerNotes (CustomerId, Kind, Status, DueDate, Body, CreatedAt, CreatedBy)
                SELECT
                    t.CustomerId,
                    N'Task',
                    CASE
                        WHEN t.Status IN (N'Completed', N'Done') THEN N'Done'
                        WHEN t.Status = N'Working' THEN N'Working'
                        ELSE N'New'
                    END,
                    t.DueDate,
                    LEFT(
                        CASE
                            WHEN t.Description IS NOT NULL AND LEN(LTRIM(RTRIM(t.Description))) > 0
                                THEN t.Title + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + t.Description
                            ELSE t.Title
                        END,
                        4000),
                    t.CreatedAt,
                    t.CreatedBy
                FROM dbo.CustomerTasks t;

                DELETE FROM dbo.CustomerTasks;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO dbo.CustomerTasks (CustomerId, Title, Description, DueDate, Status, CreatedAt, CompletedAt, CreatedBy)
                SELECT
                    n.CustomerId,
                    LEFT(n.Body, 200),
                    CASE WHEN LEN(n.Body) > 200 THEN n.Body ELSE NULL END,
                    n.DueDate,
                    CASE WHEN n.Status = N'Done' THEN N'Completed' ELSE N'Open' END,
                    n.CreatedAt,
                    CASE WHEN n.Status = N'Done' THEN n.CreatedAt ELSE NULL END,
                    n.CreatedBy
                FROM dbo.CustomerNotes n
                WHERE n.Kind = N'Task';

                DELETE FROM dbo.CustomerNotes WHERE Kind = N'Task';
                """);
        }
    }
}
