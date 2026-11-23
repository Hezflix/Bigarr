using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Series;

/// <summary>A refresh loads the whole show, asks the providers and saves what it loaded: it cannot overlap another writer of the show.</summary>
public sealed class ConcurrentSeriesWritersTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    [Fact]
    public async Task Refreshes_and_a_new_version_at_once_leave_each_episode_once_with_a_target_per_version()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var uhd = profiles.Single(p => p.Name == QualitySeeder.SeriesUhdProfileName);

        // Season 5 is gone from Bigarr: the refresh has episodes to bring back while the other writers run.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AcquisitionTargets WHERE Id IN (SELECT t.Id FROM EpisodeTargets t JOIN Episodes e ON e.Id = t.EpisodeId WHERE e.SeriesId = {series.Id} AND e.SeasonNumber = 5)");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Episodes WHERE SeriesId = {series.Id} AND SeasonNumber = 5");
        }

        var calls = new List<Task<HttpResponseMessage>>
        {
            client.PostAsync($"/api/v1/series/{series.Id}/refresh", null),
            client.PostAsync($"/api/v1/series/{series.Id}/refresh", null),
            client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions", new AddSeriesVersionRequest("4k-at-once", uhd.Id), Json),
            client.PostAsync($"/api/v1/series/{series.Id}/refresh", null),
        };
        var answers = await Task.WhenAll(calls);
        Assert.All(answers, a => Assert.True(a.IsSuccessStatusCode, $"{a.RequestMessage!.RequestUri}: {(int)a.StatusCode}"));

        using var check = factory.Services.CreateScope();
        var read = check.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var episodes = await read.Episodes.AsNoTracking().Where(e => e.SeriesId == series.Id).Select(e => new { e.Id, e.SeasonNumber, e.EpisodeNumber }).ToListAsync();
        Assert.Contains(episodes, e => e.SeasonNumber == 5);
        Assert.Equal(episodes.Count, episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).Distinct().Count());

        var versions = await read.SeriesVersions.AsNoTracking().Where(v => v.SeriesId == series.Id).Select(v => v.Id).ToListAsync();
        Assert.Equal(2, versions.Count);
        var pairs = await read.EpisodeTargets.AsNoTracking().Where(t => versions.Contains(t.VersionId)).Select(t => new { t.VersionId, t.EpisodeId }).ToListAsync();
        Assert.Equal(episodes.Count * versions.Count, pairs.Distinct().Count());
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Fact]
    public async Task A_reassign_waits_for_the_writer_that_holds_the_show()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.OrderBy(v => v.Id).First(); // the other test in this class may have added a version
        var second = (await SeriesSetup.EpisodeAsync(client, series.Id, 1, 2)).Id;

        int fileId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == version.Id && t.Episode.SeriesId == series.Id && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 1);
            var file = new EpisodeFile
            {
                VersionId = version.Id,
                RelativePath = "Season 1/Breaking Bad - S01E01 - Pilot Bluray-1080p.mkv",
                SeasonNumber = 1,
                Size = 4096,
                QualityId = ParserQuality.Bluray1080p.Id,
                OriginalReleaseTitle = "Breaking.Bad.S01E01.1080p.BluRay.x264-GRP",
                ReleaseGroup = "GRP",
            };
            target.EpisodeFile = file;
            await db.SaveChangesAsync();
            fileId = file.Id;
        }

        // Another writer of the show holds its turn.
        using var reassignScope = factory.Services.CreateScope();
        var order = reassignScope.ServiceProvider.GetRequiredService<SeriesOrderService>();
        var turn = await SeriesFileGate.EnterAsync(series.Id, CancellationToken.None);
        Task<Bigarr.Api.Infrastructure.Result<OrderFileResult>> reassign;
        try
        {
            reassign = order.ReassignFileAsync(series.Id, fileId, new ReassignEpisodeFileRequest([second], Rename: false), CancellationToken.None);
            Assert.NotSame(reassign, await Task.WhenAny(reassign, Task.Delay(300)));
        }
        finally
        {
            turn.Dispose();
        }

        var result = await reassign;
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task An_episode_file_delete_waits_for_the_writer_that_holds_the_show()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.OrderBy(v => v.Id).First();
        var folder = Path.Combine(version.RootFolderPath, version.FolderName);
        const string relative = "Season 1/Breaking Bad - S01E03 - Bluray-1080p.mkv";
        var path = Path.Combine(folder, relative);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, new byte[4096]);
            int fileId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == version.Id && t.Episode.SeriesId == series.Id && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 3);
                var file = new EpisodeFile
                {
                    VersionId = version.Id,
                    RelativePath = relative,
                    SeasonNumber = 1,
                    Size = 4096,
                    QualityId = ParserQuality.Bluray1080p.Id,
                    OriginalReleaseTitle = "Breaking.Bad.S01E03.1080p.BluRay.x264-GRP",
                    ReleaseGroup = "GRP",
                };
                target.EpisodeFile = file;
                await db.SaveChangesAsync();
                fileId = file.Id;
            }

            // Another writer of the show holds its turn.
            using var deleteScope = factory.Services.CreateScope();
            var manual = deleteScope.ServiceProvider.GetRequiredService<SeriesManualImportService>();
            var turn = await SeriesFileGate.EnterAsync(series.Id, CancellationToken.None);
            Task<Bigarr.Api.Infrastructure.Result<bool>> delete;
            try
            {
                delete = manual.DeleteEpisodeFileAsync(series.Id, fileId, CancellationToken.None);
                Assert.NotSame(delete, await Task.WhenAny(delete, Task.Delay(300)));
                Assert.True(File.Exists(path), "the delete touched the file while another writer held the show");
            }
            finally
            {
                turn.Dispose();
            }

            var result = await delete;
            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(path));
        }
        finally
        {
            TryDelete(folder);
        }
    }
}
