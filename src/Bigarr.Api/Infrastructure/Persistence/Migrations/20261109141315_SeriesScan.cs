using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SceneNumberingRefreshedAt",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseSceneNumbering",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Every root folder that exists before series came in holds films (MediaKind.Movies = 1).
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "RootFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SceneAbsoluteEpisodeNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SceneEpisodeNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SceneSeasonNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UnverifiedSceneNumbering",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SceneNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CleanTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SceneSeasonNumber = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneNames", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnmatchedEpisodeFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnmatchedEpisodeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnmatchedEpisodeFiles_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UnmatchedEpisodeFiles_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_SceneSeasonNumber_SceneEpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesId", "SceneSeasonNumber", "SceneEpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SceneNames_CleanTitle",
                table: "SceneNames",
                column: "CleanTitle");

            migrationBuilder.CreateIndex(
                name: "IX_SceneNames_TvdbId",
                table: "SceneNames",
                column: "TvdbId");

            migrationBuilder.CreateIndex(
                name: "IX_UnmatchedEpisodeFiles_SeriesId",
                table: "UnmatchedEpisodeFiles",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_UnmatchedEpisodeFiles_VersionId_RelativePath",
                table: "UnmatchedEpisodeFiles",
                columns: new[] { "VersionId", "RelativePath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SceneNames");

            migrationBuilder.DropTable(
                name: "UnmatchedEpisodeFiles");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesId_SceneSeasonNumber_SceneEpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SceneNumberingRefreshedAt",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "UseSceneNumbering",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "RootFolders");

            migrationBuilder.DropColumn(
                name: "SceneAbsoluteEpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SceneEpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SceneSeasonNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "UnverifiedSceneNumbering",
                table: "Episodes");
        }
    }
}
