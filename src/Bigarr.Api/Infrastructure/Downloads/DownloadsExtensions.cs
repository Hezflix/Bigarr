using Bigarr.Api.Infrastructure.Downloads.Mock;
using Bigarr.Api.Infrastructure.Downloads.QBittorrent;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Domain.Downloads;

namespace Bigarr.Api.Infrastructure.Downloads;

public static class DownloadsExtensions
{
    private const string UserAgent = "Bigarr/0.1 (+https://github.com/Hezflix/Bigarr)";

    public static IServiceCollection AddBigarrDownloads(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DownloadsOptions>(configuration.GetSection(DownloadsOptions.Section));

        // Sessions are handled by hand (one SID per configured client); redirects mean a proxy is in the way.
        services.AddHttpClient(QBittorrentClient.HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

        // Torrent files: trackers redirect (to a mirror, or to a magnet, which the handler leaves to us).
        services.AddHttpClient(TorrentFileFetcher.HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(60);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/x-bittorrent, */*;q=0.5");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 });

        services.AddSingleton<TorrentFileFetcher>();
        services.AddSingleton<IDownloadClient, QBittorrentClient>();
        services.AddSingleton<MockDownloadClientState>();
        services.AddSingleton<IDownloadClient, MockDownloadClient>();
        services.AddSingleton<DownloadClientResolver>();
        return services;
    }
}
