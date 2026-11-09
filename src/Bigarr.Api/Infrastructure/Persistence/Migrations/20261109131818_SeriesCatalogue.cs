using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Runtime = table.Column<int>(type: "INTEGER", nullable: true),
                    AirTime = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    OriginalCountry = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    OriginalLanguage = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    SeriesType = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordering = table.Column<int>(type: "INTEGER", nullable: false),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    AlternativeTitles = table.Column<string>(type: "TEXT", nullable: false),
                    Images = table.Column<string>(type: "TEXT", nullable: false),
                    FirstAired = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastAired = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DefaultRootFolderId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastMetadataRefreshAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_RootFolders_DefaultRootFolderId",
                        column: x => x.DefaultRootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    Images = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesTags",
                columns: table => new
                {
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesTags", x => new { x.SeriesId, x.TagId });
                    table.ForeignKey(
                        name: "FK_SeriesTags_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Constraints = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeasonFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonitorNewItems = table.Column<int>(type: "INTEGER", nullable: false),
                    RootFolderId = table.Column<int>(type: "INTEGER", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesVersions_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeriesVersions_RootFolders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeriesVersions_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsoluteEpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    AirDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AirDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    Runtime = table.Column<int>(type: "INTEGER", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Episodes_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EpisodeFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QualityId = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionReal = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRepack = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleaseGroup = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    IndexerFlags = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MediaInfo = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodeFiles_QualityDefinitions_QualityId",
                        column: x => x.QualityId,
                        principalTable: "QualityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EpisodeFiles_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesVersionSeasons",
                columns: table => new
                {
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesVersionSeasons", x => new { x.VersionId, x.SeasonId });
                    table.ForeignKey(
                        name: "FK_SeriesVersionSeasons_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesVersionSeasons_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesVersionTags",
                columns: table => new
                {
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesVersionTags", x => new { x.VersionId, x.TagId });
                    table.ForeignKey(
                        name: "FK_SeriesVersionTags_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesVersionTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EpisodeTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeFileId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodeTargets_AcquisitionTargets_Id",
                        column: x => x.Id,
                        principalTable: "AcquisitionTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EpisodeTargets_EpisodeFiles_EpisodeFileId",
                        column: x => x.EpisodeFileId,
                        principalTable: "EpisodeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EpisodeTargets_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EpisodeTargets_SeriesVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "SeriesVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeFiles_QualityId",
                table: "EpisodeFiles",
                column: "QualityId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeFiles_VersionId_RelativePath",
                table: "EpisodeFiles",
                columns: new[] { "VersionId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeasonId",
                table: "Episodes",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_AbsoluteEpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesId", "AbsoluteEpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_AirDate",
                table: "Episodes",
                columns: new[] { "SeriesId", "AirDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesId", "SeasonNumber", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_TmdbId",
                table: "Episodes",
                column: "TmdbId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_TvdbId",
                table: "Episodes",
                column: "TvdbId",
                unique: true,
                filter: "\"TvdbId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeTargets_EpisodeFileId",
                table: "EpisodeTargets",
                column: "EpisodeFileId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeTargets_EpisodeId",
                table: "EpisodeTargets",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeTargets_VersionId_EpisodeId",
                table: "EpisodeTargets",
                columns: new[] { "VersionId", "EpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_SeriesId_SeasonNumber",
                table: "Seasons",
                columns: new[] { "SeriesId", "SeasonNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_DefaultRootFolderId",
                table: "Series",
                column: "DefaultRootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_SortTitle",
                table: "Series",
                column: "SortTitle");

            migrationBuilder.CreateIndex(
                name: "IX_Series_TmdbId",
                table: "Series",
                column: "TmdbId",
                unique: true,
                filter: "\"TmdbId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Series_TvdbId",
                table: "Series",
                column: "TvdbId",
                unique: true,
                filter: "\"TvdbId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesTags_TagId",
                table: "SeriesTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesVersions_QualityProfileId",
                table: "SeriesVersions",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesVersions_RootFolderId",
                table: "SeriesVersions",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesVersions_SeriesId_IsDefault",
                table: "SeriesVersions",
                column: "SeriesId",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesVersions_SeriesId_Name",
                table: "SeriesVersions",
                columns: new[] { "SeriesId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesVersionSeasons_SeasonId",
                table: "SeriesVersionSeasons",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesVersionTags_TagId",
                table: "SeriesVersionTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpisodeTargets");

            migrationBuilder.DropTable(
                name: "SeriesTags");

            migrationBuilder.DropTable(
                name: "SeriesVersionSeasons");

            migrationBuilder.DropTable(
                name: "SeriesVersionTags");

            migrationBuilder.DropTable(
                name: "EpisodeFiles");

            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "SeriesVersions");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Series");
        }
    }
}
