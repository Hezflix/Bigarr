using Bigarr.Api.Infrastructure.Auth;

namespace Bigarr.Api.Features.Compat.Radarr;

public static class CompatEndpoints
{
    public static IServiceCollection AddRadarrCompat(this IServiceCollection services)
    {
        services.AddScoped<CompatLibrary>();
        services.AddSingleton<CompatCommandLog>();
        services.AddSignalR();
        services.AddHostedService<CompatHubBroadcaster>();
        services.AddHttpClient(MediaCoverEndpoints.HttpClientName, http =>
        {
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Bigarr/{Server.SystemEndpoints.Version}");
        });
        return services;
    }

    /// <summary>
    /// The whole surface behind one API key policy: the human session never opens <c>/api/v3</c>. Routing is
    /// case-insensitive, so Seerr's <c>/qualityProfile</c> and Bazarr's <c>/qualityprofile</c> land on the same handler.
    /// </summary>
    public static IEndpointRouteBuilder MapRadarrCompat(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RadarrCompat.RoutePrefix).WithTags("Compat").RequireAuthorization(AuthPolicies.ApiKey).ExcludeFromDescription();
        group.MapCompatSystem();
        group.MapCompatQualityProfiles();
        group.MapCompatRootFolders();
        group.MapCompatTags();
        group.MapCompatMovies();
        group.MapCompatCommands();
        group.MapCompatQueue();
        group.MapCompatHistory();
        group.MapCompatFileSystem();
        group.MapCompatIndexers();

        app.MapMediaCover();
        app.MapHub<CompatHub>(CompatHub.Route).RequireAuthorization(AuthPolicies.ApiKey);
        return app;
    }
}
