namespace Bigarr.Api.Infrastructure.MediaServers;

public static class MediaServersExtensions
{
    public const string HttpClientName = "bigarr-mediaservers";

    /// <summary>The long-lived event stream: no overall timeout, the reader detects a silent connection itself.</summary>
    public const string EventsHttpClientName = "bigarr-mediaservers-events";

    public static IServiceCollection AddBigarrMediaServers(this IServiceCollection services, IConfiguration configuration)
    {
        var plexTv = configuration.GetSection(PlexTvOptions.Section).Get<PlexTvOptions>() ?? new PlexTvOptions();
        services.AddHttpClient<PlexTvClient>(http =>
            {
                http.BaseAddress = new Uri(plexTv.BaseUrl.EndsWith('/') ? plexTv.BaseUrl : plexTv.BaseUrl + "/");
                // A lookup holds up the recording of a stream: better no location than a stalled monitor.
                http.Timeout = TimeSpan.FromSeconds(5);
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Bigarr/{Features.Server.SystemEndpoints.Version}");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpClient(HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Bigarr/{Features.Server.SystemEndpoints.Version}");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpClient(EventsHttpClientName, http =>
            {
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Bigarr/{Features.Server.SystemEndpoints.Version}");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddSingleton<PlexClient>();
        services.AddSingleton<IMediaServerClient>(sp => sp.GetRequiredService<PlexClient>());
        services.AddSingleton<IMediaServerClient, EmbyClient>();
        services.AddSingleton<MediaServerClients>();
        services.AddHostedService<MediaServerRefresher>();
        return services;
    }
}
