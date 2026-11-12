using System.Net.Http.Json;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

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
}
