using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SonarrImportShadow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SonarrView",
                table: "SeriesShadowPicks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SonarrViewAt",
                table: "SeriesShadowPicks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "History",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesTitle",
                table: "History",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TvdbId",
                table: "History",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FollowedSonarrs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    VersionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProfileMap = table.Column<string>(type: "TEXT", nullable: false),
                    RootFolderMap = table.Column<string>(type: "TEXT", nullable: false),
                    ImportTags = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeepMonitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    HistoryCheckpoint = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSummary = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastWebhookAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastWebhookEvent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedSonarrs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeriesExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesExclusions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SonarrGrabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FollowedSonarrId = table.Column<int>(type: "INTEGER", nullable: false),
                    SonarrHistoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    SonarrSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    VersionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GrabbedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ReleaseGuid = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    InfoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DownloadId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleaseSource = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ReleaseType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    DownloadClient = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    QualityId = table.Column<int>(type: "INTEGER", nullable: true),
                    RevisionVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseGroup = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormats = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: true),
                    FullSeason = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BigarrVerdict = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SonarrGrabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SonarrGrabs_FollowedSonarrs_FollowedSonarrId",
                        column: x => x.FollowedSonarrId,
                        principalTable: "FollowedSonarrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SonarrGrabs_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SonarrGrabs_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SonarrGrabEpisodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GrabId = table.Column<int>(type: "INTEGER", nullable: false),
                    SonarrHistoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    SonarrEpisodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsoluteEpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SonarrGrabEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SonarrGrabEpisodes_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SonarrGrabEpisodes_SonarrGrabs_GrabId",
                        column: x => x.GrabId,
                        principalTable: "SonarrGrabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_History_SeriesId",
                table: "History",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowedSonarrs_Name",
                table: "FollowedSonarrs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesExclusions_TvdbId",
                table: "SeriesExclusions",
                column: "TvdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabEpisodes_EpisodeId",
                table: "SonarrGrabEpisodes",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabEpisodes_GrabId",
                table: "SonarrGrabEpisodes",
                column: "GrabId");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabs_DownloadId",
                table: "SonarrGrabs",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabs_FollowedSonarrId_SonarrHistoryId",
                table: "SonarrGrabs",
                columns: new[] { "FollowedSonarrId", "SonarrHistoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabs_GrabbedAt",
                table: "SonarrGrabs",
                column: "GrabbedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabs_SeriesId",
                table: "SonarrGrabs",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabs_TvdbId_VersionName",
                table: "SonarrGrabs",
                columns: new[] { "TvdbId", "VersionName" });

            migrationBuilder.CreateIndex(
                name: "IX_SonarrGrabs_VersionId",
                table: "SonarrGrabs",
                column: "VersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_History_Series_SeriesId",
                table: "History",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_History_Series_SeriesId",
                table: "History");

            migrationBuilder.DropTable(
                name: "SeriesExclusions");

            migrationBuilder.DropTable(
                name: "SonarrGrabEpisodes");

            migrationBuilder.DropTable(
                name: "SonarrGrabs");

            migrationBuilder.DropTable(
                name: "FollowedSonarrs");

            migrationBuilder.DropIndex(
                name: "IX_History_SeriesId",
                table: "History");

            migrationBuilder.DropColumn(
                name: "SonarrView",
                table: "SeriesShadowPicks");

            migrationBuilder.DropColumn(
                name: "SonarrViewAt",
                table: "SeriesShadowPicks");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "History");

            migrationBuilder.DropColumn(
                name: "SeriesTitle",
                table: "History");

            migrationBuilder.DropColumn(
                name: "TvdbId",
                table: "History");
        }
    }
}
