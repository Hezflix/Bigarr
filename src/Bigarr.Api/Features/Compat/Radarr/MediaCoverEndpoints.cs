using System.Text.RegularExpressions;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>/MediaCover/{id}/poster-500.jpg</c> (§7.3): Bazarr shows Radarr's posters through Radarr, so the relative URLs in
/// <c>images[]</c> must answer. The id is the compat id (a version), the cache is per film in the config directory, the
/// bytes come from TMDB once. Size suffixes map to TMDB's own sizes instead of resizing anything here.
/// </summary>
public static partial class MediaCoverEndpoints
{
    public const string HttpClientName = "mediacover";

    public static IEndpointRouteBuilder MapMediaCover(this IEndpointRouteBuilder app)
    {
        // Radarr serves both paths; Bazarr's proxy uses the API one, its UI the plain one. Either key placement works.
        app.MapGet("/MediaCover/{id:int}/{file}", GetAsync).WithTags("Compat").RequireAuthorization(AuthPolicies.Api);
        app.MapGet(RadarrCompat.RoutePrefix + "/MediaCover/{id:int}/{file}", GetAsync).WithTags("Compat").RequireAuthorization(AuthPolicies.ApiKey);
        return app;
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, StatusCodeHttpResult>> GetAsync(
        int id, string file, CompatLibrary library, IHttpClientFactory httpClientFactory, IOptions<BigarrOptions> options, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var match = FileRegex().Match(file);
        if (!match.Success)
        {
            return TypedResults.NotFound();
        }

        // Any version of the film will do: covers belong to the film, and the key's view does not hide artwork.
        var row = await library.FindAsync(Domain.Auth.CompatView.All, id, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        var type = match.Groups["type"].Value.Equals("poster", StringComparison.OrdinalIgnoreCase) ? ImageType.Poster : ImageType.Fanart;
        var remote = row.Movie.Images.FirstOrDefault(i => i.Type == type)?.RemoteUrl;
        if (remote is null)
        {
            return TypedResults.NotFound();
        }

        var size = match.Groups["size"].Success ? int.Parse(match.Groups["size"].Value, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        var cacheDirectory = Path.Combine(options.Value.ConfigDirectory, "MediaCover", row.Movie.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var cached = Path.Combine(cacheDirectory, $"{match.Groups["type"].Value.ToLowerInvariant()}{(size is null ? string.Empty : $"-{size}")}.jpg");
        if (!File.Exists(cached))
        {
            var url = Sized(remote, type, size);
            try
            {
                using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    return TypedResults.StatusCode(StatusCodes.Status502BadGateway);
                }

                Directory.CreateDirectory(cacheDirectory);
                var partial = cached + ".partial";
                await using (var stream = File.Create(partial))
                {
                    await response.Content.CopyToAsync(stream, ct);
                }

                File.Move(partial, cached, overwrite: true);
            }
            catch (HttpRequestException ex)
            {
                loggerFactory.CreateLogger("Bigarr.Compat.MediaCover").LogWarning(ex, "Could not fetch cover {Url}", url);
                return TypedResults.StatusCode(StatusCodes.Status502BadGateway);
            }
        }

        return TypedResults.PhysicalFile(Path.GetFullPath(cached), "image/jpeg");
    }

    /// <summary>TMDB URLs carry the size as a path segment: <c>/original/</c> becomes <c>/w500/</c> for a 500 px poster.</summary>
    public static string Sized(string remoteUrl, ImageType type, int? size)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);
        if (size is null || !remoteUrl.Contains("/original/", StringComparison.Ordinal))
        {
            return remoteUrl;
        }

        var tmdbSize = type == ImageType.Poster
            ? size <= 185 ? "w185" : size <= 342 ? "w342" : size <= 500 ? "w500" : size <= 780 ? "w780" : "original"
            : size <= 300 ? "w300" : size <= 780 ? "w780" : size <= 1280 ? "w1280" : "original";
        return remoteUrl.Replace("/original/", $"/{tmdbSize}/", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^(?<type>poster|fanart|banner|headshot)(?:-(?<size>\d{2,4}))?\.(?:jpg|jpeg|png)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileRegex();
}
