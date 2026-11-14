using System.Globalization;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Compat.Sonarr;

public static class SonarrCompatEndpoints
{
    public static IServiceCollection AddSonarrCompat(this IServiceCollection services)
    {
        services.AddScoped<SonarrLibrary>();
        services.AddSingleton<SonarrCommandLog>();
        return services;
    }

    /// <summary>
    /// The whole surface under <see cref="SonarrCompat.RoutePrefix"/>, behind the API key policy only. Tags and the folder
    /// browser are the Radarr surface's handlers (same shapes); profiles, root folders and indexers are those handlers
    /// for series. The legacy <c>{base}/api/system/status</c> Bazarr probes first is not mapped: its 404 is the signal.
    /// </summary>
    public static IEndpointRouteBuilder MapSonarrCompat(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(SonarrCompat.RoutePrefix).WithTags("Compat").RequireAuthorization(AuthPolicies.ApiKey).ExcludeFromDescription();
        group.MapGet("/system/status", Status);
        group.MapCompatQualityProfiles(MediaKind.Series);
        group.MapCompatRootFolders(MediaKind.Series);
        group.MapCompatTags();
        group.MapCompatFileSystem();
        group.MapCompatIndexers(MediaKind.Series);
        group.MapSonarrSeries();
        group.MapSonarrEpisodes();
        group.MapSonarrCommands();
        group.MapSonarrQueue();
        group.MapSonarrHistory();

        // Sonarr serves its covers under its URL base, and again under the API path Bazarr's image proxy asks.
        app.MapGet(SonarrCompat.UrlBase + "/MediaCover/{id:int}/{file}", MediaCoverAsync).WithTags("Compat").RequireAuthorization(AuthPolicies.Api).ExcludeFromDescription();
        app.MapGet(SonarrCompat.RoutePrefix + "/MediaCover/{id:int}/{file}", MediaCoverAsync).WithTags("Compat").RequireAuthorization(AuthPolicies.ApiKey).ExcludeFromDescription();
        return app;
    }

    /// <summary>Seerr reads <c>version</c> (a 4.x skips language profiles) and <c>urlBase</c>; Bazarr reads <c>version</c>.</summary>
    private static JsonHttpResult<RadarrSystemStatus> Status(IOptions<BigarrOptions> options, IHostEnvironment environment, Server.ProcessStart start)
    {
        var radarr = SystemEndpoints.Describe(options, environment, start);
        return SonarrCompat.Ok(radarr with { AppName = "Sonarr", Version = SonarrCompat.ApplicationVersion, UrlBase = SonarrCompat.UrlBase });
    }

    /// <summary>
    /// <c>{base}/MediaCover/{id}/poster-500.jpg</c>, the id being a series version's. Covers belong to the show, so the
    /// key's view does not hide them; the bytes come from the provider once and are cached per show.
    /// </summary>
    private static async Task<Results<PhysicalFileHttpResult, NotFound, StatusCodeHttpResult>> MediaCoverAsync(
        int id, string file, SonarrLibrary library, IHttpClientFactory httpClientFactory, IOptions<BigarrOptions> options, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (!MediaCoverEndpoints.TryParse(file, out var type, out var stem))
        {
            return TypedResults.NotFound();
        }

        var row = await library.FindAsync(Domain.Auth.CompatView.All, id, ct);
        var remote = row?.Series.Images.FirstOrDefault(i => i.Type == type)?.RemoteUrl;
        if (row is null || remote is null)
        {
            return TypedResults.NotFound();
        }

        var cacheDirectory = Path.Combine(options.Value.ConfigDirectory, "MediaCover", "series", row.Series.Id.ToString(CultureInfo.InvariantCulture));
        return await MediaCoverEndpoints.ServeAsync(remote, type, stem, cacheDirectory, httpClientFactory, loggerFactory, ct);
    }
}
