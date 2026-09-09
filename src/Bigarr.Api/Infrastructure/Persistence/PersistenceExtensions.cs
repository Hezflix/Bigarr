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

        services.AddDbContext<BigarrDbContext>(o => o.UseSqlite(connectionString));
        services.AddHostedService<DatabaseInitializer>();
        return services;
    }
}
