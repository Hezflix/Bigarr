using System.Net.Http.Json;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Search;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Series;

/// <summary>Deleting a series with its files removes the folders, then the rows: the caller going away in between must not leave the rows behind.</summary>
public sealed class InterruptedSeriesDeleteTests
{
    [Fact]
    public async Task A_series_delete_whose_caller_goes_away_after_the_folders_are_gone_still_removes_the_series()
    {
        var clock = new TrippingClock(SeriesEndpointsTests.Now);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.Single();
        var folder = Path.Combine(version.RootFolderPath, version.FolderName);
        Directory.CreateDirectory(Path.Combine(folder, "Season 01"));
        await File.WriteAllBytesAsync(Path.Combine(folder, "Season 01", "Breaking.Bad.S01E01.1080p.BluRay.x264-GRP.mkv"), new byte[4096]);

        // The request is aborted the moment the folder is gone, before the rows are.
        using var aborted = new CancellationTokenSource();
        clock.Trip(() => !Directory.Exists(folder), aborted);
        using (var scope = factory.Services.CreateScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<SeriesService>().DeleteSeriesAsync(series.Id, aborted.Token, deleteFiles: true);
            }
            catch (OperationCanceledException)
            {
                // what the endpoint would see
            }
        }

        Assert.True(aborted.IsCancellationRequested, "the delete never reached the folder");
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!, s => s.Id == series.Id);
    }

    /// <summary>A clock that cancels a token the first time it is read once a condition holds (the service stamps its event after the folders went).</summary>
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
