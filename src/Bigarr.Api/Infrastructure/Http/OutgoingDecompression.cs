using System.Net;
using Microsoft.Extensions.Http;

namespace Bigarr.Api.Infrastructure.Http;

/// <summary>
/// Every outgoing HttpClient asks for compressed answers (<c>Accept-Encoding: gzip, deflate, br</c>) and decodes them: a
/// Radarr's whole film list is five times smaller gzipped. A filter rather than a client default, because several clients
/// bring their own primary handler (redirects, cookies) and run after the defaults. Only <c>Content-Encoding</c> is
/// decoded, so a torrent or NZB file is still taken byte for byte.
/// </summary>
public static class OutgoingDecompression
{
    public static IServiceCollection AddOutgoingDecompression(this IServiceCollection services)
    {
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, DecompressionFilter>();
        return services;
    }

    private sealed class DecompressionFilter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            switch (builder.PrimaryHandler)
            {
                case HttpClientHandler handler:
                    handler.AutomaticDecompression = DecompressionMethods.All;
                    break;
                case SocketsHttpHandler handler:
                    handler.AutomaticDecompression = DecompressionMethods.All;
                    break;
            }
        };
    }
}
