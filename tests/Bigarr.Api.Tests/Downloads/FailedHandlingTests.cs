using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Blocklist;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Downloads;

/// <summary>Failed handling: a dead download is blocklisted, dropped from the client and replaced.</summary>
public sealed class FailedHandlingTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task A_failed_download_is_blocklisted_removed_and_replaced_by_a_search()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-failed-{Guid.NewGuid():N}"));
        var movie = await AddMovieAsync(client, 27205, hd, root.Id); // Inception
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock fail");
        var downloadClient = await AddMockClientAsync(client, "Mock qbt fail");

        try
        {
            var search = await SearchAsync(client, movie.Id);
            var approved = search.Releases.Where(r => r.Versions.Any(v => v.Approved)).ToList();
            var first = approved[0];

            var grab = await GrabAsync(client, first, version.Id);
            await RefreshAsync(client);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/downloadclients/mock/{grab.DownloadId}/fail?message=Disk%20full", null)).StatusCode);

            // The poll that sees the failure does the whole failed handling.
            var queue = await RefreshAsync(client);
            var row = Assert.Single(queue.Items, r => r.GrabId == grab.GrabId);
            Assert.Equal(GrabState.DownloadFailed, row.State);
            Assert.Equal(DownloadItemStatus.Failed, row.Status);
            Assert.Contains(row.StatusMessages, m => m.Title == "Download failed" && m.Messages[0] == "Disk full");
            var handling = Assert.Single(row.StatusMessages, m => m.Title == "Failed handling");
            Assert.Contains("Release blocklisted.", handling.Messages);
            Assert.Contains("Removed from Mock qbt fail.", handling.Messages);
            Assert.Contains("Searching for a replacement.", handling.Messages);

            var failed = Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.DownloadFailed));
            Assert.Equal("Disk full", failed.Data["reason"]);
            Assert.Equal(first.Title, failed.SourceTitle);
            Assert.Equal("true", failed.Data["blocklisted"]);
            Assert.Equal("true", failed.Data["removedFromClient"]);
            Assert.Equal("true", failed.Data["searchQueued"]);

            var blocklisted = Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Blocklisted));
            Assert.Equal(first.Title, blocklisted.SourceTitle);
            Assert.StartsWith("Download failed: Disk full", blocklisted.Data["reason"], StringComparison.Ordinal);

            var blocklist = (await client.GetFromJsonAsync<BlocklistPageResponse>($"/api/v1/blocklist?movieId={movie.Id}", Json))!;
            var entry = Assert.Single(blocklist.Items);
            Assert.Equal(first.Title, entry.SourceTitle);
            Assert.Equal(version.Id, entry.VersionId);
            Assert.Equal("Inception", entry.MovieTitle);
            Assert.Equal(first.Quality, entry.Quality);

            // The replacement search ran in the background: a different release is downloading for the same version.
            var replacement = await WaitUntilAsync(async () => (await HistoryAsync(client, movie.Id, HistoryEventType.Grabbed)).FirstOrDefault(h => h.SourceTitle != first.Title), "the replacement grab");
            Assert.Equal("search:failed", replacement.Data["trigger"]);
            Assert.Equal(version.Id, replacement.VersionId);
            Assert.Contains("1080p", replacement.SourceTitle, StringComparison.Ordinal);

            queue = await RefreshAsync(client);
            var active = Assert.Single(queue.Items);
            Assert.Equal(replacement.SourceTitle, active.Title); // the failed torrent left the client
            Assert.Equal(GrabState.Downloading, active.State);

            var refreshed = await GetMovieAsync(client, movie.Id);
            Assert.Equal(VersionStatus.Downloading, refreshed.Versions.Single().Status);

            // The blocklisted release is refused from then on, the replacement is not.
            var judged = await SearchAsync(client, movie.Id);
            Assert.Contains(judged.Releases.Single(r => r.Title == first.Title).Versions.Single().Rejections, r => r.Reason.StartsWith("Release is blocklisted", StringComparison.Ordinal));

            // A user removal with blocklist still works and does not search again on its own.
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/queue/{active.GrabId}?removeFromClient=true&deleteFiles=false&blocklist=true")).StatusCode);
            queue = await RefreshAsync(client);
            Assert.Empty(queue.Items);
            blocklist = (await client.GetFromJsonAsync<BlocklistPageResponse>($"/api/v1/blocklist?movieId={movie.Id}", Json))!;
            Assert.Equal(2, blocklist.Total);
            Assert.Equal(2, (await HistoryAsync(client, movie.Id, HistoryEventType.Grabbed)).Count);

            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/v1/queue/999999?removeFromClient=false&deleteFiles=false&blocklist=false")).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            TryDelete(root.Path);
        }
    }

    [Fact]
    public async Task A_download_stalled_past_the_threshold_is_treated_as_failed()
    {
        // The clock is the test's: the stall timer counts from the first poll that saw it.
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        using var configured = new ConfiguredBigarrApiFactory(null, new Dictionary<string, string>
        {
            ["Bigarr:Downloads:StalledTimeoutMinutes"] = "30",
            ["Bigarr:Downloads:SearchAfterFailure"] = "false",
        }, time: clock);
        var client = await ClientAsync(configured);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-stalled-{Guid.NewGuid():N}"));
        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock stall");
        var downloadClient = await AddMockClientAsync(client, "Mock qbt stall");

        try
        {
            var search = await SearchAsync(client, movie.Id);
            var release = search.Releases.First(r => r.Versions.Any(v => v.Approved));
            var grab = await GrabAsync(client, release, version.Id);
            await RefreshAsync(client);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/downloadclients/mock/{grab.DownloadId}/stall", null)).StatusCode);

            var queue = await RefreshAsync(client);
            var row = Assert.Single(queue.Items);
            Assert.Equal(GrabState.Downloading, row.State);
            Assert.Equal(DownloadItemStatus.Stalled, row.Status);
            var stalled = Assert.Single(row.StatusMessages, m => m.Title == "Stalled");
            Assert.Contains("Treated as failed in 30 minute(s).", stalled.Messages[0], StringComparison.Ordinal);

            // Ten minutes later: still waiting, the countdown says so.
            clock.Advance(TimeSpan.FromMinutes(10));
            queue = await RefreshAsync(client);
            row = Assert.Single(queue.Items);
            Assert.Equal(GrabState.Downloading, row.State);
            Assert.Contains("Treated as failed in 20 minute(s).", Assert.Single(row.StatusMessages, m => m.Title == "Stalled").Messages[0], StringComparison.Ordinal);

            // Past the threshold: failed, blocklisted, removed; no replacement search since it is switched off here.
            clock.Advance(TimeSpan.FromMinutes(21));
            queue = await RefreshAsync(client);
            row = Assert.Single(queue.Items);
            Assert.Equal(GrabState.DownloadFailed, row.State);
            Assert.Contains(row.StatusMessages, m => m.Title == "Download failed" && m.Messages[0].StartsWith("Stalled for more than 30 minutes", StringComparison.Ordinal));
            var handling = Assert.Single(row.StatusMessages, m => m.Title == "Failed handling");
            Assert.Contains("Release blocklisted.", handling.Messages);
            Assert.DoesNotContain("Searching for a replacement.", handling.Messages);

            var failed = Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.DownloadFailed));
            Assert.Equal("false", failed.Data["searchQueued"]);
            Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Blocklisted));
            Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Grabbed));

            queue = await RefreshAsync(client);
            Assert.Empty(queue.Items); // the torrent left the client
            Assert.Equal(VersionStatus.Missing, (await GetMovieAsync(client, movie.Id)).Versions.Single().Status);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            TryDelete(root.Path);
        }
    }
}
