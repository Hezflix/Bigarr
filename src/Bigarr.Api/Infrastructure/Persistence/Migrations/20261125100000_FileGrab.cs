using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FileGrab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GrabId",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrabId",
                table: "EpisodeFiles",
                type: "INTEGER",
                nullable: true);

            // Files imported before the column: the journal's Imported or Upgraded entry (EventType 3, 4) for the file kept
            // the download id of its grab; the grab is the last one with that download id for the same target, made before
            // the import. A file imported without a grab (by hand, a scan, Radarr/Sonarr) has none and stays null.
            migrationBuilder.Sql("""
                UPDATE MediaFiles SET GrabId = (
                    SELECT g.Id FROM History h
                    JOIN GrabTargets gt ON gt.TargetId = h.TargetId
                    JOIN Grabs g ON g.Id = gt.GrabId
                    WHERE h.MediaFileId = MediaFiles.Id
                      AND h.EventType IN (3, 4)
                      AND g.DownloadId IS NOT NULL
                      AND g.DownloadId = json_extract(h.Data, '$.downloadId') COLLATE NOCASE
                      AND g.GrabbedAt <= h.CreatedAt
                    ORDER BY h.CreatedAt DESC, g.GrabbedAt DESC
                    LIMIT 1);
                """);

            // Episode files: the file id sits in the entry's data, one entry per episode; read once, in one pass. Grab ids grow
            // with time, so the highest is the last grab.
            migrationBuilder.Sql("""
                UPDATE EpisodeFiles SET GrabId = m.GrabId
                FROM (
                    SELECT CAST(json_extract(h.Data, '$.episodeFileId') AS INTEGER) AS FileId, MAX(g.Id) AS GrabId
                    FROM History h
                    JOIN GrabTargets gt ON gt.TargetId = h.TargetId
                    JOIN Grabs g ON g.Id = gt.GrabId
                    WHERE h.EventType IN (3, 4)
                      AND h.SeriesId IS NOT NULL
                      AND g.DownloadId IS NOT NULL
                      AND g.DownloadId = json_extract(h.Data, '$.downloadId') COLLATE NOCASE
                      AND g.GrabbedAt <= h.CreatedAt
                    GROUP BY FileId) AS m
                WHERE m.FileId = EpisodeFiles.Id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GrabId",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "GrabId",
                table: "EpisodeFiles");
        }
    }
}
