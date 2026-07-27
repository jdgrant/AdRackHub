using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAccountManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountManagerId",
                table: "Customers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AccountManagerId",
                table: "Customers",
                column: "AccountManagerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_AspNetUsers_AccountManagerId",
                table: "Customers",
                column: "AccountManagerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Default existing accounts to Jon Grant (or the first user if not found).
            migrationBuilder.Sql("""
                DECLARE @managerId nvarchar(450);
                SELECT TOP 1 @managerId = [Id]
                FROM [AspNetUsers]
                WHERE [DisplayName] = N'Jon Grant' OR [Email] LIKE N'%jongrant%' OR [NormalizedUserName] LIKE N'%JONGRANT%'
                ORDER BY CASE WHEN [DisplayName] = N'Jon Grant' THEN 0 ELSE 1 END;

                IF @managerId IS NULL
                    SELECT TOP 1 @managerId = [Id] FROM [AspNetUsers] ORDER BY [Email];

                IF @managerId IS NOT NULL
                    UPDATE [Customers] SET [AccountManagerId] = @managerId WHERE [AccountManagerId] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_AspNetUsers_AccountManagerId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_AccountManagerId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AccountManagerId",
                table: "Customers");
        }
    }
}
