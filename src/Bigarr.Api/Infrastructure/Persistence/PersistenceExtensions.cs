using Bigarr.Api.Infrastructure.Events;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddBigarrPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(BigarrOptions.Section).Get<BigarrOptions>() ?? new BigarrOptions();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();

        // The outbox interceptor wakes the dispatcher once a save carrying events has committed.
        services.AddDbContext<BigarrDbContext>((sp, o) => o.UseSqlite(connectionString).AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));
        services.AddHostedService<DatabaseInitializer>();
        return services;
    }
}
