using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybackStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlayHistoryCheckedUntil",
                table: "MediaServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plays_MovieId_VersionId_StoppedAt",
                table: "Plays",
                columns: new[] { "MovieId", "VersionId", "StoppedAt" });

            // The decision filter of the Plays page. Outside the model: EF cannot index a complex property's column.
            migrationBuilder.CreateIndex(
                name: "IX_PlayStreams_Decision_PlayId",
                table: "PlayStreams",
                columns: new[] { "Decision", "PlayId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayStreams_Decision_PlayId",
                table: "PlayStreams");

            migrationBuilder.DropIndex(
                name: "IX_Plays_MovieId_VersionId_StoppedAt",
                table: "Plays");

            migrationBuilder.DropColumn(
                name: "PlayHistoryCheckedUntil",
                table: "MediaServers");
        }
    }
}
