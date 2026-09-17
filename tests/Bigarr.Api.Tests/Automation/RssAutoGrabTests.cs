using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.History;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Automation;

/// <summary>The M6 acceptance loop: a monitored film gets downloaded through the RSS feed with nobody clicking.</summary>
public sealed class RssAutoGrabTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task The_rss_pass_grabs_the_best_release_per_version_once()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-rss-{Guid.NewGuid():N}"));
        var matrix = await AddMovieAsync(client, 603, hd, root.Id);
        var inception = await AddMovieAsync(client, 27205, hd, root.Id);
        var indexer = await AddMockIndexerAsync(client, "Mock rss grab");
        var downloadClient = await AddMockClientAsync(client, "Mock rss qbt");

        try
        {
            // Run it as the scheduler would: through the task.
            var run = await client.PostAsync($"/api/v1/system/tasks/{RssSyncJob.JobName}/run", null);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            var outcome = (await run.Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
            Assert.True(outcome.Success, outcome.Error);
            Assert.Contains("2 grabbed", outcome.Summary, StringComparison.Ordinal);

            var releases = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases?matched=true", Json))!;
            var grabbed = releases.Where(r => r.GrabId is not null).ToList();
            Assert.Equal(2, grabbed.Count);
            Assert.All(grabbed, r => Assert.Equal("Bluray-1080p", r.Quality)); // beats the WEB-DL of the same pass
            Assert.Contains(grabbed, r => r.Movie!.Id == matrix.Id);
            Assert.Contains(grabbed, r => r.Movie!.Id == inception.Id);
            var losers = releases.Where(r => r.AttributedVersionId is not null && r.GrabId is null).ToList();
            Assert.Equal(2, losers.Count);
            Assert.All(losers, r => Assert.Contains("A better release of this pass was grabbed", r.GrabError, StringComparison.Ordinal));

            var status = (await client.GetFromJsonAsync<RssStatusResponse>("/api/v1/rss", Json))!;
            Assert.Equal(2, status.Grabbed);

            var queue = await RefreshAsync(client);
            Assert.Equal(2, queue.Items.Count);
            Assert.All(queue.Items, row => Assert.Equal(GrabState.Downloading, row.State));

            var history = await HistoryAsync(client, matrix.Id, HistoryEventType.Grabbed);
            Assert.Equal("rss", Assert.Single(history).Data["trigger"]);

            // The same feed again: nothing new, nothing grabbed twice.
            var again = (await (await client.PostAsync("/api/v1/rss/sync", null)).Content.ReadFromJsonAsync<RssSyncResponse>(Json))!;
            Assert.Equal(0, again.New);
            Assert.Equal(0, again.Grabbed);

            var tasks = (await client.GetFromJsonAsync<List<ScheduledTaskResponse>>("/api/v1/system/tasks", Json))!;
            var rss = Assert.Single(tasks, t => t.Name == RssSyncJob.JobName);
            Assert.NotNull(rss.LastRunAt);
            Assert.True(rss.LastSucceeded);
            Assert.Equal(15, rss.IntervalMinutes);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{matrix.Id}");
            await client.DeleteAsync($"/api/v1/movies/{inception.Id}");
            TryDelete(root.Path);
        }
    }
}
