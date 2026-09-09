using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Metadata;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bigarr.Api.Infrastructure.Metadata;

public static class MetadataExtensions
{
    public static IServiceCollection AddBigarrMetadata(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetadataOptions>(configuration.GetSection(MetadataOptions.Section));
        services.TryAddSingleton(TimeProvider.System);

        var options = configuration.GetSection(MetadataOptions.Section).Get<MetadataOptions>() ?? new MetadataOptions();
        if (options.HasApiKey)
        {
            services.AddTransient<TmdbAuthHandler>();
            services.AddHttpClient<TmdbClient>(http =>
                {
                    http.BaseAddress = new Uri(options.BaseUrl);
                    http.Timeout = TimeSpan.FromSeconds(20);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Bigarr/0.1 (+https://github.com/Hezflix/Bigarr)");
                })
                .AddHttpMessageHandler<TmdbAuthHandler>();
            services.AddTransient<IMetadataProvider, TmdbMetadataProvider>();
        }
        else
        {
            services.AddSingleton<IMetadataProvider, FixtureMetadataProvider>();
        }

        services.AddScoped<IStartupTask, MetadataStartupTask>();
        services.AddHealthChecks().AddCheck<MetadataHealthCheck>("metadata");
        return services;
    }

    private static void TryAddSingleton(this IServiceCollection services, TimeProvider instance)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(instance);
        }
    }
}

/// <summary>Says once at startup which provider is live. Fixtures in production is a warning, not an error.</summary>
public sealed class MetadataStartupTask(IMetadataProvider provider, IHostEnvironment environment, ILogger<MetadataStartupTask> logger) : IStartupTask
{
    public Task RunAsync(CancellationToken cancellationToken)
    {
        if (provider.Name == "fixtures")
        {
            var level = environment.IsProduction() ? LogLevel.Warning : LogLevel.Information;
            logger.Log(level, "No TMDB API key configured (Bigarr__Tmdb__ApiKey): using the embedded fixture catalogue (a dozen films).");
        }
        else
        {
            logger.LogInformation("Metadata provider: {Provider}", provider.Name);
        }

        return Task.CompletedTask;
    }
}

public sealed class MetadataHealthCheck(IMetadataProvider provider, IHostEnvironment environment) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (provider.Name == "fixtures" && environment.IsProduction())
        {
            return Task.FromResult(HealthCheckResult.Degraded("TMDB API key not configured: the fixture catalogue is in use."));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"metadata: {provider.Name}"));
    }
}
