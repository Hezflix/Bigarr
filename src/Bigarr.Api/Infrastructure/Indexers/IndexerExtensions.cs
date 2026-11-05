using Bigarr.Api.Infrastructure.Indexers.Mock;
using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Infrastructure.Indexers;

public static class IndexerExtensions
{
    private const string UserAgent = "Bigarr/0.1 (+https://github.com/Hezflix/Bigarr)";

    public static IServiceCollection AddBigarrIndexers(this IServiceCollection services)
    {
        // Redirects are never followed: an SSO proxy answering with its login page must surface as an error, not as HTML to parse.
        services.AddHttpClient(TorznabClient.HttpClientName, http =>
            {
                http.Timeout = TimeSpan.FromSeconds(60);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/xml, text/xml;q=0.9, */*;q=0.5");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddSingleton<IIndexerClient, TorznabClient>();
        services.AddSingleton<IIndexerClient, MockIndexerClient>();
        services.AddSingleton<IndexerGateway>();
        return services;
    }
}
