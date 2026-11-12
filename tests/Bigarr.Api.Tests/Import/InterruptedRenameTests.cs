using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Import;

/// <summary>
/// A rename moves files, then saves. The caller going away in between (a closed tab, a proxy timeout) must not leave
/// the disk on the new names and the database on the old ones: nothing repairs that afterwards.
/// </summary>
public sealed class InterruptedRenameTests : IDisposable
{
    private const string DvdGroup = "69f50757054263b7bc87e32a";

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-interrupted-rename-").FullName;

    public void Dispose() => TryDelete(_root);

    [Fact]
    public async Task A_film_rename_whose_caller_goes_away_after_the_move_still_records_the_new_name()
    {
        var clock = new TrippingClock(DateTimeOffset.UtcNow);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var folder = Path.Combine(_root, "The Matrix (1999)");
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "The.Matrix.1999.1080p.BluRay.x264-GRP.mkv");
        File.WriteAllBytes(original, new byte[64 * 1024]);
        var root = await AddRootAsync(client, _root);
        await ScanAsync(client, root.Id);
        var movie = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 603);
        var renamedTo = Path.Combine(folder, "The Matrix (1999) [Bluray-1080p].mkv");

        // The request is aborted the moment the file is at its new name, before anything is saved.
        using var aborted = new CancellationTokenSource();
        clock.Trip(() => File.Exists(renamedTo), aborted);
        using (var scope = factory.Services.CreateScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<RenameService>().RenameAsync(new RenameRequest(movie.Id), aborted.Token);
            }
            catch (OperationCanceledException)
            {
                // what the endpoint would see
            }
        }

        Assert.True(aborted.IsCancellationRequested, "the rename never reached the move");
        Assert.True(File.Exists(renamedTo));
        var file = (await GetMovieAsync(client, movie.Id)).Versions.Single().File!;
        Assert.Equal("The Matrix (1999) [Bluray-1080p].mkv", file.RelativePath);
    }

    [Fact]
    public async Task An_order_switch_whose_caller_goes_away_after_the_moves_is_still_recorded_and_can_be_undone()
    {
        var clock = new TrippingClock(SeriesEndpointsTests.Now);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.Single();
        var folder = Path.Combine(version.RootFolderPath, version.FolderName);
        const string before = "Season 5/Breaking Bad - S05E09 - Blood Money Bluray-1080p.mkv";
        const string after = "Season 6/Breaking Bad - S06E01 - Blood Money Bluray-1080p.mkv";
        try
        {
            var fileId = await FileAsync(factory, series.Id, version.Id, folder, 5, 9, before);

            using var aborted = new CancellationTokenSource();
            clock.Trip(() => File.Exists(Path.Combine(folder, after)), aborted);
            using (var scope = factory.Services.CreateScope())
            {
                try
                {
                    await scope.ServiceProvider.GetRequiredService<SeriesOrderService>().ApplyAsync(series.Id, new OrderChoiceRequest(EpisodeOrder.TmdbEpisodeGroup, DvdGroup), aborted.Token);
                }
                catch (OperationCanceledException)
                {
                    // what the endpoint would see
                }
            }

            Assert.True(aborted.IsCancellationRequested, "the switch never reached the move");
            Assert.True(File.Exists(Path.Combine(folder, after)));
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                Assert.Equal(after, (await db.EpisodeFiles.AsNoTracking().SingleAsync(f => f.Id == fileId)).RelativePath);
                Assert.Equal(EpisodeOrder.TmdbEpisodeGroup, (await db.Series.AsNoTracking().SingleAsync(s => s.Id == series.Id)).Ordering);
            }

            // The journal was written with the moves: the undo puts the file back.
            var undone = await client.PostAsync($"/api/v1/series/{series.Id}/orders/undo", null);
            Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
            Assert.True(File.Exists(Path.Combine(folder, before)));
            Assert.False(File.Exists(Path.Combine(folder, after)));
        }
        finally
        {
            TryDelete(folder);
        }
    }

    [Fact]
    public async Task Two_order_switches_at_once_on_one_show_leave_one_change_that_undoes_the_moves()
    {
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: new TrippingClock(SeriesEndpointsTests.Now));
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.Single();
        var folder = Path.Combine(version.RootFolderPath, version.FolderName);
        const string before = "Season 5/Breaking Bad - S05E09 - Blood Money Bluray-1080p.mkv";
        const string after = "Season 6/Breaking Bad - S06E01 - Blood Money Bluray-1080p.mkv";
        try
        {
            await FileAsync(factory, series.Id, version.Id, folder, 5, 9, before);

            var request = new OrderChoiceRequest(EpisodeOrder.TmdbEpisodeGroup, DvdGroup);
            var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync($"/api/v1/series/{series.Id}/orders/apply", request, Json)));

            // One switch happened; the others found the show already in that order.
            Assert.Single(answers, a => a.StatusCode == HttpStatusCode.OK);
            Assert.Equal(3, answers.Count(a => a.StatusCode == HttpStatusCode.BadRequest));
            Assert.True(File.Exists(Path.Combine(folder, after)));
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var change = await db.SeriesOrderChanges.AsNoTracking().SingleAsync(c => c.SeriesId == series.Id);
                Assert.Single(change.Moves);
            }

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/series/{series.Id}/orders/undo", null)).StatusCode);
            Assert.True(File.Exists(Path.Combine(folder, before)));
        }
        finally
        {
            TryDelete(folder);
        }
    }

    /// <summary>A real file in the version folder, recorded as the episode's file (as an import would).</summary>
    private static async Task<int> FileAsync(BigarrApiFactory factory, int seriesId, int versionId, string folder, int season, int number, string relativePath)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[4096]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == versionId && t.Episode.SeriesId == seriesId && t.Episode.SeasonNumber == season && t.Episode.EpisodeNumber == number);
        var file = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = relativePath,
            SeasonNumber = season,
            Size = 4096,
            QualityId = ParserQuality.Bluray1080p.Id,
            OriginalReleaseTitle = $"Breaking.Bad.S{season:00}E{number:00}.1080p.BluRay.x264-GRP",
            ReleaseGroup = "GRP",
        };
        target.EpisodeFile = file;
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static async Task ScanAsync(HttpClient client, int rootId)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{rootId}/scan", null)).StatusCode);
        await WaitUntilAsync(async () =>
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/rootfolders/{rootId}/scan", Json))!;
            Assert.NotEqual(ScanState.Failed, status.State);
            return status.State is ScanState.Completed ? status : null;
        }, "the scan to finish");
    }

    /// <summary>
    /// A clock that cancels a token the first time it is read once a condition holds. The services read the clock
    /// after their file moves (the event they publish is stamped with it) and before their save: exactly where a
    /// request that went away would be noticed.
    /// </summary>
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
