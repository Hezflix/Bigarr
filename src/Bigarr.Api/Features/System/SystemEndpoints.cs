using System.Reflection;
using System.Runtime.InteropServices;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.System;

public sealed record SystemStatusResponse(
    string AppName,
    string Version,
    string EnvironmentName,
    DateTime StartedAt,
    string RuntimeVersion,
    string Os,
    bool IsDocker,
    string ConfigDirectory);

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

    private static Ok<SystemStatusResponse> Status(IOptions<BigarrOptions> options, IHostEnvironment environment) =>
        TypedResults.Ok(new SystemStatusResponse(
            AppName: "Bigarr",
            Version: Version,
            EnvironmentName: environment.EnvironmentName,
            StartedAt: StartedAt,
            RuntimeVersion: RuntimeInformation.FrameworkDescription,
            Os: RuntimeInformation.OSDescription,
            IsDocker: string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase),
            ConfigDirectory: Path.GetFullPath(options.Value.ConfigDirectory)));
}
