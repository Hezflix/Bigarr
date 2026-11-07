using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShadowMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FollowedRadarrs",
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
                    IncludeMoviesWithoutFile = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_FollowedRadarrs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseSightings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    InfoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    UnmatchedReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AttributedVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Verdicts = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseSightings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShadowPicks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
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
                    ReleaseGroup = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormats = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    RadarrView = table.Column<string>(type: "TEXT", nullable: true),
                    RadarrViewAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShadowPicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShadowPicks_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShadowPicks_MovieVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MovieVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShadowPicks_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RadarrGrabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FollowedRadarrId = table.Column<int>(type: "INTEGER", nullable: false),
                    RadarrHistoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    RadarrMovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
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
                    DownloadClient = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    QualityId = table.Column<int>(type: "INTEGER", nullable: true),
                    RevisionVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseGroup = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormats = table.Column<string>(type: "TEXT", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BigarrVerdict = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarrGrabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadarrGrabs_FollowedRadarrs_FollowedRadarrId",
                        column: x => x.FollowedRadarrId,
                        principalTable: "FollowedRadarrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RadarrGrabs_MovieVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MovieVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RadarrGrabs_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowedRadarrs_Name",
                table: "FollowedRadarrs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadarrGrabs_DownloadId",
                table: "RadarrGrabs",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_RadarrGrabs_FollowedRadarrId_RadarrHistoryId",
                table: "RadarrGrabs",
                columns: new[] { "FollowedRadarrId", "RadarrHistoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadarrGrabs_GrabbedAt",
                table: "RadarrGrabs",
                column: "GrabbedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RadarrGrabs_MovieId",
                table: "RadarrGrabs",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_RadarrGrabs_TmdbId_VersionName",
                table: "RadarrGrabs",
                columns: new[] { "TmdbId", "VersionName" });

            migrationBuilder.CreateIndex(
                name: "IX_RadarrGrabs_VersionId",
                table: "RadarrGrabs",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseSightings_Guid",
                table: "ReleaseSightings",
                column: "Guid");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseSightings_IndexerName_Guid",
                table: "ReleaseSightings",
                columns: new[] { "IndexerName", "Guid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseSightings_InfoHash",
                table: "ReleaseSightings",
                column: "InfoHash");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseSightings_Title",
                table: "ReleaseSightings",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_ShadowPicks_IndexerId",
                table: "ShadowPicks",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_ShadowPicks_MovieId",
                table: "ShadowPicks",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_ShadowPicks_PickedAt",
                table: "ShadowPicks",
                column: "PickedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShadowPicks_TmdbId_VersionName",
                table: "ShadowPicks",
                columns: new[] { "TmdbId", "VersionName" });

            migrationBuilder.CreateIndex(
                name: "IX_ShadowPicks_VersionId_ReleaseGuid",
                table: "ShadowPicks",
                columns: new[] { "VersionId", "ReleaseGuid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RadarrGrabs");

            migrationBuilder.DropTable(
                name: "ReleaseSightings");

            migrationBuilder.DropTable(
                name: "ShadowPicks");

            migrationBuilder.DropTable(
                name: "FollowedRadarrs");
        }
    }
}
