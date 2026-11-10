using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Series;

/// <summary>The app on the fixture catalogue (Breaking Bad, Frieren, The Daily Show), clock fixed on 2026-11-09 noon UTC.</summary>
public sealed class SeriesApiFactory() : BigarrApiFactory(null, null, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now));

public sealed class SeriesEndpointsTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    public static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const int BreakingBad = 81189;
    private const int Frieren = 424536;
    private const int DailyShowTmdb = 2224;

    [Fact]
    public async Task Lookup_supports_text_tvdb_and_tmdb_terms_and_flags_existing_series()
    {
        var client = await ClientAsync();

        var byText = await client.GetFromJsonAsync<List<SeriesLookupResult>>("/api/v1/series/lookup?term=breaking%20bad", Json);
        var hit = Assert.Single(byText!);
        Assert.Equal(BreakingBad, hit.TvdbId);
        Assert.Equal(1396, hit.TmdbId);
        Assert.Equal(SeriesType.Standard, hit.SuggestedType);
        Assert.NotNull(hit.PosterUrl);

        var byTvdb = await client.GetFromJsonAsync<List<SeriesLookupResult>>($"/api/v1/series/lookup?term=tvdb:{Frieren}", Json);
        Assert.Equal(SeriesType.Anime, Assert.Single(byTvdb!).SuggestedType);

        // An alias finds the show too.
        var byAlias = await client.GetFromJsonAsync<List<SeriesLookupResult>>("/api/v1/series/lookup?term=sousou%20no%20frieren", Json);
        Assert.Equal(Frieren, Assert.Single(byAlias!).TvdbId);

        var byTmdb = await client.GetFromJsonAsync<List<SeriesLookupResult>>($"/api/v1/series/lookup?term=tmdb:{DailyShowTmdb}", Json);
        Assert.Equal(SeriesType.Daily, Assert.Single(byTmdb!).SuggestedType);

        var empty = await client.GetAsync("/api/v1/series/lookup?term=");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var (profileId, rootId) = await SetupAsync(client);
        var added = await AddAsync(client, new AddSeriesRequest(profileId, rootId, TvdbId: BreakingBad));
        var again = await client.GetFromJsonAsync<List<SeriesLookupResult>>($"/api/v1/series/lookup?term=tvdb:{BreakingBad}", Json);
        Assert.Equal(added.Id, Assert.Single(again!).ExistingSeriesId);
    }

    [Fact]
    public async Task Adding_a_standard_series_creates_seasons_episodes_in_tvdb_order_and_a_monitored_default_version()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, BreakingBad);

        var response = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profileId, rootId, TvdbId: BreakingBad));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"seriesType\":\"standard\"", raw);
        Assert.Contains("\"ordering\":\"tvdb\"", raw);

        var series = JsonSerializer.Deserialize<SeriesResponse>(raw, Json)!;
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal(1396, series.TmdbId);
        Assert.Equal("tt0903747", series.ImdbId);
        Assert.Equal(SeriesStatus.Ended, series.Status);
        Assert.StartsWith("https://image.tmdb.org/", series.PosterUrl);
        Assert.Equal([0, 1, 2, 3, 4, 5], series.Seasons.Select(s => s.SeasonNumber));

        var version = Assert.Single(series.Versions);
        Assert.Equal("1080p", version.Name);
        Assert.True(version.IsDefault);
        Assert.True(version.SeasonFolder);
        Assert.Equal("Breaking Bad (2008)", version.FolderName);
        Assert.Equal(62, version.Statistics.TotalEpisodeCount);
        Assert.Equal(62, version.Statistics.MonitoredEpisodeCount);
        Assert.Equal(62, version.Statistics.EpisodeCount);
        Assert.Equal(0, version.Statistics.EpisodeFileCount);

        // "All" leaves the specials alone, episodes and season flags alike.
        var specials = series.Seasons.Single(s => s.SeasonNumber == 0);
        Assert.False(specials.Versions.Single().Monitored);
        Assert.All(series.Seasons.Where(s => s.SeasonNumber > 0), s => Assert.True(s.Versions.Single().Monitored));

        var episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        Assert.Equal(64, episodes.Count);
        var pilot = episodes.Single(e => e.SeasonNumber == 1 && e.EpisodeNumber == 1);
        Assert.Equal("Pilot", pilot.Title);
        Assert.Equal(new DateOnly(2008, 1, 20), pilot.AirDate);
        // 21:00 in New York, in winter: 02:00 UTC the next day.
        Assert.Equal(new DateTime(2008, 1, 21, 2, 0, 0, DateTimeKind.Utc), pilot.AirDateUtc);
        Assert.NotNull(pilot.TvdbId);
        Assert.NotNull(pilot.TmdbId);
        Assert.True(Assert.Single(pilot.Versions).Monitored);
        Assert.All(episodes.Where(e => e.IsSpecial), e => Assert.False(e.Versions.Single().Monitored));
        Assert.Equal(episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(e => e.Id), episodes.Select(e => e.Id));

        var seasonTwo = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes?seasonNumber=2", Json))!;
        Assert.Equal(13, seasonTwo.Count);

        var list = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!;
        var summary = list.Single(s => s.Id == series.Id);
        Assert.Equal(5, summary.SeasonCount);
        Assert.Equal(62, summary.Versions.Single().Statistics.MonitoredEpisodeCount);

        var conflict = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profileId, rootId, TvdbId: BreakingBad));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var sameShowByTmdb = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profileId, rootId, TmdbId: 1396));
        Assert.Equal(HttpStatusCode.Conflict, sameShowByTmdb.StatusCode);

        var unknown = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profileId, rootId, TvdbId: 999999999));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var noId = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profileId, rootId));
        Assert.Equal(HttpStatusCode.BadRequest, noId.StatusCode);
    }

    [Fact]
    public async Task An_anime_keeps_its_absolute_numbers_and_the_pilot_option_monitors_one_episode()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, Frieren);

        var series = await AddAsync(client, new AddSeriesRequest(profileId, rootId, TvdbId: Frieren, Monitor: MonitoringOption.Pilot));
        Assert.Equal(SeriesType.Anime, series.SeriesType);
        Assert.Equal("葬送のフリーレン", series.OriginalTitle);
        Assert.Contains(series.AlternativeTitles, a => a.Title == "Sousou no Frieren");

        var episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        var monitored = Assert.Single(episodes, e => e.Versions.Single().Monitored);
        Assert.Equal((1, 1), (monitored.SeasonNumber, monitored.EpisodeNumber));

        var secondSeasonOpener = episodes.Single(e => e.SeasonNumber == 2 && e.EpisodeNumber == 1);
        Assert.Equal(29, secondSeasonOpener.AbsoluteEpisodeNumber);
        Assert.All(episodes.Where(e => e.IsSpecial), e => Assert.Null(e.AbsoluteEpisodeNumber));

        var seasons = series.Seasons.ToDictionary(s => s.SeasonNumber, s => s.Versions.Single().Monitored);
        Assert.True(seasons[1]);
        Assert.False(seasons[2]);
        Assert.False(seasons[0]);

        // The type is the user's call: switched back to standard, the numbers stay.
        var update = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}", new UpdateSeriesRequest(SeriesType.Standard));
        Assert.Equal(SeriesType.Standard, (await update.Content.ReadFromJsonAsync<SeriesResponse>(Json))!.SeriesType);
    }

    [Fact]
    public async Task A_daily_show_added_by_tmdb_id_with_future_monitors_only_what_has_not_aired()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, 71256);

        var series = await AddAsync(client, new AddSeriesRequest(profileId, rootId, TmdbId: DailyShowTmdb, Monitor: MonitoringOption.Future));
        Assert.Equal(71256, series.TvdbId);
        Assert.Equal(SeriesType.Daily, series.SeriesType);

        var episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        // E127 airs on 2026-11-09 at 23:00 in New York, after noon UTC that day.
        Assert.Equal([127, 128, 129, 130], episodes.Where(e => e.Versions.Single().Monitored).Select(e => e.EpisodeNumber));
        Assert.All(episodes.Where(e => e.Versions.Single().Monitored), e => Assert.False(e.HasAired));
        Assert.True(series.Seasons.Single().Versions.Single().Monitored);
        Assert.Equal(new DateOnly(2026, 11, 9), episodes.Single(e => e.EpisodeNumber == 127).AirDate);

        var summary = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!.Single(s => s.Id == series.Id);
        Assert.Equal(new DateOnly(2026, 11, 9), summary.NextAiring);
        Assert.Equal(0, summary.Versions.Single().Statistics.EpisodeCount);
    }

    [Fact]
    public async Task Monitoring_is_per_version_while_episode_ids_are_shared()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, BreakingBad);
        var series = await AddAsync(client, new AddSeriesRequest(profileId, rootId, TvdbId: BreakingBad));
        var first = series.Versions.Single();

        var created = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions", new AddSeriesVersionRequest("4k", profileId, Monitor: MonitoringOption.None));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var fourK = (await created.Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!;
        Assert.False(fourK.IsDefault);
        Assert.Equal(first.FolderName, fourK.FolderName);
        Assert.Equal(0, fourK.Statistics.MonitoredEpisodeCount);
        Assert.Equal(62, fourK.Statistics.TotalEpisodeCount);

        var duplicate = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions", new AddSeriesVersionRequest("4k", profileId));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // A season switched on for the 4K version only.
        var season = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/versions/{fourK.Id}/seasons/3", new SeasonMonitoringRequest(true));
        Assert.Equal(HttpStatusCode.NoContent, season.StatusCode);

        var episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        Assert.All(episodes, e => Assert.Equal([first.Id, fourK.Id], e.Versions.Select(v => v.VersionId)));
        Assert.All(episodes, e => Assert.Equal(e.SeasonNumber == 3, e.Versions.Single(v => v.VersionId == fourK.Id).Monitored));
        Assert.All(episodes, e => Assert.Equal(e.SeasonNumber > 0, e.Versions.Single(v => v.VersionId == first.Id).Monitored));

        // One episode switched off for the default version only.
        var pilot = episodes.Single(e => e.SeasonNumber == 1 && e.EpisodeNumber == 1);
        var off = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/versions/{first.Id}/episodes", new EpisodeMonitoringRequest([pilot.Id], false));
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        var unknownEpisode = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/versions/{first.Id}/episodes", new EpisodeMonitoringRequest([int.MaxValue], false));
        Assert.Equal(HttpStatusCode.BadRequest, unknownEpisode.StatusCode);

        var detail = (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{series.Id}", Json))!;
        Assert.Equal(61, detail.Versions.Single(v => v.Id == first.Id).Statistics.MonitoredEpisodeCount);
        Assert.Equal(13, detail.Versions.Single(v => v.Id == fourK.Id).Statistics.MonitoredEpisodeCount);
        var seasonThree = detail.Seasons.Single(s => s.SeasonNumber == 3);
        Assert.True(seasonThree.Versions.Single(v => v.VersionId == fourK.Id).Monitored);
        Assert.Equal(13, seasonThree.Versions.Single(v => v.VersionId == fourK.Id).Statistics.MonitoredEpisodeCount);
        Assert.False(detail.Seasons.Single(s => s.SeasonNumber == 1).Versions.Single(v => v.VersionId == fourK.Id).Monitored);

        // Reapplying an option rewrites the version's flags, and only that version's.
        var latest = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions/{fourK.Id}/monitoring", new ApplyMonitoringRequest(MonitoringOption.LatestSeason));
        var afterLatest = (await latest.Content.ReadFromJsonAsync<SeriesResponse>(Json))!;
        Assert.Equal(16, afterLatest.Versions.Single(v => v.Id == fourK.Id).Statistics.MonitoredEpisodeCount);
        Assert.Equal(61, afterLatest.Versions.Single(v => v.Id == first.Id).Statistics.MonitoredEpisodeCount);
        Assert.Equal([5], afterLatest.Seasons.Where(s => s.Versions.Single(v => v.VersionId == fourK.Id).Monitored).Select(s => s.SeasonNumber));

        // Version-level switch and settings.
        var update = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/versions/{fourK.Id}", new UpdateSeriesVersionRequest(Monitored: false, SeasonFolder: false, MonitorNewItems: NewItemMonitoring.None));
        var updated = (await update.Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!;
        Assert.False(updated.Monitored);
        Assert.False(updated.SeasonFolder);
        Assert.Equal(NewItemMonitoring.None, updated.MonitorNewItems);
    }

    [Fact]
    public async Task Deleting_versions_follows_the_film_rules_and_leaves_no_orphan_target()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, BreakingBad);
        var series = await AddAsync(client, new AddSeriesRequest(profileId, rootId, TvdbId: BreakingBad));
        var first = series.Versions.Single();
        var second = (await (await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions", new AddSeriesVersionRequest("4k", profileId))).Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!;
        Assert.Equal(128, await EpisodeTargetRowsAsync(series.Id));

        var refused = await client.DeleteAsync($"/api/v1/series/{series.Id}/versions/{first.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var promoted = await client.DeleteAsync($"/api/v1/series/{series.Id}/versions/{first.Id}?promote={second.Id}");
        Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        var detail = (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{series.Id}", Json))!;
        Assert.True(Assert.Single(detail.Versions).IsDefault);
        Assert.Equal(64, await EpisodeTargetRowsAsync(series.Id));
        Assert.Equal(0, await OrphanTargetRowsAsync());

        // The last version takes the series with it.
        var last = await client.DeleteAsync($"/api/v1/series/{series.Id}/versions/{second.Id}");
        Assert.Equal(HttpStatusCode.NoContent, last.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/series/{series.Id}")).StatusCode);
        Assert.Equal(0, await EpisodeTargetRowsAsync(series.Id));
        Assert.Equal(0, await OrphanTargetRowsAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.False(await db.Episodes.AnyAsync(e => e.SeriesId == series.Id));
        Assert.False(await db.Seasons.AnyAsync(s => s.SeriesId == series.Id));
    }

    [Fact]
    public async Task Refreshing_with_unchanged_metadata_keeps_ids_and_monitoring()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, Frieren);
        var series = await AddAsync(client, new AddSeriesRequest(profileId, rootId, TvdbId: Frieren, Monitor: MonitoringOption.FirstSeason));
        var before = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;

        var refresh = await client.PostAsync($"/api/v1/series/{series.Id}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = (await refresh.Content.ReadFromJsonAsync<SeriesResponse>(Json))!;
        Assert.NotNull(refreshed.LastMetadataRefreshAt);

        var after = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        Assert.Equal(before.Select(e => (e.Id, e.Versions.Single().TargetId, e.Versions.Single().Monitored)), after.Select(e => (e.Id, e.Versions.Single().TargetId, e.Versions.Single().Monitored)));
        Assert.Equal(28, after.Count(e => e.Versions.Single().Monitored));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private async Task<int> EpisodeTargetRowsAsync(int seriesId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.EpisodeTargets.CountAsync(t => t.Episode.SeriesId == seriesId);
    }

    /// <summary>Base rows of the TPT hierarchy left without their episode row: what a database-only cascade would leave.</summary>
    private async Task<int> OrphanTargetRowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM AcquisitionTargets a WHERE a.Kind = {(int)TargetKind.Episode} AND NOT EXISTS (SELECT 1 FROM EpisodeTargets e WHERE e.Id = a.Id)")
            .SingleAsync();
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    /// <summary>Tests share one app (class fixture): each starts from a library without the show it adds.</summary>
    private static async Task DeleteIfPresentAsync(HttpClient client, int tvdbId)
    {
        var list = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!;
        foreach (var series in list.Where(s => s.TvdbId == tvdbId))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/series/{series.Id}")).StatusCode);
        }
    }

    private static async Task<(int ProfileId, int RootId)> SetupAsync(HttpClient client)
    {
        var profiles = await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json);
        var hd = profiles!.Single(p => p.Name == QualitySeeder.SeriesHdProfileName);

        var roots = (await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!.Where(r => r.Kind == MediaKind.Series).ToList();
        if (roots.Count == 0)
        {
            var dir = Directory.CreateTempSubdirectory("bigarr-series-").FullName;
            var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir, MediaKind.Series));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            roots = [(await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!];
        }

        return (hd.Id, roots[0].Id);
    }

    private static async Task<SeriesResponse> AddAsync(HttpClient client, AddSeriesRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/series", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesResponse>(Json))!;
    }
}
