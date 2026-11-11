using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Downloads.Mock;

/// <summary>
/// What the mock client "downloads": progress follows the clock, so the queue moves on its own. Tests and the
/// developer can complete, fail or remove a torrent by hand, like a user would in qBittorrent. A completed torrent
/// gets real files on disk (a small video and a sample) so the import has something to move into the library; with
/// ffmpeg around the video is a genuine two-second clip, so ffprobe has something to read too.
/// </summary>
public sealed partial class MockDownloadClientState(TimeProvider time, IOptions<DownloadsOptions> downloads, IOptions<BigarrOptions> bigarr)
{
    /// <summary>Size of the fake video file when ffmpeg is not available: enough to notice a copy, small enough for tests.</summary>
    public const int VideoFileBytes = 1024 * 1024;

    private static readonly Lazy<string?> Ffmpeg = new(() => ExecutableLocator.Find("ffmpeg"));

    private readonly ConcurrentDictionary<string, MockTorrent> _torrents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long any torrent takes to complete, whatever its size.</summary>
    public TimeSpan CompletionDelay { get; set; } = TimeSpan.FromSeconds(90);

    public string DownloadDirectory { get; } = Path.GetFullPath(downloads.Value.MockDownloadDirectory ?? Path.Combine(bigarr.Value.ConfigDirectory, "mock-downloads"));

    /// <summary>Folder of a torrent's content: <c>&lt;dir&gt;/&lt;category&gt;/&lt;title&gt;</c>.</summary>
    public string ContentPath(MockTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return Path.Combine(DownloadDirectory, FolderNameBuilder.Sanitize(torrent.Category), FolderNameBuilder.Sanitize(torrent.Title));
    }

    /// <summary>
    /// Writes the content of a completed torrent once: <c>&lt;title&gt;.mkv</c> plus a sample file the import must skip.
    /// A torrent covering several episodes (a season pack) gets one video per episode instead, named like a pack's files
    /// (<c>Show.S01E03.1080p…mkv</c>), each with an English subtitle, plus one sample.
    /// </summary>
    public void Materialize(MockTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        var folder = ContentPath(torrent);
        var names = FileNames(torrent);
        var marker = Path.Combine(folder, names[0] + ".mkv");
        if (File.Exists(marker))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var bytes = new byte[4096];
        new Random(torrent.Hash.GetHashCode(StringComparison.Ordinal)).NextBytes(bytes);
        // The first file last: it is the "already written" marker above.
        foreach (var name in names.AsEnumerable().Reverse())
        {
            var video = Path.Combine(folder, name + ".mkv");
            if (!TryRenderClip(video, name))
            {
                var filler = new byte[VideoFileBytes];
                new Random(HashCode.Combine(torrent.Hash.GetHashCode(StringComparison.Ordinal), name.GetHashCode(StringComparison.Ordinal))).NextBytes(filler);
                File.WriteAllBytes(video, filler);
            }

            if (names.Count > 1)
            {
                File.WriteAllText(Path.Combine(folder, name + ".en.srt"), "1\n00:00:00,000 --> 00:00:01,500\n" + name + "\n");
            }
        }

        File.WriteAllBytes(Path.Combine(folder, FolderNameBuilder.Sanitize(torrent.Title) + "-sample.mkv"), bytes);
    }

