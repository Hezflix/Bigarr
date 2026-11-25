using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>
/// Files imported before the GrabId column take it from their journal entry: the grab whose download the import came
/// from, never a grab of the same target that a file put in by hand later replaced.
/// </summary>
public sealed class FileGrabMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bigarr-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task Files_take_the_grab_their_import_entry_names_and_a_file_put_in_by_hand_takes_none()
    {
        // Rows only, no films, series or versions behind them: the backfill reads ids, not foreign keys.
        var options = new DbContextOptionsBuilder<BigarrDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, ForeignKeys = false }.ToString())
            .Options;
        var at = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        await using (var before = new BigarrDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("20261124145048_ApiKeyScope");
            await before.Database.ExecuteSqlRawAsync("""
                INSERT INTO Grabs (Id, DownloadId, ReleaseTitle, Size, IndexerFlags, Protocol, GrabbedAt, State, StatusMessages, EpisodesOverridden)
                VALUES (1, 'aaaa', 'The.Film.2020.1080p.BluRay-GRP', 0, 0, 1, '2026-11-20 10:00:00', 5, '[]', 0),
                       (2, 'bbbb', 'The.Show.S01.1080p.BluRay-GRP', 0, 0, 1, '2026-11-20 10:00:00', 5, '[]', 0),
                       (3, 'cccc', 'The.Film.2020.720p.WEB-OLD', 0, 0, 1, '2026-11-19 10:00:00', 5, '[]', 0);
                INSERT INTO GrabTargets (GrabId, TargetId, Active) VALUES (1, 10, 0), (2, 20, 0), (2, 21, 0), (3, 11, 0);
                INSERT INTO MediaFiles (Id, DateAdded, IndexerFlags, Languages, QualityId, RelativePath, Size, VersionId, RevisionVersion, RevisionReal, IsRepack)
                VALUES (1, '2026-11-20 12:00:00', 0, '[]', 7, 'a.mkv', 1, 10, 1, 0, 0),
                       (2, '2026-11-21 12:00:00', 0, '[]', 7, 'b.mkv', 1, 11, 1, 0, 0);
                INSERT INTO EpisodeFiles (Id, DateAdded, IndexerFlags, Languages, QualityId, RelativePath, SeasonNumber, Size, VersionId, RevisionVersion, RevisionReal, IsRepack)
                VALUES (3, '2026-11-20 12:00:00', 0, '[]', 7, 's01e01.mkv', 1, 1, 5, 1, 0, 0),
                       (4, '2026-11-20 12:00:00', 0, '[]', 7, 's01e02.mkv', 1, 1, 5, 1, 0, 0),
                       (5, '2026-11-21 12:00:00', 0, '[]', 7, 's01e03.mkv', 1, 1, 5, 1, 0, 0);
                """);

            // Written by EF so the Data column is in the format the app stores; the film's download id is upper case here.
            before.History.AddRange(
                // Film file 1 came from grab 1.
                new HistoryEvent { EventType = HistoryEventType.Imported, TargetId = 10, MediaFileId = 1, CreatedAt = at, Data = new() { ["downloadId"] = "AAAA" } },
                // Film file 2 was put in by hand over grab 3's file: its entry names no download.
                new HistoryEvent { EventType = HistoryEventType.Imported, TargetId = 11, CreatedAt = at.AddDays(-1), Data = new() { ["downloadId"] = "cccc" } },
                new HistoryEvent { EventType = HistoryEventType.Upgraded, TargetId = 11, MediaFileId = 2, CreatedAt = at.AddDays(1), Data = new() { ["manual"] = "true" } },
                // A pack's two episode files came from grab 2; the third episode's file was put in by hand.
                new HistoryEvent { EventType = HistoryEventType.Imported, TargetId = 20, SeriesId = 1, CreatedAt = at, Data = new() { ["downloadId"] = "bbbb", ["episodeFileId"] = "3" } },
                new HistoryEvent { EventType = HistoryEventType.Imported, TargetId = 21, SeriesId = 1, CreatedAt = at, Data = new() { ["downloadId"] = "bbbb", ["episodeFileId"] = "4" } },
                new HistoryEvent { EventType = HistoryEventType.Imported, TargetId = 22, SeriesId = 1, CreatedAt = at.AddDays(1), Data = new() { ["episodeFileId"] = "5" } });
            await before.SaveChangesAsync();
        }

        await using var after = new BigarrDbContext(options);
        await after.Database.MigrateAsync();

        Assert.Equal([1, null], await after.MediaFiles.OrderBy(f => f.Id).Select(f => f.GrabId).ToListAsync());
        Assert.Equal([2, 2, null], await after.EpisodeFiles.OrderBy(f => f.Id).Select(f => f.GrabId).ToListAsync());
        Assert.Empty(await after.Database.GetPendingMigrationsAsync());
    }
}
