using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace Bigarr.Api.Infrastructure.Http;

/// <summary>
/// Brotli and gzip for the JSON the API sends, only when the client asks (<c>Accept-Encoding</c>). The library list is
/// megabytes of repetitive JSON; the SPA's own files are compressed at build time instead (see <c>MapStaticAssets</c>).
/// </summary>
public static class ResponseCompressionExtensions
{
    // JSON only: SignalR long polling (octet-stream, text/plain), server-sent events, backups (zip) and covers (jpeg)
    // are never on this list, so they are never compressed.
    private static readonly string[] MimeTypes = ["application/json", "application/problem+json"];

    public static IServiceCollection AddBigarrResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(o =>
        {
            // The connection is usually TLS-terminated by a proxy, and https is where most traffic is: leaving it off
            // would switch the feature off for them. BREACH needs a secret and attacker-chosen text in one compressed
            // response; the API reflects no request text next to a secret, the session cookie is SameSite=Lax, and the
            // one response that carries a fresh secret (a created API key) is a POST, which is never compressed here.
            o.EnableForHttps = true;
            o.MimeTypes = MimeTypes;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
        });
        // Optimal on both, measured on a 13 MB list: Fastest gzip is 60% bigger for the same time, Fastest brotli is
        // slower and bigger than gzip, and SmallestSize brotli takes seconds. Optimal is a gain from 1 Gbps down.
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        return services;
    }

    /// <summary>
    /// Compresses <c>GET</c> responses under <c>/api/</c> (the SPA's API and the Radarr-compatible <c>/api/v3</c>).
    /// Everything else (SignalR, covers, backups, the event streams' mime types) stays as it is.
    /// </summary>
    public static IApplicationBuilder UseBigarrResponseCompression(this IApplicationBuilder app) =>
        app.UseWhen(
            ctx => HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseResponseCompression());
}
