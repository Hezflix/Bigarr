using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Domain.Downloads;

namespace Bigarr.Api.Infrastructure.Downloads.Mock;

/// <summary>
/// What the mock client "downloads": progress follows the clock, so the queue moves on its own. Tests and the
/// developer can complete, fail or remove a torrent by hand, like a user would in qBittorrent.
/// </summary>
public sealed class MockDownloadClientState(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, MockTorrent> _torrents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long any torrent takes to complete, whatever its size.</summary>
    public TimeSpan CompletionDelay { get; set; } = TimeSpan.FromSeconds(90);

    public IReadOnlyCollection<MockTorrent> All => _torrents.Values.ToList();

    public MockTorrent Add(string hash, string title, string category, long size, bool paused)
    {
        var torrent = new MockTorrent(hash.ToLowerInvariant(), title, category, size, time.GetUtcNow()) { Paused = paused };
        _torrents[torrent.Hash] = torrent;
        return torrent;
    }

    public bool Remove(string hash) => _torrents.TryRemove(hash, out _);

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
        var now = time.GetUtcNow();
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
            : DownloadItemStatus.Downloading;
        var rate = status == DownloadItemStatus.Downloading ? (long)(torrent.Size / Math.Max(1, CompletionDelay.TotalSeconds)) : 0;
        var eta = status == DownloadItemStatus.Downloading ? CompletionDelay * (1 - progress) : (TimeSpan?)null;

        return new DownloadItem(
            torrent.Hash,
            torrent.Title,
            torrent.Size,
            remaining,
            progress,
            status,
            eta,
            rate,
            $"/data/downloads/{torrent.Category}/{torrent.Title}",
            torrent.Error,
            progress >= 1 ? 0.1 : null,
            null,
            status == DownloadItemStatus.Downloading ? 12 : null,
            torrent.AddedAt,
            progress >= 1 ? torrent.CompletedAt ?? torrent.AddedAt + CompletionDelay : null);
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

        state.Add(hash, request.Title, client.Category, request.Size > 0 ? request.Size : 8L * 1024 * 1024 * 1024, client.AddPaused);
        return Task.FromResult(hash);
    }

    public Task<IReadOnlyList<DownloadItem>> GetItemsAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        IReadOnlyList<DownloadItem> items = state.All.Where(t => t.Category == client.Category).Select(state.ToItem).ToList();
        return Task.FromResult(items);
    }

    public Task RemoveAsync(DownloadClient client, string downloadId, bool deleteFiles, CancellationToken cancellationToken)
    {
        state.Remove(downloadId);
        return Task.CompletedTask;
    }
}
