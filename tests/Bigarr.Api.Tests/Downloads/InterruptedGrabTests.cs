using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Downloads.Mock;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Downloads;

/// <summary>Once the download client holds the torrent, the grab row is written whatever the caller does: an aborted request must not leave a download nobody tracks.</summary>
public sealed class InterruptedGrabTests
{
    [Fact]
    public async Task A_grab_whose_caller_goes_away_once_the_client_has_the_torrent_is_still_recorded()
    {
        var clock = new TrippingClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var (profileId, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Directory.CreateTempSubdirectory("bigarr-interrupted-grab-").FullName);
        var movie = await AddMovieAsync(client, 603, profileId, root.Id);
        var version = movie.Versions.Single();
        await AddMockIndexerAsync(client, "Mock interrupted");
        await AddMockClientAsync(client, "Mock interrupted qbt");
        var release = (await SearchAsync(client, movie.Id)).Releases.First(r => r.Versions.Any(v => v.Approved));

        // The request is aborted the moment the client holds the torrent, before the grab row is written.
        var torrents = factory.Services.GetRequiredService<MockDownloadClientState>();
        using var aborted = new CancellationTokenSource();
        clock.Trip(() => torrents.All.Count > 0, aborted);
        var request = new GrabRequest(
            version.Id, release.IndexerId, release.Guid, release.Title, release.DownloadUrl, release.MagnetUrl, release.InfoHash,
            release.Size, release.Seeders, release.Flags, release.Protocol, release.Indexer);
        using (var scope = factory.Services.CreateScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<GrabService>().GrabAsync(request, aborted.Token);
            }
            catch (OperationCanceledException)
            {
                // what the endpoint would see
            }
        }

        Assert.True(aborted.IsCancellationRequested, "the grab never reached the send");
        var held = Assert.Single(torrents.All);
        using var check = factory.Services.CreateScope();
        var grab = await check.ServiceProvider.GetRequiredService<BigarrDbContext>().Grabs.AsNoTracking().SingleOrDefaultAsync(g => g.DownloadId == held.Hash);
        Assert.NotNull(grab);
    }

    /// <summary>A clock that cancels a token the first time it is read once a condition holds (the service stamps the grab right after the send).</summary>
    private sealed class TrippingClock(DateTimeOffset now) : TimeProvider
    {
        private Func<bool>? _when;
        private CancellationTokenSource? _cancel;

        public void Trip(Func<bool> when, CancellationTokenSource cancel)
        {
            _cancel = cancel;
            _when = when;
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (_when is { } when && _cancel is { IsCancellationRequested: false } cancel && when())
            {
                cancel.Cancel();
            }

            return now;
        }
    }
}
