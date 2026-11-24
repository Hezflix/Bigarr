using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Infrastructure.Downloads.Mock;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Acquisition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Import;

/// <summary>Who may write a grab's state: the poll, the user's removal and the import worker never save over one another.</summary>
public sealed class GrabOwnershipTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-grab-owner-").FullName;

    public void Dispose() => TryDelete(_root);

    [Fact]
    public async Task A_grab_being_imported_is_left_to_the_import_worker_and_a_removal_waits_for_the_poll()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "owned"));

        var movie = await AddMovieAsync(client, 129, hd, root.Id); // Spirited Away
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock owned idx");
        var downloadClient = await AddMockClientAsync(client, "Mock owned qbt");
        var queueState = factory.Services.GetRequiredService<Bigarr.Api.Features.Downloads.QueueState>();
        const string removal = "?removeFromClient=false&deleteFiles=false&blocklist=false";
        try
        {
            var release = (await SearchAsync(client, movie.Id)).Releases.First(r => r.Title.Contains("1080p", StringComparison.Ordinal));
            var grab = await GrabAsync(client, release, version.Id);
            await RefreshAsync(client);
            await SetStateAsync(grab.GrabId!.Value, GrabState.Importing);

            // The worker's grab: the user cannot dismiss it from under the import...
            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/queue/{grab.GrabId}{removal}")).StatusCode);

            // ...and the poll does not count it removed when the torrent leaves the client meanwhile.
            Assert.True(factory.Services.GetRequiredService<MockDownloadClientState>().Remove(grab.DownloadId!));
            for (var poll = 0; poll < 5; poll++)
            {
                Assert.Equal(GrabState.Importing, (await RefreshAsync(client)).Items.Single(i => i.GrabId == grab.GrabId).State);
            }

            // Back to a state the user may dismiss: the removal waits for a poll in progress instead of racing it.
            await SetStateAsync(grab.GrabId.Value, GrabState.ImportFailed);
            await queueState.PollLock.WaitAsync();
            Task<HttpResponseMessage> removing;
            try
            {
                removing = client.DeleteAsync($"/api/v1/queue/{grab.GrabId}{removal}");
                Assert.NotSame(removing, await Task.WhenAny(removing, Task.Delay(TimeSpan.FromMilliseconds(500))));
            }
            finally
            {
                queueState.PollLock.Release();
            }

            Assert.Equal(HttpStatusCode.NoContent, (await removing).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Equal(GrabState.Ignored, (await db.Grabs.AsNoTracking().SingleAsync(g => g.Id == grab.GrabId)).State);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task A_manual_import_is_refused_while_the_download_is_active_and_leaves_the_grab_alone()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "manual"));

        var movie = await AddMovieAsync(client, 129, hd, root.Id); // Spirited Away
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock manual idx");
        var downloadClient = await AddMockClientAsync(client, "Mock manual qbt");
        try
        {
            var release = (await SearchAsync(client, movie.Id)).Releases.First(r => r.Title.Contains("1080p", StringComparison.Ordinal));
            var grab = await GrabAsync(client, release, version.Id);
            var grabId = grab.GrabId!.Value;
            await RefreshAsync(client);

            var filmRequest = new ManualImportRequest([new ManualImportItemRequest(Path.Combine(_root, "nothing.mkv"), version.Id, GrabId: grabId)]);
            var seriesRequest = new SeriesManualImportRequest([new SeriesManualImportItemRequest(Path.Combine(_root, "nothing.mkv"), version.Id, [1], GrabId: grabId)]);

            // The poller's and the worker's grab: a manual import does not write over it, whichever route it comes by.
            foreach (var state in new[] { GrabState.Grabbed, GrabState.Downloading, GrabState.ImportPending, GrabState.Importing })
            {
                await SetStateAsync(grabId, state);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/manualimport", filmRequest, Json)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/manualimport/series", seriesRequest, Json)).StatusCode);
                Assert.Equal(state, await StateAsync(grabId));
            }

            // A failed import is what a manual import is for: the route answers per file again.
            await SetStateAsync(grabId, GrabState.ImportFailed);
            var accepted = await client.PostAsJsonAsync("/api/v1/manualimport", filmRequest, Json);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var result = Assert.Single((await accepted.Content.ReadFromJsonAsync<List<ManualImportResultResponse>>(Json))!);
            Assert.False(result.Imported);
            Assert.Equal("File not found.", result.Reason);
            Assert.Equal(GrabState.ImportFailed, await StateAsync(grabId));
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    private async Task<GrabState> StateAsync(int grabId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return (await db.Grabs.AsNoTracking().SingleAsync(g => g.Id == grabId)).State;
    }

    private async Task SetStateAsync(int grabId, GrabState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.Equal(1, await db.Grabs.Where(g => g.Id == grabId).ExecuteUpdateAsync(s => s.SetProperty(g => g.State, state)));
    }
}
