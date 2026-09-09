using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Persistence;

/// <summary>
/// Creates the config directory, applies migrations, switches SQLite to WAL, then runs the startup tasks.
/// Hosted services start before Kestrel listens, so no request ever sees an unmigrated database.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<BigarrOptions> options,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.Value.ConfigDirectory);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        }

        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);

        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
        {
            await task.RunAsync(cancellationToken);
        }

        logger.LogInformation("Database ready at {Path}", Path.GetFullPath(options.Value.DatabasePath));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
