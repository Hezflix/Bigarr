using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybackUserNeverSeenRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Its own migration: SQLite rebuilds the table for the nullable column at the end of the previous one.
            migrationBuilder.Sql(Features.Playback.Persistence.NeverSeenUsers.RepairSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the previous migration's Down dates everyone again.
        }
    }
}
