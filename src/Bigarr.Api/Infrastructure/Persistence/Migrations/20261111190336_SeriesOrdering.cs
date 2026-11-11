using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Numbering",
                table: "SeriesShadowPicks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EpisodeGroupId",
                table: "Series",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EpisodeGroupName",
                table: "Series",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EpisodeGroupType",
                table: "Series",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderOnlyEpisodes",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "Numbering",
                table: "Grabs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderAbsoluteNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderEpisodeNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderSeasonNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SeriesOrderChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    From = table.Column<string>(type: "TEXT", nullable: false),
                    To = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UndoneAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreviousNumbers = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousOrderOnly = table.Column<string>(type: "TEXT", nullable: false),
                    Moves = table.Column<string>(type: "TEXT", nullable: false),
                    UnmatchedFiles = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesOrderChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesOrderChanges_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_OrderSeasonNumber_OrderEpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesId", "OrderSeasonNumber", "OrderEpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesOrderChanges_SeriesId_AppliedAt",
                table: "SeriesOrderChanges",
                columns: new[] { "SeriesId", "AppliedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeriesOrderChanges");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesId_OrderSeasonNumber_OrderEpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Numbering",
                table: "SeriesShadowPicks");

            migrationBuilder.DropColumn(
                name: "EpisodeGroupId",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "EpisodeGroupName",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "EpisodeGroupType",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "OrderOnlyEpisodes",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Numbering",
                table: "Grabs");

            migrationBuilder.DropColumn(
                name: "OrderAbsoluteNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "OrderEpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "OrderSeasonNumber",
                table: "Episodes");
        }
    }
}
