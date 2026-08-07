using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdRackHub.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameRestAreaStopNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Match by official rest-area number in the current name (e.g. "Rest Area #22").
            void Rename(int number, string name) =>
                migrationBuilder.Sql($"""
                    UPDATE [Stops]
                    SET [StopName] = N'{name.Replace("'", "''")}'
                    WHERE [StopName] = N'Rest Area #{number}'
                       OR [StopName] = N'{number}'
                       OR [StopName] LIKE N'Rest Area #{number} %'
                       OR [StopName] LIKE N'{number} – %'
                       OR [StopName] LIKE N'{number} - %';
                    """);

            Rename(1, "1 – Powell Co / Mtn Pkwy/MM 33 near Slade");
            Rename(5, "5 - Clark Co / I-64 EB/MM 98 near Winchester");
            Rename(7, "7 - Rowan Co / I-64 EB/MM 141 near Morehead");
            Rename(8, "8 - Rowan Co / I-64 WB/MM 141 near Morehead");
            Rename(9, "9 - Carter Co / I-64 EB/MM 174 near Grayson");
            Rename(3, "3 - Woodford Co / I-64 EB/MM 60 near Midway");
            Rename(4, "4 - Woodford Co / I-64 WB/MM 60 near Midway");
            Rename(17, "17 - Oldham Co / I-71 NB/MM 13 near Crestwood");
            Rename(18, "18 - Oldham Co / I-71 SB/MM 13 near Crestwood");
            Rename(22, "22 - Scott Co / I-75 NB/MM 127 near Georgetown");
            Rename(23, "23 - Scott Co / I-75 SB/MM 127 near Georgetown");
            Rename(24, "24 - Boone Co / I-75 NB/MM 176 near Walton/Florence");
            Rename(25, "25 - Boone Co / I-75 SB/MM 176 near Walton/Florence");
            Rename(15, "15 - Hart Co / I-65 NB/MM 60 near Munfordville");
            Rename(16, "16 - Hart Co / I-65 SB/MM 60 near Munfordville");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            void Revert(int number) =>
                migrationBuilder.Sql($"""
                    UPDATE [Stops]
                    SET [StopName] = N'Rest Area #{number}'
                    WHERE [StopName] LIKE N'{number} – %'
                       OR [StopName] LIKE N'{number} - %';
                    """);

            foreach (var number in new[] { 1, 3, 4, 5, 7, 8, 9, 15, 16, 17, 18, 22, 23, 24, 25 })
                Revert(number);
        }
    }
}
