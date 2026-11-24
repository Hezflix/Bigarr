using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewEpisodeNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordBotToken",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_BatchContent",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_BatchLine",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_BatchMore",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_BatchTitle",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_Content",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_LinkName",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewEpisodes_LinkTarget",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_LinkText",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_PlexWebAddress",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewEpisodes_Title",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NewEpisodeAnnouncements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlexCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReadyAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlexWait = table.Column<int>(type: "INTEGER", nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewEpisodeAnnouncements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeriesNotificationOptOuts",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesNotificationOptOuts", x => new { x.UserId, x.SeriesId });
                    table.ForeignKey(
                        name: "FK_SeriesNotificationOptOuts_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesNotificationOptOuts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewEpisodeAnnouncementEpisodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnnouncementId = table.Column<long>(type: "INTEGER", nullable: false),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Overview = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewEpisodeAnnouncementEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewEpisodeAnnouncementEpisodes_NewEpisodeAnnouncements_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "NewEpisodeAnnouncements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewEpisodeMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnnouncementId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscordUserId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewEpisodeMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewEpisodeMessages_NewEpisodeAnnouncements_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "NewEpisodeAnnouncements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewEpisodeAnnouncementEpisodes_AnnouncementId",
                table: "NewEpisodeAnnouncementEpisodes",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "IX_NewEpisodeAnnouncementEpisodes_EpisodeId",
                table: "NewEpisodeAnnouncementEpisodes",
                column: "EpisodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewEpisodeAnnouncements_ReadyAt_SeriesId",
                table: "NewEpisodeAnnouncements",
                columns: new[] { "ReadyAt", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewEpisodeMessages_AnnouncementId_UserId",
                table: "NewEpisodeMessages",
                columns: new[] { "AnnouncementId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewEpisodeMessages_DeliveredAt_NextAttemptAt",
                table: "NewEpisodeMessages",
                columns: new[] { "DeliveredAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesNotificationOptOuts_SeriesId",
                table: "SeriesNotificationOptOuts",
                column: "SeriesId");

            // "New episode" is on by default: users who already chose their mentions get it too, and turn it off on /profile.
            migrationBuilder.Sql("""
                UPDATE "Users" SET "DiscordMentions" = json_insert("DiscordMentions", '$[#]', 'newEpisode')
                WHERE "DiscordMentions" IS NOT NULL AND json_valid("DiscordMentions")
                  AND NOT EXISTS (SELECT 1 FROM json_each("Users"."DiscordMentions") WHERE value = 'newEpisode');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewEpisodeAnnouncementEpisodes");

            migrationBuilder.DropTable(
                name: "NewEpisodeMessages");

            migrationBuilder.DropTable(
                name: "SeriesNotificationOptOuts");

            migrationBuilder.DropTable(
                name: "NewEpisodeAnnouncements");

            migrationBuilder.DropColumn(
                name: "DiscordBotToken",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_BatchContent",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_BatchLine",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_BatchMore",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_BatchTitle",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_Content",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_LinkName",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_LinkTarget",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_LinkText",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_PlexWebAddress",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "NewEpisodes_Title",
                table: "InstanceSettings");
        }
    }
}
