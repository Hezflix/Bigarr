using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NetworkCountry",
                table: "Series",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HeldReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeIds = table.Column<string>(type: "TEXT", nullable: false),
                    Episodes = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    MagnetUrl = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    InfoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Seeders = table.Column<int>(type: "INTEGER", nullable: true),
                    Flags = table.Column<int>(type: "INTEGER", nullable: false),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Numbering = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HeldAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeldReleases_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HeldReleases_SeriesId",
                table: "HeldReleases",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_HeldReleases_VersionId_Guid",
                table: "HeldReleases",
                columns: new[] { "VersionId", "Guid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeldReleases");

            migrationBuilder.DropColumn(
                name: "NetworkCountry",
                table: "Series");
        }
    }
}
