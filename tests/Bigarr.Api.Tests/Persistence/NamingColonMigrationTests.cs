using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Naming;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>The colon replacement migration on a database that already holds its media management row: the defaults of both apps.</summary>
public sealed class NamingColonMigrationTests : IDisposable
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
    public async Task An_existing_install_gets_replace_on_and_a_smart_colon_for_films_and_series()
    {
        var options = new DbContextOptionsBuilder<BigarrDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString())
            .Options;

        await using (var before = new BigarrDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("20261111172355_PlaybackUserNeverSeenRepair");
            await before.Database.ExecuteSqlRawAsync(
                "INSERT INTO MediaManagement (Id, RenameFiles, FileNameTemplate, FolderNameTemplate, TransferMode, RecycleBinRetentionDays, ImportExtraFiles, ExtraFileExtensions, SeasonPackUpgrade, SeasonPackUpgradeThreshold, RenameEpisodes, MultiEpisodeStyle) " +
                "VALUES (1, 1, '{{Movie Title}} {{Quality Full}}', '{{Movie Title}}', 1, 7, 1, 'srt', 0, 100, 1, 5)");
        }

        await using var after = new BigarrDbContext(options);
        await after.Database.MigrateAsync();

        var config = await after.MediaManagement.SingleAsync();
        Assert.Equal("{Movie Title} {Quality Full}", config.FileNameTemplate);
        Assert.Equal((true, ColonReplacement.Smart), (config.ReplaceIllegalCharacters, config.ColonReplacement));
        Assert.Equal((true, ColonReplacement.Smart, string.Empty), (config.SeriesReplaceIllegalCharacters, config.SeriesColonReplacement, config.SeriesCustomColonReplacement));
        Assert.Equal(NameCleaning.Default, config.FilmNameCleaning);
        Assert.Empty(await after.Database.GetPendingMigrationsAsync());
    }
}
