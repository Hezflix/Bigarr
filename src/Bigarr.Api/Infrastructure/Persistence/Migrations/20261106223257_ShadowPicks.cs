using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShadowPicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_ReleaseSightings_SeenAt",
                table: "ReleaseSightings",
                column: "SeenAt");

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
                name: "ReleaseSightings");

            migrationBuilder.DropTable(
                name: "ShadowPicks");
        }
    }
}
