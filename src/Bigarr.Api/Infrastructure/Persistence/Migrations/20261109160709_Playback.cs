using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Playback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaServerItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    RatingKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    CreditsStartMs = table.Column<long>(type: "INTEGER", nullable: true),
                    LibrarySectionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LibrarySectionTitle = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ShowRatingKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ShowGuid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ShowTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ShowTmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShowTvdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShowImdbId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SeasonRatingKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaServerItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaServerItems_MediaServers_MediaServerId",
                        column: x => x.MediaServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaServerItems_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaServerUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ThumbUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    IsOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeepHistory = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaServerUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaServerUsers_MediaServers_MediaServerId",
                        column: x => x.MediaServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumPlay = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumPlayUnit = table.Column<int>(type: "INTEGER", nullable: false),
                    WatchedPercent = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ServerSessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RatingKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ViewOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PausedSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PausedSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybackSessions_MediaServerItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "MediaServerItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaybackSessions_MediaServerUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "MediaServerUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaybackSessions_MediaServers_MediaServerId",
                        column: x => x.MediaServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Plays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StoppedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlayedSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    PausedSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    Watched = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plays_MediaServerItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "MediaServerItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Plays_MediaServerUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "MediaServerUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Plays_MovieVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MovieVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Plays_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlayStreams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StoppedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    EndOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    PausedSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    VersionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExternalKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AudioDecision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Bandwidth = table.Column<int>(type: "INTEGER", nullable: true),
                    Decision = table.Column<int>(type: "INTEGER", nullable: true),
                    HardwareTranscode = table.Column<bool>(type: "INTEGER", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Local = table.Column<bool>(type: "INTEGER", nullable: true),
                    PlayerDevice = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PlayerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PlayerPlatform = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PlayerPlatformVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PlayerProduct = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PlayerTitle = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Relayed = table.Column<bool>(type: "INTEGER", nullable: true),
                    Secure = table.Column<bool>(type: "INTEGER", nullable: true),
                    ServerFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SourceAudioChannels = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceAudioCodec = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SourceBitrate = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceContainer = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SourceDynamicRange = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SourceVideoCodec = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SourceVideoResolution = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    StreamAudioChannels = table.Column<int>(type: "INTEGER", nullable: true),
                    StreamAudioCodec = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    StreamBitrate = table.Column<int>(type: "INTEGER", nullable: true),
                    StreamContainer = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    StreamVideoCodec = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    StreamVideoResolution = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SubtitleDecision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VideoDecision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayStreams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayStreams_MovieVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MovieVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayStreams_Plays_PlayId",
                        column: x => x.PlayId,
                        principalTable: "Plays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaServerItems_MediaServerId_RatingKey",
                table: "MediaServerItems",
                columns: new[] { "MediaServerId", "RatingKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaServerItems_MovieId",
                table: "MediaServerItems",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaServerItems_ShowTvdbId",
                table: "MediaServerItems",
                column: "ShowTvdbId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaServerItems_TmdbId",
                table: "MediaServerItems",
                column: "TmdbId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaServerUsers_MediaServerId_ExternalId",
                table: "MediaServerUsers",
                columns: new[] { "MediaServerId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSessions_ItemId",
                table: "PlaybackSessions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSessions_MediaServerId_SessionKey",
                table: "PlaybackSessions",
                columns: new[] { "MediaServerId", "SessionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSessions_UserId",
                table: "PlaybackSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Plays_ItemId_UserId",
                table: "Plays",
                columns: new[] { "ItemId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Plays_MovieId_StartedAt",
                table: "Plays",
                columns: new[] { "MovieId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Plays_StartedAt",
                table: "Plays",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Plays_UserId_StartedAt",
                table: "Plays",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Plays_VersionId_StartedAt",
                table: "Plays",
                columns: new[] { "VersionId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayStreams_ExternalKey",
                table: "PlayStreams",
                column: "ExternalKey",
                unique: true,
                filter: "\"ExternalKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlayStreams_PlayId",
                table: "PlayStreams",
                column: "PlayId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayStreams_VersionId",
                table: "PlayStreams",
                column: "VersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackSessions");

            migrationBuilder.DropTable(
                name: "PlaybackSettings");

            migrationBuilder.DropTable(
                name: "PlayStreams");

            migrationBuilder.DropTable(
                name: "Plays");

            migrationBuilder.DropTable(
                name: "MediaServerItems");

            migrationBuilder.DropTable(
                name: "MediaServerUsers");
        }
    }
}
