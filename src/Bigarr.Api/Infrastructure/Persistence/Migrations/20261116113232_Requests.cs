using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Requests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Quota_FilmDays",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "Quota_FilmLimit",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quota_SeasonDays",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "Quota_SeasonLimit",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultQuota_FilmDays",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "DefaultQuota_FilmLimit",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultQuota_SeasonDays",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "DefaultQuota_SeasonLimit",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_MovieFourKProfileId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_MovieFourKRootFolderId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_MovieProfileId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_MovieRootFolderId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_SeriesFourKProfileId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_SeriesFourKRootFolderId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_SeriesProfileId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDefaults_SeriesRootFolderId",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    PosterUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    FourK = table.Column<bool>(type: "INTEGER", nullable: false),
                    Seasons = table.Column<string>(type: "TEXT", nullable: false),
                    AllSeasons = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedById = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedById = table.Column<int>(type: "INTEGER", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AutoApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeclineReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    AddOptions = table.Column<string>(type: "TEXT", nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    RootFolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    ForwardStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ForwardedTo = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ForwardError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ForwardedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AvailableAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaRequests_Users_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaRequests_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_DecidedById",
                table: "MediaRequests",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_MediaType_TmdbId",
                table: "MediaRequests",
                columns: new[] { "MediaType", "TmdbId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_RequestedById_CreatedAt",
                table: "MediaRequests",
                columns: new[] { "RequestedById", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_Status",
                table: "MediaRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaRequests");

            migrationBuilder.DropColumn(
                name: "Quota_FilmDays",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Quota_FilmLimit",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Quota_SeasonDays",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Quota_SeasonLimit",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DefaultQuota_FilmDays",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultQuota_FilmLimit",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultQuota_SeasonDays",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultQuota_SeasonLimit",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_MovieFourKProfileId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_MovieFourKRootFolderId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_MovieProfileId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_MovieRootFolderId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_SeriesFourKProfileId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_SeriesFourKRootFolderId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_SeriesProfileId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "RequestDefaults_SeriesRootFolderId",
                table: "InstanceSettings");
        }
    }
}
