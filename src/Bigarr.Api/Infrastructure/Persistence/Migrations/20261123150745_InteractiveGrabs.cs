using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InteractiveGrabs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Interactive",
                table: "Grabs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Grabs made before the column: the journal's Grabbed entry (EventType 1) kept who decided.
            migrationBuilder.Sql("""
                UPDATE Grabs SET Interactive = 1
                WHERE DownloadId IS NOT NULL AND EXISTS (
                    SELECT 1 FROM History h
                    WHERE h.EventType = 1
                      AND json_extract(h.Data, '$.trigger') = 'interactive'
                      AND json_extract(h.Data, '$.downloadId') = Grabs.DownloadId COLLATE NOCASE);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Interactive",
                table: "Grabs");
        }
    }
}
