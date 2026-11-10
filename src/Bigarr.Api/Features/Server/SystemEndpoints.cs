using System.Reflection;
using System.Runtime.InteropServices;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Metadata;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Server;

public sealed record SystemStatusResponse(
    string AppName,
    string Version,
    string EnvironmentName,
    DateTime StartedAt,
    string RuntimeVersion,
    string Os,
    bool IsDocker,
    string ConfigDirectory,
    /// <summary><c>tmdb</c> or <c>fixtures</c>.</summary>
    string MetadataProvider,
    /// <summary><c>Bigarr:Scheduler:Enabled</c>: when false no task runs on its own (RSS sync, searches, health check, maintenance, backups); each runs only when triggered, but for the read-only Plex watch history.</summary>
    bool SchedulerEnabled,
    /// <summary><c>Bigarr:Shadow:Enabled</c>: grabs are recorded as picks and nothing is sent to a download client.</summary>
    bool ShadowEnabled = false,
    /// <summary><c>tvdb+tmdb</c>, <c>tvdb</c>, <c>tmdb</c> or <c>fixtures</c>: where series numbering comes from.</summary>
    string? SeriesMetadataProvider = null);

public static class SystemEndpoints
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    public static readonly string Version =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static IEndpointRouteBuilder MapSystem(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health").AllowAnonymous();

        var group = app.MapGroup("/api/v1/system").WithTags("System").RequireAuthorization(AuthPolicies.Api);
        group.MapGet("/status", Status);

        return app;
    }

    private static Ok<SystemStatusResponse> Status(IOptions<BigarrOptions> options, IOptions<SchedulerOptions> scheduler, IOptions<ShadowOptions> shadow, IHostEnvironment environment, IMetadataProvider metadata, ISeriesMetadataProvider seriesMetadata) =>
        TypedResults.Ok(new SystemStatusResponse(
            AppName: "Bigarr",
            Version: Version,
            EnvironmentName: environment.EnvironmentName,
            StartedAt: StartedAt,
            RuntimeVersion: RuntimeInformation.FrameworkDescription,
            Os: RuntimeInformation.OSDescription,
            IsDocker: string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase),
            ConfigDirectory: Path.GetFullPath(options.Value.ConfigDirectory),
            MetadataProvider: metadata.Name,
            SchedulerEnabled: scheduler.Value.Enabled,
            ShadowEnabled: shadow.Value.Enabled,
            SeriesMetadataProvider: seriesMetadata.Name));
}