    /// <summary>File names (without extension) of a torrent's videos: the title, or one per episode of a pack.</summary>
    public static List<string> FileNames(MockTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        var title = FolderNameBuilder.Sanitize(torrent.Title);
        if (torrent.EpisodeLabels is not { Count: > 1 } labels)
        {
            return [title];
        }

        var season = SeasonTokenRegex().Match(title);
        return labels
            .Select(label => season.Success
                ? title[..season.Index] + label + title[(season.Index + season.Length)..]
                : $"{title}.{label}")
            .ToList();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![a-z0-9])S\d{1,2}(?:-S?\d{1,2})?(?![a-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SeasonTokenRegex();

    /// <summary>A real two-second h264/AAC clip, so an import probes something. False when ffmpeg is missing or refuses.</summary>
    private static bool TryRenderClip(string path, string title)
    {
        var ffmpeg = Ffmpeg.Value;
        if (ffmpeg is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[]
        {
            "-v", "error", "-y",
            "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=10",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac",
            "-metadata:s:v:0", "language=eng", "-metadata:s:a:0", "language=eng", "-metadata", "title=" + title,
            path,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0 && new FileInfo(path).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void DeleteContent(MockTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        var folder = ContentPath(torrent);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    public IReadOnlyCollection<MockTorrent> All => _torrents.Values.ToList();

    public MockTorrent Add(string hash, string title, string category, long size, bool paused, IReadOnlyList<string>? episodeLabels = null)
    {
        var torrent = new MockTorrent(hash.ToLowerInvariant(), title, category, size, time.GetUtcNow()) { Paused = paused, EpisodeLabels = episodeLabels };
        _torrents[torrent.Hash] = torrent;
        return torrent;
    }

    public bool Remove(string hash, bool deleteFiles = false)
    {
        if (!_torrents.TryRemove(hash, out var torrent))
        {
            return false;
        }

        if (deleteFiles)
        {
            DeleteContent(torrent);
        }

        return true;
    }

    public bool Complete(string hash)
    {
        if (!_torrents.TryGetValue(hash, out var torrent))
        {
            return false;
        }

        torrent.CompletedAt = time.GetUtcNow();
        torrent.Paused = false;
        return true;
    }

    /// <summary>Freezes the torrent where it is: no peers, no progress, the client reports it stalled.</summary>
    public bool Stall(string hash)
    {
        if (!_torrents.TryGetValue(hash, out var torrent) || torrent.CompletedAt is not null)
        {
            return false;
        }

        torrent.StalledAt ??= time.GetUtcNow();
        return true;
    }

    public bool Fail(string hash, string message)
    {
        if (!_torrents.TryGetValue(hash, out var torrent))
        {
            return false;
        }

        torrent.Error = message;
        return true;
    }

    public DownloadItem ToItem(MockTorrent torrent)
    {
        var now = torrent.StalledAt ?? time.GetUtcNow();
        var progress = torrent.CompletedAt is not null
            ? 1
            : Math.Clamp((now - torrent.AddedAt).TotalSeconds / Math.Max(1, CompletionDelay.TotalSeconds), 0, 1);
        if (torrent.Paused && torrent.CompletedAt is null)
        {
            progress = Math.Min(progress, 0.05);
        }

        var remaining = (long)Math.Round(torrent.Size * (1 - progress));
        var status = torrent.Error is not null ? DownloadItemStatus.Failed
            : progress >= 1 ? DownloadItemStatus.Completed
            : torrent.Paused ? DownloadItemStatus.Paused
            : torrent.StalledAt is not null ? DownloadItemStatus.Stalled
            : DownloadItemStatus.Downloading;
        var rate = status == DownloadItemStatus.Downloading ? (long)(torrent.Size / Math.Max(1, CompletionDelay.TotalSeconds)) : 0;
        var eta = status == DownloadItemStatus.Downloading ? CompletionDelay * (1 - progress) : (TimeSpan?)null;
        if (status == DownloadItemStatus.Completed)
        {
            Materialize(torrent);
        }

        return new DownloadItem(
            torrent.Hash,
            torrent.Title,
            torrent.Size,
            remaining,
            progress,
            status,
            eta,
            rate,
            ContentPath(torrent),
            torrent.Error,
            progress >= 1 ? 0.1 : null,
            null,
            status == DownloadItemStatus.Downloading ? 12 : null,
            torrent.AddedAt,
            progress >= 1 ? torrent.CompletedAt ?? torrent.AddedAt + CompletionDelay : null,
            torrent.Category);
    }

    public sealed class MockTorrent(string hash, string title, string category, long size, DateTimeOffset addedAt)
    {
        public string Hash { get; } = hash;

        public string Title { get; } = title;

        public string Category { get; } = category;

        public long Size { get; } = size;

        public DateTimeOffset AddedAt { get; } = addedAt;

        public bool Paused { get; set; }

        public string? Error { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public DateTimeOffset? StalledAt { get; set; }

        /// <summary>The episodes of a series release (<c>S01E01</c>…): several give a multi-file torrent.</summary>
        public IReadOnlyList<string>? EpisodeLabels { get; init; }
    }
}

/// <summary>A download client without a network: for development and for the acceptance tests of the queue.</summary>
public sealed class MockDownloadClient(MockDownloadClientState state) : IDownloadClient
{
    public DownloadClientImplementation Implementation => DownloadClientImplementation.Mock;

    public Task<DownloadClientInfo> TestAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Task.FromResult(new DownloadClientInfo("mock", null, client.Category, false));
    }

    public Task<string> AddAsync(DownloadClient client, DownloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        var hash = InfoHashes.Normalize(request.InfoHash)
                   ?? MagnetLink.TryGetInfoHash(request.MagnetUrl)
                   ?? MagnetLink.TryGetInfoHash(request.DownloadUrl)
                   ?? Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(request.Title)));

        if (state.All.Any(t => t.Hash == hash))
        {
            throw new DownloadClientException(DownloadClientFailureKind.Request, "The mock client already has this torrent.");
        }

        state.Add(hash, request.Title, string.IsNullOrWhiteSpace(request.Category) ? client.Category : request.Category, request.Size > 0 ? request.Size : 8L * 1024 * 1024 * 1024, client.AddPaused, request.EpisodeLabels);
        return Task.FromResult(hash);
    }

    public Task<IReadOnlyList<DownloadItem>> GetItemsAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        IReadOnlyList<DownloadItem> items = state.All.Where(t => t.Category == client.Category || t.Category == client.SeriesCategory).Select(state.ToItem).ToList();
        return Task.FromResult(items);
    }

    public Task RemoveAsync(DownloadClient client, string downloadId, bool deleteFiles, CancellationToken cancellationToken)
    {
        state.Remove(downloadId, deleteFiles);
        return Task.CompletedTask;
    }
}
