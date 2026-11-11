using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesDownloadImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnimeEpisodeTemplate",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}");

            migrationBuilder.AddColumn<string>(
                name: "DailyEpisodeTemplate",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "{Series Title} - {Air-Date} - {Episode Title} {Quality Full}");

            migrationBuilder.AddColumn<int>(
                name: "MultiEpisodeStyle",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5); // Sonarr's default: prefixed range

            migrationBuilder.AddColumn<bool>(
                name: "RenameEpisodes",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // like the film side's "rename files"

            migrationBuilder.AddColumn<string>(
                name: "SeasonFolderTemplate",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "Season {season}");

            migrationBuilder.AddColumn<string>(
                name: "SeriesFolderTemplate",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "{Series TitleYear}");

            migrationBuilder.AddColumn<string>(
                name: "SpecialsFolderName",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "Specials");

            migrationBuilder.AddColumn<string>(
                name: "StandardEpisodeTemplate",
                table: "MediaManagement",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}");

            migrationBuilder.AddColumn<string>(
                name: "SeriesCategory",
                table: "DownloadClients",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "bigarr-tv");

            migrationBuilder.CreateTable(
                name: "EpisodeExtraFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EpisodeFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Forced = table.Column<bool>(type: "INTEGER", nullable: false),
                    HearingImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeExtraFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodeExtraFiles_EpisodeFiles_EpisodeFileId",
                        column: x => x.EpisodeFileId,
                        principalTable: "EpisodeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeExtraFiles_EpisodeFileId",
                table: "EpisodeExtraFiles",
                column: "EpisodeFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpisodeExtraFiles");

            migrationBuilder.DropColumn(
                name: "AnimeEpisodeTemplate",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "DailyEpisodeTemplate",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "MultiEpisodeStyle",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "RenameEpisodes",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SeasonFolderTemplate",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SeriesFolderTemplate",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SpecialsFolderName",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "StandardEpisodeTemplate",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "SeriesCategory",
                table: "DownloadClients");
        }
    }
}
