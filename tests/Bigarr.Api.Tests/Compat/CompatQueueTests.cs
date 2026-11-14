using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Downloads;
using Bigarr.Domain.Downloads;

namespace Bigarr.Api.Tests.Compat;

/// <summary>What the Radarr <c>/queue</c> lists: film downloads of the key's view, never a series download.</summary>
public sealed class CompatQueueTests
{
    [Fact]
    public void A_series_download_never_passes_for_the_film_version_with_the_same_id()
    {
        var visible = new HashSet<int> { 12 };
        var film = Item(versionId: 12, series: false);
        var episodes = Item(versionId: 12, series: true);

        Assert.True(QueueEndpoints.Lists(film, visible, null));
        Assert.True(QueueEndpoints.Lists(film, visible, 12));
        Assert.False(QueueEndpoints.Lists(film, visible, 13));
        Assert.False(QueueEndpoints.Lists(episodes, visible, null));
        Assert.False(QueueEndpoints.Lists(Item(versionId: 14, series: false), visible, null));
        Assert.False(QueueEndpoints.Lists(film with { Tracked = false }, visible, null));
    }

    private static QueueItemResponse Item(int versionId, bool series) =>
        new(1, "abc", "Some.Release.1080p", null, null, null, null, versionId, "1080p", null, DownloadItemStatus.Downloading, 100, 50, 0.5, 0, null, 1, "qbt", null, null, null, [], DateTime.UnixEpoch, true) { Series = series };
}
