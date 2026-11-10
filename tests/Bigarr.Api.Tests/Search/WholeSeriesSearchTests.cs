using System.Net.Http.Json;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Search;

/// <summary>
/// Decision 7: a whole-series search asks once by the show's ids, then season by season for the seasons no acceptable
/// release of that request covers, interactive and automatic alike. The mock indexer answers a series request with
/// season 1 packs only, so seasons 2–5 of Breaking Bad need the fallback.
/// </summary>
public sealed class WholeSeriesSearchTests(SonarrShadowApiFactory factory) : IClassFixture<SonarrShadowApiFactory>
{
    [Fact]
    public void Uncovered_seasons_are_those_with_a_wanted_episode_no_release_covers()
    {
        Episode E(int id, int season) => new() { Id = id, SeasonNumber = season, EpisodeNumber = id, Title = "x" };
        var wanted = new[] { E(1, 1), E(2, 1), E(3, 2), E(4, 3), E(5, 3) };
        Assert.Equal([2, 3], WholeSeriesSearch.UncoveredSeasons(wanted, new HashSet<int> { 1, 2, 4 }));
        Assert.Empty(WholeSeriesSearch.UncoveredSeasons(wanted, new HashSet<int> { 1, 2, 3, 4, 5 }));
    }

    [Fact]
    public async Task Interactive_and_automatic_series_searches_fall_back_season_by_season()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);

        var interactive = await SeriesSearchTests.SearchAsync(client, series.Id, string.Empty);
        Assert.Equal("series", interactive.Scope);
        Assert.Equal([2, 3, 4, 5], interactive.FallbackSeasons);
        Assert.Contains(interactive.Releases, r => r.Title == "Breaking.Bad.S01.1080p.BluRay.x264-DEMAND");
        Assert.Contains(interactive.Releases, r => r.Title == "Breaking.Bad.S04.1080p.BluRay.x264-DEMAND" && r.Versions.Single().Approved);

        using var scope = factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<SeriesAutomaticSearchService>();
        var versionId = series.Versions.Single().Id;
        var result = Assert.Single(await search.SearchAsync([versionId], SearchTrigger.Added, CancellationToken.None));
        Assert.Equal([2, 3, 4, 5], result.FallbackSeasons);
        Assert.Equal(result.Wanted, result.Covered);
        Assert.Equal(5, result.Picked);

        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var picks = await db.SeriesShadowPicks.AsNoTracking().Where(p => p.VersionId == versionId).Select(p => p.ReleaseTitle).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 5).Select(s => $"Breaking.Bad.S0{s}.1080p.BluRay.x264-DEMAND").Order(), picks.Order());
    }
}
