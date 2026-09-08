using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>A fresh SQLite file with the real migrations applied. One per test.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bigarr-test-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<BigarrDbContext> _options;

    public SqliteTestDatabase()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        _options = new DbContextOptionsBuilder<BigarrDbContext>().UseSqlite(connectionString).Options;

        using var db = CreateContext();
        db.Database.Migrate();
    }

    public BigarrDbContext CreateContext() => new(_options);

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
}
