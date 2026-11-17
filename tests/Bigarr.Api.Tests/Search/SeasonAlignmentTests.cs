using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Search;

/// <summary>Its own app (shadow mode, fixed clock): the files seeded here must not meet other classes' picks.</summary>
public sealed class SeasonAlignmentApiFactory() : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now));

/// <summary>"Align season" on the mock indexer: the dry run names the season's group and the off-group episodes, the search replaces them with the group's release.</summary>
public sealed class SeasonAlignmentTests(SeasonAlignmentApiFactory factory) : IClassFixture<SeasonAlignmentApiFactory>
{
    [Fact]
    public async Task Aligning_a_season_previews_then_takes_the_season_groups_release_for_the_off_group_episode()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var version = series.Versions.Single();

        // Season 2: four episodes from NTb, the fifth from another group, all WEB-DL 1080p.
        for (var n = 1; n <= 5; n++)
        {
            await FileAsync(series.Id, version.Id, 2, n, n <= 4 ? "NTb" : "OTHER");
        }

        // The series page says which seasons have something to align.
        var page = (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{series.Id}", Json))!;
        var state = page.Seasons.Single(s => s.SeasonNumber == 2).Versions.Single();
        Assert.Equal(("NTb", 1), (state.SeasonGroup, state.OffGroupEpisodes));
        Assert.Null(page.Seasons.Single(s => s.SeasonNumber == 1).Versions.Single().SeasonGroup);

        // An ordinary search of that episode finds the same quality from NTb: not an upgrade, nothing taken.
        var e05 = await SeriesSetup.EpisodeAsync(client, series.Id, 2, 5);
        var plain = (await (await client.PostAsync($"/api/v1/series/{series.Id}/episodes/{e05.Id}/search", null)).Content.ReadFromJsonAsync<List<SeriesAutomaticSearchResult>>(Json))!;
        Assert.Equal(0, plain.Single().Picked);

        // The dry run: NTb is the group (4 files), S02E05 would be replaced. Nothing is searched.
        var url = $"/api/v1/series/{series.Id}/versions/{version.Id}/seasons/2/alignment";
        var preview = (await client.GetFromJsonAsync<SeasonAlignmentResponse>(url, Json))!;
        Assert.Equal("NTb", preview.SeasonGroup);
        Assert.Null(preview.Reason);
        Assert.Equal([new SeasonGroupCount("NTb", 4), new SeasonGroupCount("OTHER", 1)], preview.Groups);
        var episode = Assert.Single(preview.Episodes);
        Assert.Equal(("S02E05", "OTHER", "WEBDL-1080p"), (episode.Label, episode.CurrentGroup, episode.Quality));

        // Align: the episode search keeps NTb's releases only and takes one.
        var aligned = await client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, aligned.StatusCode);
        var result = (await aligned.Content.ReadFromJsonAsync<SeasonAlignmentResult>(Json))!;
        Assert.Equal(1, result.Search!.Picked);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var pick = await db.SeriesShadowPicks.Include(p => p.Episodes).OrderByDescending(p => p.Id).FirstAsync();
            Assert.Equal("NTb", pick.ReleaseGroup);
            Assert.Equal(e05.Id, Assert.Single(pick.Episodes).EpisodeId);
        }

        // Seasons without a group say why; specials never get one.
        var none = (await client.GetFromJsonAsync<SeasonAlignmentResponse>($"/api/v1/series/{series.Id}/versions/{version.Id}/seasons/1/alignment", Json))!;
        Assert.Equal((null, "No file of the season has a known release group"), (none.SeasonGroup, none.Reason));
        var specials = (await client.GetFromJsonAsync<SeasonAlignmentResponse>($"/api/v1/series/{series.Id}/versions/{version.Id}/seasons/0/alignment", Json))!;
        Assert.Equal("Specials keep whatever group each one comes from", specials.Reason);
        var nothing = (await (await client.PostAsync($"/api/v1/series/{series.Id}/versions/{version.Id}/seasons/1/alignment", null)).Content.ReadFromJsonAsync<SeasonAlignmentResult>(Json))!;
        Assert.Null(nothing.Search);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/series/{series.Id}/versions/999999/seasons/2/alignment")).StatusCode);
    }

    private async Task FileAsync(int seriesId, int versionId, int season, int number, string group)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == versionId && t.Episode.SeriesId == seriesId && t.Episode.SeasonNumber == season && t.Episode.EpisodeNumber == number);
        target.EpisodeFile = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = $"Season {season}/Breaking Bad - S{season:00}E{number:00}.mkv",
            SeasonNumber = season,
            Size = 1_900_000_000,
            QualityId = ParserQuality.WEBDL1080p.Id,
            OriginalReleaseTitle = $"Breaking.Bad.S{season:00}E{number:00}.1080p.WEB-DL.DDP5.1.H.264-{group}",
            ReleaseGroup = group,
        };
        await db.SaveChangesAsync();
    }
}
