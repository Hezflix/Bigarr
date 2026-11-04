namespace Bigarr.Api.Infrastructure.MediaServers;

public static class MediaServersExtensions
{
    public const string HttpClientName = "bigarr-mediaservers";

    public static IServiceCollection AddBigarrMediaServers(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Bigarr/{Features.Server.SystemEndpoints.Version}");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddSingleton<IMediaServerClient, PlexClient>();
        services.AddSingleton<IMediaServerClient, EmbyClient>();
        services.AddSingleton<MediaServerClients>();
        services.AddHostedService<MediaServerRefresher>();
        return services;
    }
}
