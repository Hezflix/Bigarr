using System.Net;
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

    private async Task SetStateAsync(int grabId, GrabState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.Equal(1, await db.Grabs.Where(g => g.Id == grabId).ExecuteUpdateAsync(s => s.SetProperty(g => g.State, state)));
    }
}
