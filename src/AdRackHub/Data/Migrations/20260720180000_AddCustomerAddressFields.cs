using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAddressFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Customers",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Customers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Zip",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Customers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebUrl",
                table: "Customers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Copy primary contact fields onto the customer when customer fields are empty.
            migrationBuilder.Sql("""
                UPDATE cu
                SET
                    cu.Address = COALESCE(cu.Address, c.Address),
                    cu.City = COALESCE(cu.City, c.City),
                    cu.State = COALESCE(cu.State, c.State),
                    cu.Zip = COALESCE(cu.Zip, c.Zip),
                    cu.Phone = COALESCE(cu.Phone, c.Phone),
                    cu.Email = COALESCE(cu.Email, c.Email),
                    cu.WebUrl = COALESCE(cu.WebUrl, c.WebUrl)
                FROM Customers AS cu
                INNER JOIN (
                    SELECT CustomerId, Address, City, State, Zip, Phone, Email, WebUrl,
                           ROW_NUMBER() OVER (
                               PARTITION BY CustomerId
                               ORDER BY CASE WHEN Role = N'Primary' THEN 0 ELSE 1 END, Id
                           ) AS rn
                    FROM Contacts
                ) AS c ON c.CustomerId = cu.Id AND c.rn = 1
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Address", table: "Customers");
            migrationBuilder.DropColumn(name: "City", table: "Customers");
            migrationBuilder.DropColumn(name: "State", table: "Customers");
            migrationBuilder.DropColumn(name: "Zip", table: "Customers");
            migrationBuilder.DropColumn(name: "Phone", table: "Customers");
            migrationBuilder.DropColumn(name: "Email", table: "Customers");
            migrationBuilder.DropColumn(name: "WebUrl", table: "Customers");
        }
    }
}
