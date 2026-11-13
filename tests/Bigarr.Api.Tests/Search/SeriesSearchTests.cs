using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Search;

/// <summary>The fixture catalogue in shadow mode, clock fixed on 2026-11-09 noon UTC.</summary>
public sealed class SeriesShadowApiFactory() : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now));

/// <summary>Interactive series search on the mock indexer: episode, season and series scopes, verdicts per version, the grab gate.</summary>
public sealed class SeriesSearchTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    public const int BreakingBad = 81189;
    public const int Frieren = 424536;
    public const int DailyShow = 71256;

    [Fact]
    public async Task A_season_search_ranks_the_pack_and_single_episodes_with_rejections_spelled_out()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);

        var result = await SearchAsync(client, series.Id, "season=1");
        Assert.Equal("season", result.Scope);
        Assert.Equal("Season 1", result.Label);

        // The best release is the 1080p BluRay pack: it covers the whole season and every version verdict is listed.
        var top = result.Releases[0];
        Assert.Equal("Breaking.Bad.S01.1080p.BluRay.x264-DEMAND", top.Title);
        Assert.True(top.FullSeason);
        Assert.True(top.MatchesSeries);
        Assert.Equal(7, top.Episodes.Count);
        Assert.Equal("S01E01–S01E07 (7 episodes)", top.EpisodesLabel);
        var verdict = Assert.Single(top.Versions);
        Assert.True(verdict.Approved, string.Join(" | ", verdict.Rejections.Select(r => r.Reason)));
        Assert.Equal(7, verdict.TargetIds.Count);
        Assert.Equal(verdict.VersionId, top.SuggestedVersionId);

        // Packs and single episodes together: a single and a double episode are accepted too.
        Assert.Contains(result.Releases, r => r.Title.Contains("S01E01.1080p", StringComparison.Ordinal) && r.Versions.Single().Approved);
        var doubleEpisode = result.Releases.Single(r => r.Title.Contains("S01E01E02", StringComparison.Ordinal));
        Assert.Equal(2, doubleEpisode.Episodes.Count);

        // Refused, with the reason.
        var uhd = result.Releases.Single(r => r.Title == "Breaking.Bad.S01.2160p.WEB-DL.DDP5.1.HDR.H.265-FLUX");
        Assert.Contains(uhd.Versions.Single().Rejections, r => r.Reason.Contains("is not wanted in profile", StringComparison.Ordinal));
        var multi = result.Releases.Single(r => r.Title.Contains("S01-S03", StringComparison.Ordinal));
        Assert.Contains(multi.Versions.Single().Rejections, r => r.Reason.StartsWith("Multi-season release", StringComparison.Ordinal));
        Assert.Equal(7 + 13 + 13, multi.Episodes.Count);
    }

    [Fact]
    public async Task An_episode_search_refuses_packs_other_episodes_and_samples()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var episode = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 5);

        var result = await SearchAsync(client, series.Id, $"episodeId={episode.Id}");
        Assert.Equal("S01E05", result.Label);

        var single = result.Releases.Single(r => r.Title == "Breaking.Bad.S01E05.1080p.WEB-DL.DDP5.1.H.264-NTb");
        Assert.True(single.Versions.Single().Approved);

        Assert.Contains(Reasons(result, "Breaking.Bad.S01.1080p.BluRay.x264-DEMAND"), r => r.StartsWith("Full season pack", StringComparison.Ordinal));
        Assert.Contains(Reasons(result, "Breaking.Bad.S01E06.1080p.WEB-DL.DDP5.1.H.264-NTb"), r => r.StartsWith("Episode wasn't requested: the release is S01E06", StringComparison.Ordinal));
        Assert.Contains(Reasons(result, "Breaking.Bad.S01E05.1080p.WEB-DL.sample-NTb"), r => r == "Sample");
    }

    [Fact]
    public async Task Anime_and_daily_episodes_are_found_by_absolute_number_and_air_date()
    {
        var client = await ClientAsync(factory);
        await SeriesSetup.MockIndexerAsync(client);

        var frieren = await SeriesSetup.AddAsync(client, Frieren);
        var s2e05 = await SeriesSetup.EpisodeAsync(client, frieren.Id, 2, 5);
        var anime = await SearchAsync(client, frieren.Id, $"episodeId={s2e05.Id}");
        var absolute = anime.Releases.First(r => r.Title.StartsWith("[SubsPlease]", StringComparison.Ordinal) && r.Title.Contains($"- {s2e05.AbsoluteEpisodeNumber} (1080p)", StringComparison.Ordinal));
        Assert.Equal(s2e05.Id, Assert.Single(absolute.Episodes).Id);
        Assert.True(absolute.Versions.Single().Approved, string.Join(" | ", absolute.Versions.Single().Rejections.Select(r => r.Reason)));

        var daily = await SeriesSetup.AddAsync(client, DailyShow);
        var day = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{daily.Id}/episodes", Json))!.Single(e => e.AirDate == new DateOnly(2026, 10, 27));
        var byDate = await SearchAsync(client, daily.Id, $"episodeId={day.Id}");
        var release = byDate.Releases.Single(r => r.Title == "The.Daily.Show.2026.10.27.1080p.WEB.h264-EDITH");
        Assert.Equal(day.Id, Assert.Single(release.Episodes).Id);
        Assert.Contains(Reasons(byDate, "The.Daily.Show.2026.10.26.1080p.WEB.h264-EDITH"), r => r.StartsWith("Episode wasn't requested", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_special_is_found_by_its_title()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var special = await SeriesSetup.EpisodeAsync(client, series.Id, 0, 2);

        var result = await SearchAsync(client, series.Id, $"episodeId={special.Id}");
        var byTitle = result.Releases.Single(r => r.Title == "Breaking.Bad.Wedding.Day.1080p.WEB-DL.DD5.1.H.264-NTb");
        Assert.True(byTitle.MatchesSeries);
        Assert.Equal(special.Id, Assert.Single(byTitle.Episodes).Id);
        Assert.True(byTitle.Versions.Single().Approved, string.Join(" | ", byTitle.Versions.Single().Rejections.Select(r => r.Reason)));
    }

    [Fact]
    public async Task Without_shadow_mode_a_series_grab_goes_to_the_client_with_the_series_category()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var id = Guid.NewGuid().ToString("N")[..8];
        var created = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest($"Mock series {id}", Bigarr.Domain.Downloads.DownloadClientImplementation.Mock, Category: $"films-{id}", SeriesCategory: $"tv-{id}"), Json);
        var downloadClient = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
        try
        {
            var result = await SearchAsync(client, series.Id, "season=2");
            var pack = result.Releases.Single(r => r.Title == "Breaking.Bad.S02.1080p.BluRay.x264-DEMAND");

            var grab = await client.PostAsJsonAsync("/api/v1/grab", SeriesSetup.Grab(pack), Json);
            Assert.Equal(HttpStatusCode.Created, grab.StatusCode);
            var grabbed = (await grab.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
            Assert.False(grabbed.Shadow);

            var torrent = factory.Services.GetRequiredService<Bigarr.Api.Infrastructure.Downloads.Mock.MockDownloadClientState>().All.Single(t => t.Hash == grabbed.DownloadId);
            Assert.Equal($"tv-{id}", torrent.Category);
            Assert.Equal(13, torrent.EpisodeLabels!.Count);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Equal(13, await db.GrabTargets.CountAsync(t => t.GrabId == grabbed.GrabId && t.Active));

            // One active download per episode: the same season again is refused.
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/grab", SeriesSetup.Grab(pack) with { Guid = "other", InfoHash = new string('b', 40) }, Json)).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    [Fact]
    public async Task Series_version_constraints_are_editable_and_judge_releases()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var versionId = series.Versions.Single().Id;

        var put = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/versions/{versionId}", new UpdateSeriesVersionRequest(Constraints: new Bigarr.Api.Features.Movies.ConstraintsDto(null, null, null, "NTb")), Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var version = (await put.Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!;
        Assert.Equal("NTb", version.Constraints!.ReleaseGroup);

        var result = await SearchAsync(client, series.Id, "season=1");
        Assert.Contains(Reasons(result, "Breaking.Bad.S01.1080p.BluRay.x264-DEMAND"), r => r == "Release group DEMAND is not NTb");
        Assert.True(result.Releases.Single(r => r.Title == "Breaking.Bad.S01E03.1080p.WEB-DL.DDP5.1.H.264-NTb").Versions.Single().Approved);

        var cleared = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/versions/{versionId}", new UpdateSeriesVersionRequest(ClearConstraints: true), Json);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!.Constraints);
    }

    private static IEnumerable<string> Reasons(SeriesSearchResponse result, string title) =>
        result.Releases.Single(r => r.Title == title).Versions.SelectMany(v => v.Rejections).Select(r => r.Reason);

    public static async Task<SeriesSearchResponse> SearchAsync(HttpClient client, int seriesId, string query)
    {
        var response = await client.GetAsync($"/api/v1/series/{seriesId}/search?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesSearchResponse>(Json))!;
    }
}

/// <summary>Shadow mode: a series grab records a pick with every episode it covers; later decisions judge against it; RSS matches episodes.</summary>
public sealed class SeriesShadowTests(SeriesShadowApiFactory factory) : IClassFixture<SeriesShadowApiFactory>
{
    [Fact]
    public async Task A_pack_grab_records_one_pick_per_version_with_every_episode_and_the_next_search_judges_against_it()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);

        var result = await SeriesSearchTests.SearchAsync(client, series.Id, "season=1");
        var pack = result.Releases[0];
        Assert.True(pack.FullSeason);

        var grab = await client.PostAsJsonAsync("/api/v1/grab", SeriesSetup.Grab(pack), Json);
        Assert.Equal(HttpStatusCode.OK, grab.StatusCode);
        var response = (await grab.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
        Assert.True(response.Shadow);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var pick = await db.SeriesShadowPicks.Include(p => p.Episodes).SingleAsync(p => p.Id == response.ShadowPickId);
            Assert.Equal(SeriesSearchTests.BreakingBad, pick.TvdbId);
            Assert.Equal("interactive", pick.Trigger);
            Assert.True(pick.FullSeason);
            Assert.Equal("Bluray-1080p", pick.Quality);
            Assert.Equal(Enumerable.Range(1, 7), pick.Episodes.OrderBy(e => e.EpisodeNumber).Select(e => e.EpisodeNumber));
            Assert.All(pick.Episodes, e => Assert.NotNull(e.TargetId));
            Assert.False(await db.Grabs.AnyAsync());

            // The clock is fixed: a decision only sees picks made before it, so this one is made a minute earlier.
            pick.PickedAt = pick.PickedAt.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        // The pick stands in for the files: the same pack is no upgrade any more, a 720p single neither.
        var again = await SeriesSeriesSearchAgain(client, series.Id);
        Assert.Contains(again.Releases.Single(r => r.Title == pack.Title).Versions.Single().Rejections, r => r.Reason.StartsWith("Not an upgrade for every episode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rss_matches_new_episodes_and_picks_the_season_pack_over_the_single()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);

        var pass = (await (await client.PostAsync("/api/v1/rss/sync", null)).Content.ReadFromJsonAsync<RssSyncResponse>(Json))!;
        Assert.True(pass.Picked >= 1);

        var releases = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases", Json))!;
        var packEntry = releases.Single(r => r.Title == "Breaking.Bad.S05.1080p.BluRay.x264-ROVERS");
        Assert.Equal(series.Id, packEntry.Series!.Id);
        Assert.Equal("S05E01–S05E16 (16 episodes)", packEntry.Episodes);
        Assert.NotNull(packEntry.ShadowPickId);

        var single = releases.Single(r => r.Title == "Breaking.Bad.S05E16.1080p.WEB-DL.DD5.1.H.264-BS");
        Assert.Equal("S05E16", single.Episodes);
        Assert.Null(single.ShadowPickId);
        Assert.StartsWith("A better release of this pass covers these episodes", single.GrabError);

        var unknown = releases.Single(r => r.Title == "Some.Unknown.Show.S01E01.1080p.WEB.h264-GRP");
        Assert.Null(unknown.Series);
        Assert.Equal("No series in the library matches 'Some Unknown Show'", unknown.UnmatchedReason);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var sighting = await db.ReleaseSightings.SingleAsync(s => s.Title == packEntry.Title);
        Assert.Equal(SeriesSearchTests.BreakingBad, sighting.TvdbId);
        Assert.Equal(packEntry.Episodes, sighting.Episodes);
        Assert.True(await db.SeriesShadowPicks.AnyAsync(p => p.ReleaseTitle == packEntry.Title && p.Trigger == "rss"));
    }

    [Fact]
    public async Task An_interactive_grab_goes_to_the_episodes_picked_whatever_the_release_matched()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var versionId = series.Versions.Single().Id;
        var s03e02 = await SeriesSetup.EpisodeAsync(client, series.Id, 3, 2);
        var s03e03 = await SeriesSetup.EpisodeAsync(client, series.Id, 3, 3);
        var s03e04 = await SeriesSetup.EpisodeAsync(client, series.Id, 3, 4);

        var result = await SeriesSearchTests.SearchAsync(client, series.Id, "season=3");
        var single = result.Releases.Single(r => r.Title == "Breaking.Bad.S03E01.1080p.WEB-DL.DDP5.1.H.264-NTb");
        Assert.Equal("S03E01", single.EpisodesLabel);

        // Matched to S03E01, sent by the user to S03E02 and S03E03: the pick holds what was picked, not the match.
        var picked = await PickAsync(client, SeriesSetup.Grab(single) with { EpisodeIds = [s03e03.Id, s03e02.Id] });
        Assert.Equal([2, 3], picked.Episodes.Where(e => e.SeasonNumber == 3).Select(e => e.EpisodeNumber).Order());
        Assert.Equal(2, picked.Episodes.Count);

        // Mapped to nothing: grabbed for the episodes picked, at any search level.
        var collection = result.Releases.Single(r => r.Title.Contains("The.Complete.Collection", StringComparison.Ordinal));
        Assert.Empty(collection.Episodes);
        var unmapped = await PickAsync(client, SeriesSetup.Grab(single) with { Guid = collection.Guid, Title = collection.Title, InfoHash = collection.InfoHash, DownloadUrl = collection.DownloadUrl, MagnetUrl = collection.MagnetUrl, VersionId = versionId, EpisodeIds = [s03e04.Id] });
        Assert.Equal((3, 4), (unmapped.Episodes.Single().SeasonNumber, unmapped.Episodes.Single().EpisodeNumber));

        // Refused: an episode of another show, nothing picked, a special a film owns.
        var other = await SeriesSetup.AddAsync(client, SeriesSearchTests.Frieren);
        var otherEpisode = await SeriesSetup.EpisodeAsync(client, other.Id, 1, 1);
        await AssertRefusedAsync(client, SeriesSetup.Grab(single) with { EpisodeIds = [s03e02.Id, otherEpisode.Id] }, "not episodes of");
        await AssertRefusedAsync(client, SeriesSetup.Grab(single) with { EpisodeIds = [] }, "at least one episode");

        var special = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!.First(e => e.SeasonNumber == 0);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var movie = new Bigarr.Domain.Movies.Movie
            {
                TmdbId = 990_001,
                Title = "A Film Special",
                SortTitle = Bigarr.Domain.Movies.SortTitle.Build("A Film Special"),
                Year = 2019,
                TmdbStatus = Bigarr.Domain.Movies.MovieStatus.Released,
                DefaultRootFolderId = await db.RootFolders.Select(r => r.Id).FirstAsync(),
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.SeriesMovieLinks.Add(new Bigarr.Domain.Tv.SeriesMovieLink { SeriesId = series.Id, EpisodeId = special.Id, MovieId = movie.Id });
            await db.SaveChangesAsync();
        }

        await AssertRefusedAsync(client, SeriesSetup.Grab(single) with { EpisodeIds = [special.Id] }, "A Film Special");
    }

    private async Task<Bigarr.Domain.Shadow.SeriesShadowPick> PickAsync(HttpClient client, GrabRequest request)
    {
        var grab = await client.PostAsJsonAsync("/api/v1/grab", request, Json);
        Assert.Equal(HttpStatusCode.OK, grab.StatusCode);
        var response = (await grab.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.SeriesShadowPicks.AsNoTracking().Include(p => p.Episodes).SingleAsync(p => p.Id == response.ShadowPickId);
    }

    private static async Task AssertRefusedAsync(HttpClient client, GrabRequest request, string why)
    {
        var response = await client.PostAsJsonAsync("/api/v1/grab", request, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(why, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static Task<SeriesSearchResponse> SeriesSeriesSearchAgain(HttpClient client, int seriesId) => SeriesSearchTests.SearchAsync(client, seriesId, "season=1");
}

internal static class SeriesSetup
{
    /// <summary>Tests share one app per class: a series is added once and reused.</summary>
    public static async Task<SeriesResponse> AddAsync(HttpClient client, int tvdbId)
    {
        var existing = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!.FirstOrDefault(s => s.TvdbId == tvdbId);
        if (existing is not null)
        {
            return (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{existing.Id}", Json))!;
        }

        var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var hd = profiles.Single(p => p.Name == QualitySeeder.SeriesHdProfileName);
        var roots = (await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!.Where(r => r.Kind == MediaKind.Series).ToList();
        if (roots.Count == 0)
        {
            var dir = Directory.CreateTempSubdirectory("bigarr-series-search-").FullName;
            var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir, MediaKind.Series));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            roots = [(await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!];
        }

        var response = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(hd.Id, roots[0].Id, TvdbId: tvdbId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesResponse>(Json))!;
    }

    public static async Task MockIndexerAsync(HttpClient client)
    {
        var indexers = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Indexers.IndexerResponse>>("/api/v1/indexers", Json))!;
        if (indexers.Count == 0)
        {
            await AddMockIndexerAsync(client, "Mock series");
        }
    }

    public static async Task<EpisodeResponse> EpisodeAsync(HttpClient client, int seriesId, int season, int number) =>
        (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{seriesId}/episodes", Json))!.Single(e => e.SeasonNumber == season && e.EpisodeNumber == number);

    public static GrabRequest Grab(SeriesReleaseResponse release) => new(
        release.SuggestedVersionId!.Value,
        release.IndexerId,
        release.Guid,
        release.Title,
        release.DownloadUrl,
        release.MagnetUrl,
        release.InfoHash,
        release.Size,
        release.Seeders,
        release.Flags,
        release.Protocol,
        release.Indexer,
        release.PublishDate,
        release.Episodes.Select(e => e.Id).ToList());
}
