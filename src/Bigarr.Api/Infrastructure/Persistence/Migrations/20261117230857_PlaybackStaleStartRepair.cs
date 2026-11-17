using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybackStaleStartRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingRepairs",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingRepairs", x => x.Name);
                });

            // Queues the repair of plays that opened on a stale position: a startup task does it and removes the row.
            migrationBuilder.Sql($"INSERT INTO \"PendingRepairs\" (\"Name\") VALUES ('{Features.Playback.Persistence.StaleStartRepair.Name}');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingRepairs");
        }
    }
}
