using System.Runtime.InteropServices;
using Bigarr.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>GET /api/v3/system/status</c>: Seerr reads <c>urlBase</c>, Bazarr reads <c>version</c>, Prowlarr reads the
/// version too. The legacy <c>GET /api/system/status</c> Bazarr probes first is simply not mapped: its 404 is the signal.
/// </summary>
public static class SystemEndpoints
{
    public static void MapCompatSystem(this RouteGroupBuilder group)
    {
        group.MapGet("/system/status", Status);
    }

    private static JsonHttpResult<RadarrSystemStatus> Status(IOptions<BigarrOptions> options, IHostEnvironment environment, Server.ProcessStart start) =>
        RadarrCompat.Ok(new RadarrSystemStatus(
            AppName: "Radarr",
            InstanceName: "Bigarr",
            Version: RadarrCompat.ApplicationVersion,
            BuildTime: start.At,
            IsDebug: false,
            IsProduction: environment.IsProduction(),
            IsAdmin: false,
            IsUserInteractive: false,
            StartupPath: AppContext.BaseDirectory,
            AppData: Path.GetFullPath(options.Value.ConfigDirectory),
            OsName: RuntimeInformation.OSDescription,
            OsVersion: Environment.OSVersion.VersionString,
            IsNetCore: true,
            IsLinux: RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            IsOsx: RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            IsWindows: RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            IsDocker: string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase),
            Mode: "console",
            Branch: "bigarr",
            DatabaseType: "sqLite",
            DatabaseVersion: "3",
            Authentication: "external",
            MigrationVersion: 0,
            UrlBase: string.Empty,
            RuntimeVersion: Environment.Version.ToString(),
            RuntimeName: "netCore",
            StartTime: start.At,
            PackageVersion: Server.SystemEndpoints.Version,
            PackageAuthor: "Bigarr",
            PackageUpdateMechanism: "docker"));
}
