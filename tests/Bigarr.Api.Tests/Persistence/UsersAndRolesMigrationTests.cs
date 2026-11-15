using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>
/// The R1 migration on an install that already has its settings row and its admin: new Plex accounts are let in and
/// new users get the default permissions, as on a new install; the admin stays the admin.
/// </summary>
public sealed class UsersAndRolesMigrationTests : IDisposable
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
    public async Task An_upgraded_install_lets_new_plex_accounts_in_with_the_default_permissions()
    {
        var options = new DbContextOptionsBuilder<BigarrDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString())
            .Options;

        await using (var before = new BigarrDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("20261115002824_IndexerSeasonPackSeedTime");
            await before.Database.ExecuteSqlRawAsync("INSERT INTO InstanceSettings (Id, PlexClientIdentifier) VALUES (1, 'demo-client')");
            await before.Database.ExecuteSqlRawAsync("INSERT INTO Users (Username, PasswordHash, Role, CreatedAt) VALUES ('admin', 'hash', 1, '2026-11-01 10:00:00')");
        }

        await using var after = new BigarrDbContext(options);
        await after.Database.MigrateAsync();

        var settings = await after.InstanceSettings.SingleAsync();
        Assert.True(settings.PlexNewAccounts);
        Assert.Equal(
            UserPermissions.Defaults().Granted(),
            settings.DefaultPermissions.Granted());

        var admin = await after.Users.SingleAsync();
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Null(admin.DisabledAt);
        Assert.Equal(6, admin.Effective.Granted().Count());
        Assert.Empty(await after.Database.GetPendingMigrationsAsync());
    }
}
