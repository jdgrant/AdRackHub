using AdRackHub.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261026150000_AddInvoiceReminderTemplate")]
    public partial class AddInvoiceReminderTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceReminderTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceReminderTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceReminderTemplateRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ChangeKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceReminderTemplateRevisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReminderTemplateRevisions_ChangedAt",
                table: "InvoiceReminderTemplateRevisions",
                column: "ChangedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InvoiceReminderTemplateRevisions");
            migrationBuilder.DropTable(name: "InvoiceReminderTemplates");
        }
    }
}
