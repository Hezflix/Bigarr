using System.Net;
using Bigarr.Domain.Downloads;

namespace Bigarr.Api.Infrastructure.Downloads.Torrents;

/// <summary>What an indexer link turned into: a torrent file we hold, or a magnet the link redirected to.</summary>
public sealed record FetchedTorrent(byte[]? File, string? Magnet)
{
    public string InfoHash => (File is not null ? Bencode.TryGetInfoHash(File) : MagnetLink.TryGetInfoHash(Magnet))
                              ?? throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, "The torrent has no readable info hash.");
}

/// <summary>
/// Downloads the <c>.torrent</c> behind an indexer link so the info hash is known before the client sees it (that is
/// how the queue finds the download again). Trackers often answer with a redirect to a magnet: that is kept as is.
/// </summary>
public sealed class TorrentFileFetcher(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "torrent-files";

    /// <summary>Anything bigger is not a torrent file.</summary>
    public const int MaxBytes = 16 * 1024 * 1024;

    public async Task<FetchedTorrent> FetchAsync(string downloadUrl, CancellationToken cancellationToken)
    {
        if (MagnetLink.IsMagnet(downloadUrl))
        {
            return new FetchedTorrent(null, downloadUrl);
        }

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, $"The release link is not a magnet nor an http(s) URL: {downloadUrl}");
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                // Redirects to http(s) are followed by the handler; only a magnet ends up here.
                var location = response.Headers.Location?.OriginalString;
                if (MagnetLink.IsMagnet(location))
                {
                    return new FetchedTorrent(null, location);
                }

                throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, $"The indexer redirected the torrent link to {location ?? "nowhere"}.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, $"The indexer refused the torrent link (HTTP {(int)response.StatusCode}).");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, $"The indexer answered HTTP {(int)response.StatusCode} for the torrent link.");
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, "The torrent link points at something far too large to be a torrent file.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > MaxBytes || Bencode.TryGetInfoHash(bytes) is null)
            {
                var type = response.Content.Headers.ContentType?.MediaType ?? "unknown type";
                throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, $"The torrent link did not return a torrent file ({type}, {bytes.Length} bytes). A login page in front of the indexer is the usual cause.");
            }

            return new FetchedTorrent(bytes, null);
        }
        catch (HttpRequestException ex)
        {
            throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, $"Could not download the torrent file: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, "The indexer did not deliver the torrent file in time.", ex);
        }
    }
}
