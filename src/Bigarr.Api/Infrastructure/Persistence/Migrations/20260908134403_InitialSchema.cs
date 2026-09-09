using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcquisitionTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSearchAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomFormats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IncludeWhenRenaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    SpecificationsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Grabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DownloadClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    DownloadClientName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DownloadId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ReleaseGuid = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    InfoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexerFlags = table.Column<int>(type: "INTEGER", nullable: false),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false),
                    GrabbedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusMessages = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grabs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Resolution = table.Column<int>(type: "INTEGER", nullable: false),
                    Modifier = table.Column<int>(type: "INTEGER", nullable: false),
                    MinSize = table.Column<double>(type: "REAL", nullable: true),
                    PreferredSize = table.Column<double>(type: "REAL", nullable: true),
                    MaxSize = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Items = table.Column<string>(type: "TEXT", nullable: false),
                    Cutoff = table.Column<int>(type: "INTEGER", nullable: false),
                    UpgradeAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CutoffFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    MinUpgradeFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RootFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RootFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Blocklist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseGuid = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    InfoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocklist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocklist_AcquisitionTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "AcquisitionTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrabTargets",
                columns: table => new
                {
                    GrabId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrabTargets", x => new { x.GrabId, x.TargetId });
                    table.ForeignKey(
                        name: "FK_GrabTargets_AcquisitionTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "AcquisitionTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GrabTargets_Grabs_GrabId",
                        column: x => x.GrabId,
                        principalTable: "Grabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileFormatScores",
                columns: table => new
                {
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomFormatId = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileFormatScores", x => new { x.QualityProfileId, x.CustomFormatId });
                    table.ForeignKey(
                        name: "FK_ProfileFormatScores_CustomFormats_CustomFormatId",
                        column: x => x.CustomFormatId,
                        principalTable: "CustomFormats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileFormatScores_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Runtime = table.Column<int>(type: "INTEGER", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    Certification = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    OriginalLanguage = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    Ratings = table.Column<string>(type: "TEXT", nullable: true),
                    AlternativeTitles = table.Column<string>(type: "TEXT", nullable: false),
                    TmdbStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    InCinemasDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DigitalReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PhysicalReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CollectionTmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    Images = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultRootFolderId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastMetadataRefreshAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movies_RootFolders_DefaultRootFolderId",
                        column: x => x.DefaultRootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CompatView = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieTags",
                columns: table => new
                {
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieTags", x => new { x.MovieId, x.TagId });
                    table.ForeignKey(
                        name: "FK_MovieTags_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Constraints = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    RootFolderId = table.Column<int>(type: "INTEGER", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MinimumAvailability = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovieVersions_AcquisitionTargets_Id",
                        column: x => x.Id,
                        principalTable: "AcquisitionTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieVersions_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieVersions_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovieVersions_RootFolders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QualityId = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionReal = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRepack = table.Column<bool>(type: "INTEGER", nullable: false),
                    Edition = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ReleaseGroup = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    IndexerFlags = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MediaInfo = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaFiles_MovieVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MovieVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaFiles_QualityDefinitions_QualityId",
                        column: x => x.QualityId,
                        principalTable: "QualityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovieVersionTags",
                columns: table => new
                {
                    VersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieVersionTags", x => new { x.VersionId, x.TagId });
                    table.ForeignKey(
                        name: "FK_MovieVersionTags_MovieVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MovieVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieVersionTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "History",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    VersionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MediaFileId = table.Column<int>(type: "INTEGER", nullable: true),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                    table.ForeignKey(
                        name: "FK_History_AcquisitionTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "AcquisitionTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_History_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_History_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionTargets_Kind_Monitored",
                table: "AcquisitionTargets",
                columns: new[] { "Kind", "Monitored" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_InfoHash",
                table: "Blocklist",
                column: "InfoHash");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_TargetId_ReleaseGuid",
                table: "Blocklist",
                columns: new[] { "TargetId", "ReleaseGuid" },
                unique: true,
                filter: "\"ReleaseGuid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_TargetId_SourceTitle",
                table: "Blocklist",
                columns: new[] { "TargetId", "SourceTitle" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFormats_Name",
                table: "CustomFormats",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrabTargets_TargetId",
                table: "GrabTargets",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_GrabTargets_TargetId_Active",
                table: "GrabTargets",
                column: "TargetId",
                unique: true,
                filter: "\"Active\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Grabs_DownloadId",
                table: "Grabs",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_Grabs_State",
                table: "Grabs",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_History_CreatedAt",
                table: "History",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_History_EventType",
                table: "History",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_History_MediaFileId",
                table: "History",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_History_MovieId",
                table: "History",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_History_TargetId",
                table: "History",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_QualityId",
                table: "MediaFiles",
                column: "QualityId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_VersionId",
                table: "MediaFiles",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieTags_TagId",
                table: "MovieTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_MovieVersionTags_TagId",
                table: "MovieVersionTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_MovieVersions_MovieId_IsDefault",
                table: "MovieVersions",
                column: "MovieId",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MovieVersions_MovieId_Name",
                table: "MovieVersions",
                columns: new[] { "MovieId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieVersions_QualityProfileId",
                table: "MovieVersions",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MovieVersions_RootFolderId",
                table: "MovieVersions",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_DefaultRootFolderId",
                table: "Movies",
                column: "DefaultRootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_ImdbId",
                table: "Movies",
                column: "ImdbId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_SortTitle",
                table: "Movies",
                column: "SortTitle");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TmdbId",
                table: "Movies",
                column: "TmdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileFormatScores_CustomFormatId",
                table: "ProfileFormatScores",
                column: "CustomFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityDefinitions_Name",
                table: "QualityDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_Name",
                table: "QualityProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RootFolders_Path",
                table: "RootFolders",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Label",
                table: "Tags",
                column: "Label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "Blocklist");

            migrationBuilder.DropTable(
                name: "GrabTargets");

            migrationBuilder.DropTable(
                name: "History");

            migrationBuilder.DropTable(
                name: "MovieTags");

            migrationBuilder.DropTable(
                name: "MovieVersionTags");

            migrationBuilder.DropTable(
                name: "ProfileFormatScores");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Grabs");

            migrationBuilder.DropTable(
                name: "MediaFiles");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "CustomFormats");

            migrationBuilder.DropTable(
                name: "MovieVersions");

            migrationBuilder.DropTable(
                name: "QualityDefinitions");

            migrationBuilder.DropTable(
                name: "AcquisitionTargets");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "QualityProfiles");

            migrationBuilder.DropTable(
                name: "RootFolders");
        }
    }
}
