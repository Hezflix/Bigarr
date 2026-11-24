using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>Grabs made before the Interactive column take it from their journal entry: only the interactive search's.</summary>
public sealed class InteractiveGrabsMigrationTests : IDisposable
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
    public async Task Grabs_the_journal_says_were_picked_by_hand_are_marked_interactive()
    {
        var options = new DbContextOptionsBuilder<BigarrDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString())
            .Options;

        await using (var before = new BigarrDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("20261123151943_MediaInfoTriedAt");
            await before.Database.ExecuteSqlRawAsync("""
                INSERT INTO Grabs (Id, DownloadId, ReleaseTitle, Size, IndexerFlags, Protocol, GrabbedAt, State, StatusMessages, EpisodesOverridden)
                VALUES (1, 'aaaa', 'Godzilla.1998.By.Hand', 0, 0, 1, '2026-11-23 10:00:00', 6, '[]', 0),
                       (2, 'bbbb', 'Godzilla.1998.Rss', 0, 0, 1, '2026-11-23 10:00:00', 6, '[]', 0),
                       (3, NULL, 'No.Download.Id', 0, 0, 1, '2026-11-23 10:00:00', 6, '[]', 0)
                """);
            // Written by EF so the Data column is in the format the app stores; the download id is upper case here.
            before.History.AddRange(
                new HistoryEvent { EventType = HistoryEventType.Grabbed, SourceTitle = "Godzilla.1998.By.Hand", Data = new() { ["downloadId"] = "AAAA", ["trigger"] = "interactive" } },
                new HistoryEvent { EventType = HistoryEventType.Grabbed, SourceTitle = "Godzilla.1998.Rss", Data = new() { ["downloadId"] = "bbbb", ["trigger"] = "rss" } },
                new HistoryEvent { EventType = HistoryEventType.ImportFailed, SourceTitle = "Godzilla.1998.Rss", Data = new() { ["downloadId"] = "bbbb", ["trigger"] = "interactive" } });
            await before.SaveChangesAsync();
        }

        await using var after = new BigarrDbContext(options);
        await after.Database.MigrateAsync();

        var marks = await after.Grabs.OrderBy(g => g.Id).Select(g => g.Interactive).ToListAsync();
        Assert.Equal([true, false, false], marks);
        Assert.Empty(await after.Database.GetPendingMigrationsAsync());
    }
}
