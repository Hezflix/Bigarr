using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Episodes",
                table: "ReleaseSightings",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TvdbId",
                table: "ReleaseSightings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnimeCategories",
                table: "Indexers",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "TvCategories",
                table: "Indexers",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "SeriesShadowPicks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    VersionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PickedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastPickedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Picks = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ReleaseGuid = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    InfoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Seeders = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexerFlags = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    QualityId = table.Column<int>(type: "INTEGER", nullable: true),
                    RevisionVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RevisionReal = table.Column<int>(type: "INTEGER", nullable: true),
                    IsRepack = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleaseGroup = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormats = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    FullSeason = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesShadowPicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesShadowPicks_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SeriesShadowPicks_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SeriesShadowPicks_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SeriesShadowPickEpisodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PickId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsoluteEpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesShadowPickEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesShadowPickEpisodes_AcquisitionTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "AcquisitionTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SeriesShadowPickEpisodes_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SeriesShadowPickEpisodes_SeriesShadowPicks_PickId",
                        column: x => x.PickId,
                        principalTable: "SeriesShadowPicks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPickEpisodes_EpisodeId",
                table: "SeriesShadowPickEpisodes",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPickEpisodes_PickId",
                table: "SeriesShadowPickEpisodes",
                column: "PickId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPickEpisodes_TargetId",
                table: "SeriesShadowPickEpisodes",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPicks_IndexerId",
                table: "SeriesShadowPicks",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPicks_PickedAt",
                table: "SeriesShadowPicks",
                column: "PickedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPicks_SeriesId",
                table: "SeriesShadowPicks",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPicks_TvdbId_VersionName",
                table: "SeriesShadowPicks",
                columns: new[] { "TvdbId", "VersionName" });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesShadowPicks_VersionId_ReleaseGuid",
                table: "SeriesShadowPicks",
                columns: new[] { "VersionId", "ReleaseGuid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeriesShadowPickEpisodes");

            migrationBuilder.DropTable(
                name: "SeriesShadowPicks");

            migrationBuilder.DropColumn(
                name: "Episodes",
                table: "ReleaseSightings");

            migrationBuilder.DropColumn(
                name: "TvdbId",
                table: "ReleaseSightings");

            migrationBuilder.DropColumn(
                name: "AnimeCategories",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "TvCategories",
                table: "Indexers");
        }
    }
}
