using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesSpecialsAsFilms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAddSeriesFilms",
                table: "MediaManagement",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AirsAfterSeason",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AirsBeforeEpisode",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AirsBeforeSeason",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMovie",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MovieCheckedAt",
                table: "Episodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MovieDismissedAt",
                table: "Episodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovieImdbId",
                table: "Episodes",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MovieMatch",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MovieTitle",
                table: "Episodes",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MovieTmdbId",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MovieYear",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedFromPath",
                table: "EpisodeFiles",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkedMediaFileId",
                table: "EpisodeFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesMovieLinkId",
                table: "EpisodeFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SeriesMovieLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    AfterSeason = table.Column<int>(type: "INTEGER", nullable: true),
                    BeforeSeason = table.Column<int>(type: "INTEGER", nullable: true),
                    PlacementSource = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoAdded = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LinkError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LinkErrorAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesMovieLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesMovieLinks_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesMovieLinks_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesMovieLinks_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeFiles_SeriesMovieLinkId",
                table: "EpisodeFiles",
                column: "SeriesMovieLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesMovieLinks_EpisodeId",
                table: "SeriesMovieLinks",
                column: "EpisodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesMovieLinks_MovieId",
                table: "SeriesMovieLinks",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesMovieLinks_SeriesId",
                table: "SeriesMovieLinks",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_EpisodeFiles_SeriesMovieLinks_SeriesMovieLinkId",
                table: "EpisodeFiles",
                column: "SeriesMovieLinkId",
                principalTable: "SeriesMovieLinks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EpisodeFiles_SeriesMovieLinks_SeriesMovieLinkId",
                table: "EpisodeFiles");

            migrationBuilder.DropTable(
                name: "SeriesMovieLinks");

            migrationBuilder.DropIndex(
                name: "IX_EpisodeFiles_SeriesMovieLinkId",
                table: "EpisodeFiles");

            migrationBuilder.DropColumn(
                name: "AutoAddSeriesFilms",
                table: "MediaManagement");

            migrationBuilder.DropColumn(
                name: "AirsAfterSeason",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "AirsBeforeEpisode",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "AirsBeforeSeason",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "IsMovie",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "MovieCheckedAt",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "MovieDismissedAt",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "MovieImdbId",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "MovieMatch",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "MovieTitle",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "MovieTmdbId",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "MovieYear",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "LinkedFromPath",
                table: "EpisodeFiles");

            migrationBuilder.DropColumn(
                name: "LinkedMediaFileId",
                table: "EpisodeFiles");

            migrationBuilder.DropColumn(
                name: "SeriesMovieLinkId",
                table: "EpisodeFiles");
        }
    }
}
