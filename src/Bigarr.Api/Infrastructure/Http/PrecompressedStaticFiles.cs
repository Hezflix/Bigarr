using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace Bigarr.Api.Infrastructure.Http;

/// <summary>
/// Serves the SPA with the <c>.br</c> / <c>.gz</c> files the web build writes next to each asset (see <c>vite.config.ts</c>),
/// so nothing is compressed per request. Hashed files under <c>/assets</c> are cached for good; everything else
/// (<c>index.html</c> first) is revalidated, which costs a 304 and means a new release shows up at once.
/// </summary>
public static class PrecompressedStaticFiles
{
    private static readonly (string Encoding, string Extension)[] Encodings = [("br", ".br"), ("gzip", ".gz")];

    public static StaticFileOptions Options { get; } = new()
    {
        ContentTypeProvider = new PrecompressedContentTypeProvider(),
        OnPrepareResponse = ctx =>
        {
            var headers = ctx.Context.Response.Headers;
            foreach (var (encoding, extension) in Encodings)
            {
                if (ctx.File.Name.EndsWith(extension, StringComparison.Ordinal))
                {
                    headers.ContentEncoding = encoding;
                }
            }

            headers.Vary = HeaderNames.AcceptEncoding;
            headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets")
                ? "public, max-age=31536000, immutable"
                : "no-cache";
        },
    };

    /// <summary>Must run before the static file middleware: points the request at the compressed twin when there is one.</summary>
    public static IApplicationBuilder UsePrecompressedStaticFiles(this IApplicationBuilder app)
    {
        var files = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        return app.Use((ctx, next) =>
        {
            if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)) && ctx.Request.Path.HasValue)
            {
                var accepted = new RequestHeaders(ctx.Request.Headers).AcceptEncoding;
                foreach (var (encoding, extension) in Encodings)
                {
                    var asked = accepted.Any(a => a.Quality != 0 && string.Equals(a.Value.Value, encoding, StringComparison.OrdinalIgnoreCase));
                    if (asked && files.GetFileInfo(ctx.Request.Path.Value + extension) is { Exists: true, IsDirectory: false })
                    {
                        ctx.Request.Path += extension;
                        break;
                    }
                }
            }

            return next(ctx);
        });
    }

    /// <summary><c>app.js.br</c> is JavaScript: the type comes from the name without the compression suffix.</summary>
    private sealed class PrecompressedContentTypeProvider : IContentTypeProvider
    {
        private readonly FileExtensionContentTypeProvider _inner = new();

        public bool TryGetContentType(string subpath, out string contentType)
        {
            foreach (var (_, extension) in Encodings)
            {
                if (subpath.EndsWith(extension, StringComparison.Ordinal))
                {
                    subpath = subpath[..^extension.Length];
                }
            }

            return _inner.TryGetContentType(subpath, out contentType!);
        }
    }
}
