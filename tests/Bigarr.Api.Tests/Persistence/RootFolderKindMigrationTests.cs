using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>The S2 migration on a database that already holds root folders: they all become film roots.</summary>
public sealed class RootFolderKindMigrationTests : IDisposable
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
    public async Task Existing_root_folders_become_film_roots()
    {
        var options = new DbContextOptionsBuilder<BigarrDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString())
            .Options;

        await using (var before = new BigarrDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("20261109131818_SeriesCatalogue");
            await before.Database.ExecuteSqlRawAsync("INSERT INTO RootFolders (Path) VALUES ('/data/movies'), ('/data/movies-4k')");
        }

        await using var after = new BigarrDbContext(options);
        await after.Database.MigrateAsync();

        var roots = await after.RootFolders.OrderBy(r => r.Path).ToListAsync();
        Assert.Equal(2, roots.Count);
        Assert.All(roots, r => Assert.Equal(MediaKind.Movies, r.Kind));
        Assert.Empty(await after.Database.GetPendingMigrationsAsync());
    }
}
