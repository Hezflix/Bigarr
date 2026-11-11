namespace Bigarr.Api.Tests.Series;

using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Refresh keeps each episode's row and every version's state on it, whatever the provider renumbers.</summary>
public sealed class SeriesRefreshTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeProvider _provider = new();
    private readonly Bigarr.Api.Features.Search.SeriesSearchQueue _searches = new();
    private readonly SeriesRefreshAlerts _alerts = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Renumbered_new_and_vanished_episodes_are_reconciled_per_version()
    {
        _provider.Show = Show(
            [0, 1],
            Ep(100, 0, 1), Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3));
        var (seriesId, hd, uhd) = await SeedAsync();

        // The default version stops wanting S01E02; the 4K one wants nothing and takes no new seasons.
        await using (var db = _database.CreateContext())
        {
            var target = await db.EpisodeTargets.SingleAsync(t => t.VersionId == hd && t.Episode.TvdbId == 102);
            target.Monitored = false;

            // S01E03 is dropped by the provider below: nobody monitors it, so it may go.
            (await db.EpisodeTargets.SingleAsync(t => t.VersionId == hd && t.Episode.TvdbId == 103)).Monitored = false;
            await db.SaveChangesAsync();
        }

        var before = await EpisodeIdsAsync(seriesId);

        _provider.Show = Show(
            [0, 1, 2],
            Ep(100, 0, 1), Ep(101, 1, 1), Ep(102, 1, 5), Ep(104, 1, 4), Ep(201, 2, 1));
        await using (var db = _database.CreateContext())
        {
            var result = await Service(db).RefreshAsync(seriesId, CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var check = _database.CreateContext();
        var episodes = await check.Episodes.Include(e => e.Targets).Where(e => e.SeriesId == seriesId).ToListAsync();

        // Same rows for the episodes TVDB still lists, renumbered in place.
        Assert.Equal(before[101], episodes.Single(e => e.TvdbId == 101).Id);
        var moved = episodes.Single(e => e.TvdbId == 102);
        Assert.Equal(before[102], moved.Id);
        Assert.Equal(5, moved.EpisodeNumber);
        Assert.False(moved.Targets.Single(t => t.VersionId == hd).Monitored);

        // Gone from the provider, gone here, base rows included.
        Assert.DoesNotContain(episodes, e => e.TvdbId == 103);
        Assert.Equal(0, await OrphansAsync(check));

        // New in a monitored season: wanted by the version that monitors the season only.
        var added = episodes.Single(e => e.TvdbId == 104);
        Assert.True(added.Targets.Single(t => t.VersionId == hd).Monitored);
        Assert.False(added.Targets.Single(t => t.VersionId == uhd).Monitored);

        // New season: follows each version's "monitor new items".
        var flags = await check.SeriesVersionSeasons.Where(s => s.Season.SeasonNumber == 2).ToDictionaryAsync(s => s.VersionId, s => s.Monitored);
        Assert.True(flags[hd]);
        Assert.False(flags[uhd]);
        var newSeason = episodes.Single(e => e.TvdbId == 201);
        Assert.True(newSeason.Targets.Single(t => t.VersionId == hd).Monitored);
        Assert.False(newSeason.Targets.Single(t => t.VersionId == uhd).Monitored);
    }

    [Fact]
    public async Task An_episode_with_a_file_stays_when_the_provider_drops_it_and_a_deleted_file_frees_its_episodes()
    {
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2));
        var (seriesId, hd, _) = await SeedAsync();

        int fileId;
        await using (var db = _database.CreateContext())
        {
            var file = new EpisodeFile { VersionId = hd, RelativePath = "Season 01/Show - S01E02.mkv", SeasonNumber = 1, QualityId = TestData.BlurayQualityId, Size = 1_000 };
            db.EpisodeFiles.Add(file);
            await db.SaveChangesAsync();
            fileId = file.Id;
            var target = await db.EpisodeTargets.SingleAsync(t => t.VersionId == hd && t.Episode.TvdbId == 102);
            target.EpisodeFileId = file.Id;
            await db.SaveChangesAsync();
        }

        _provider.Show = Show([1], Ep(101, 1, 1));
        await using (var db = _database.CreateContext())
        {
            await Service(db).RefreshAsync(seriesId, CancellationToken.None);
        }

        await using (var db = _database.CreateContext())
        {
            Assert.True(await db.Episodes.AnyAsync(e => e.TvdbId == 102));
            await db.EpisodeFiles.Where(f => f.Id == fileId).ExecuteDeleteAsync();
        }

        await using var check = _database.CreateContext();
        var freed = await check.EpisodeTargets.SingleAsync(t => t.VersionId == hd && t.Episode.TvdbId == 102);
        Assert.Null(freed.EpisodeFileId);
    }

    [Fact]
    public async Task An_episode_with_a_grab_a_shadow_pick_or_history_stays_when_the_provider_drops_it()
    {
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3), Ep(104, 1, 4), Ep(105, 1, 5));
        var (seriesId, hd, uhd) = await SeedAsync();

        await using (var db = _database.CreateContext())
        {
            var targets = await db.EpisodeTargets.Include(t => t.Episode).Where(t => t.Episode.SeriesId == seriesId).ToListAsync();
            EpisodeTarget Target(int tvdbId, int versionId) => targets.Single(t => t.Episode.TvdbId == tvdbId && t.VersionId == versionId);

            // 102: an ended grab of the 4K version; 103: a shadow pick; 104: a history line. 105 has nothing, and nobody
            // monitors it (a monitored episode is kept too).
            Target(105, hd).Monitored = false;
            var grab = new Grab { ReleaseTitle = "Demo.Show.S01E02.2160p.WEB-DL-GRP" };
            grab.AddTarget(Target(102, uhd));
            grab.TransitionTo(GrabState.Removed);
            db.Grabs.Add(grab);
            db.SeriesShadowPicks.Add(new SeriesShadowPick
            {
                SeriesTitle = "Demo Show",
                VersionName = "1080p",
                Trigger = "rss",
                ReleaseTitle = "Demo.Show.S01E03.1080p.WEB-DL-GRP",
                ReleaseGuid = "g103",
                Episodes = [new SeriesShadowPickEpisode { TargetId = Target(103, hd).Id, EpisodeId = Target(103, hd).EpisodeId, SeasonNumber = 1, EpisodeNumber = 3 }],
            });
            db.History.Add(new Bigarr.Domain.History.HistoryEvent { TargetId = Target(104, hd).Id, EventType = Bigarr.Domain.History.HistoryEventType.Grabbed, SourceTitle = "Demo.Show.S01E04.1080p.WEB-DL-GRP" });
            await db.SaveChangesAsync();
        }

        _provider.Show = Show([1], Ep(101, 1, 1));
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        await using var check = _database.CreateContext();
        var left = await check.Episodes.Where(e => e.SeriesId == seriesId).Select(e => e.TvdbId!.Value).OrderBy(id => id).ToListAsync();
        Assert.Equal([101, 102, 103, 104], left);
        Assert.Equal(0, await OrphansAsync(check));
    }

    [Fact]
    public async Task A_show_the_provider_no_longer_knows_is_flagged_deleted_and_keeps_its_episodes()
    {
        _provider.Show = Show([1], Ep(101, 1, 1));
        var (seriesId, _, _) = await SeedAsync();

        _provider.Show = null;
        await using (var db = _database.CreateContext())
        {
            await Service(db).RefreshAsync(seriesId, CancellationToken.None);
        }

        await using var check = _database.CreateContext();
        Assert.Equal(SeriesStatus.Deleted, (await check.Series.SingleAsync()).Status);
        Assert.Equal(1, await check.Episodes.CountAsync());
    }

    [Fact]
    public async Task New_episodes_follow_sonarrs_rules_and_the_recent_ones_are_searched_after_the_refresh()
    {
        _provider.Show = Show([0, 1], Ep(100, 0, 1), Ep(101, 1, 1));
        var (seriesId, hd, uhd) = await SeedAsync();
        Assert.False(_searches.Reader.TryRead(out _));

        // The default version monitors the specials season, the 4K one does not.
        await using (var db = _database.CreateContext())
        {
            var specials = await db.SeriesVersionSeasons.SingleAsync(s => s.VersionId == hd && s.Season.SeasonNumber == 0);
            specials.Monitored = true;
            await db.SaveChangesAsync();
        }

        // A new special, an episode 0 of season 2, a season 1 episode aired five days ago, one airing tonight (20:00 New
        // York, 13 hours from now), one in a month.
        var now = Now.UtcDateTime;
        _provider.Show = Show(
            [0, 1, 2],
            Ep(100, 0, 1), Ep(101, 1, 1), Ep(110, 0, 2), Ep(200, 2, 0),
            Ep(102, 1, 2) with { AirDate = DateOnly.FromDateTime(now.AddDays(-5)) },
            Ep(103, 1, 3) with { AirDate = DateOnly.FromDateTime(now) },
            Ep(104, 1, 4) with { AirDate = DateOnly.FromDateTime(now.AddDays(30)) });
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        await using var check = _database.CreateContext();
        var targets = await check.EpisodeTargets.Include(t => t.Episode).Where(t => t.VersionId == hd).ToDictionaryAsync(t => t.Episode.TvdbId!.Value);
        Assert.True(targets[110].Monitored); // Sonarr: a new special follows season 0's flag
        Assert.False(await check.EpisodeTargets.Where(t => t.VersionId == uhd && t.Episode.TvdbId == 110).Select(t => t.Monitored).SingleAsync());
        Assert.False(targets[200].Monitored); // Sonarr: episode 0 outside season 1
        Assert.True(targets[102].Monitored);
        Assert.True(targets[103].Monitored);
        Assert.True(targets[104].Monitored);

        // Sonarr's EpisodeRefreshedService: aired within 14 days or airing within a day, monitored, no file; the 4K
        // version monitors nothing new and is not searched.
        Assert.True(_searches.Reader.TryRead(out var request));
        Assert.Equal(hd, request!.VersionId);
        Assert.Equal(Bigarr.Api.Features.Search.SearchTrigger.Refreshed, request.Trigger);
        Assert.Equal([targets[102].EpisodeId, targets[103].EpisodeId], request.EpisodeIds!.Order().ToArray());
        Assert.False(_searches.Reader.TryRead(out _));
        Assert.DoesNotContain(await check.EpisodeTargets.Where(t => t.VersionId == uhd && t.Episode.TvdbId == 102).Select(t => t.Monitored).ToListAsync(), m => m);
    }

    [Fact]
    public async Task Old_episodes_appearing_on_a_series_that_had_none_are_not_monitored()
    {
        _provider.Show = Show([1]);
        var (seriesId, hd, _) = await SeedAsync();

        // The provider found the show's episodes again: two aired years ago, one airing tonight, one next week.
        var now = Now.UtcDateTime;
        _provider.Show = Show(
            [1],
            Ep(101, 1, 1),
            Ep(102, 1, 2),
            Ep(103, 1, 3) with { AirDate = DateOnly.FromDateTime(now) },
            Ep(104, 1, 4) with { AirDate = DateOnly.FromDateTime(now.AddDays(7)) });
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        await using var check = _database.CreateContext();
        var monitored = await check.EpisodeTargets.Include(t => t.Episode).Where(t => t.VersionId == hd).ToDictionaryAsync(t => t.Episode.TvdbId!.Value, t => t.Monitored);
        Assert.False(monitored[101]);
        Assert.False(monitored[102]);
        Assert.True(monitored[103]);
        Assert.True(monitored[104]);
    }

    [Fact]
    public async Task A_refresh_recomputes_air_times_in_the_networks_zone_and_spreads_a_double_bill()
    {
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2));
        var (seriesId, _, _) = await SeedAsync();

        // The show is British, its network is Netflix: Sonarr's air times read New York time (The Crown, recorded).
        _provider.Show = Show(
            [1],
            Ep(101, 1, 1) with { AirDate = new DateOnly(2017, 12, 8) },
            Ep(102, 1, 2) with { AirDate = new DateOnly(2017, 12, 8) }) with { AirTime = "08:00", OriginalCountry = "gb", NetworkCountry = "us", Runtime = 57 };
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        await using var check = _database.CreateContext();
        var times = await check.Episodes.Where(e => e.SeriesId == seriesId).OrderBy(e => e.EpisodeNumber).Select(e => e.AirDateUtc).ToListAsync();
        Assert.Equal(new DateTime(2017, 12, 8, 13, 0, 0, DateTimeKind.Utc), times[0]);
        Assert.Equal(new DateTime(2017, 12, 8, 13, 57, 0, DateTimeKind.Utc), times[1]); // Sonarr's AdjustMultiEpisodeAirTime
        Assert.Equal("us", (await check.Series.SingleAsync()).NetworkCountry);
    }

    /// <summary>
    /// The incident: a library whose episodes carry TVDB ids and no TMDB ids (as a Sonarr import leaves them), refreshed
    /// by a provider without a TVDB key, used to get TMDB's own numbering for the TVDB id, drop every TVDB episode and
    /// add TMDB's next to them. Now nothing about the episodes changes and the health check is told.
    /// </summary>
    [Fact]
    public async Task Without_a_tvdb_key_a_refresh_leaves_the_tvdb_episode_list_alone()
    {
        _provider.Show = Show([1, 2], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3), Ep(201, 2, 1), Ep(202, 2, 2));
        var (seriesId, hd, _) = await SeedAsync();
        await using (var db = _database.CreateContext())
        {
            // Nobody monitors season 2 and nothing is on disk: the old refresh deleted such episodes.
            foreach (var target in await db.EpisodeTargets.Where(t => t.Episode.SeasonNumber == 2).ToListAsync())
            {
                target.Monitored = false;
            }

            await db.SaveChangesAsync();
        }

        var before = await SnapshotAsync(seriesId);

        // What a keyless provider answers for the TVDB id: TMDB's numbering, episodes without TVDB ids, TMDB's seasons.
        _provider.CanNumberByTvdb = false;
        _provider.Show = Show([1], TmdbEp(9001, 1, 1), TmdbEp(9002, 1, 2), TmdbEp(9003, 1, 3), TmdbEp(9004, 1, 4), TmdbEp(9005, 1, 5)) with { Source = "tmdb" };
        var calls = _provider.Calls;
        await using (var db = _database.CreateContext())
        {
            var result = await Service(db).RefreshAsync(seriesId, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(503, result.Failure!.StatusCode);
        }

        Assert.Equal(calls, _provider.Calls); // no provider asked at all
        Assert.Equal(before, await SnapshotAsync(seriesId));
        Assert.Equal("No TVDB API key", _alerts.TvdbUnavailable?.Reason);

        // With a key whose TVDB has no list for the id (TMDB answers instead): kept as well.
        _provider.CanNumberByTvdb = true;
        await using (var db = _database.CreateContext())
        {
            Assert.Equal(503, (await Service(db).RefreshAsync(seriesId, CancellationToken.None)).Failure!.StatusCode);
        }

        Assert.Equal(before, await SnapshotAsync(seriesId));

        // TVDB answers again: the normal refresh, numbering by TVDB ids, the alert gone.
        _provider.Show = Show([1, 2], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3), Ep(104, 1, 4), Ep(201, 2, 1), Ep(202, 2, 2));
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        var after = await SnapshotAsync(seriesId);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Contains(after, e => e.StartsWith("104 S01E04", StringComparison.Ordinal));
        Assert.Null(_alerts.TvdbUnavailable);
        Assert.True(await HdMonitorsAsync(hd, 104));
    }

    [Fact]
    public async Task A_show_with_a_tvdb_id_is_not_added_with_tmdbs_numbering()
    {
        await using var db = _database.CreateContext();
        _provider.CanNumberByTvdb = false;
        _provider.Show = Show([1], TmdbEp(9001, 1, 1)) with { Source = "tmdb" };
        var (rootId, profileId) = await RootAndProfileAsync(db);
        var added = await Service(db).AddSeriesAsync(new AddSeriesRequest(profileId, rootId, TvdbId: 1), CancellationToken.None);

        Assert.False(added.IsSuccess);
        Assert.Equal(503, added.Failure!.StatusCode);
        Assert.False(await db.Series.AnyAsync());
    }

    [Fact]
    public async Task The_safety_net_refuses_a_refresh_that_would_replace_most_episodes()
    {
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3), Ep(104, 1, 4), Ep(105, 1, 5), Ep(106, 1, 6));
        var (seriesId, _, _) = await SeedAsync();
        await using (var db = _database.CreateContext())
        {
            foreach (var target in await db.EpisodeTargets.ToListAsync())
            {
                target.Monitored = false; // nothing protects them but the safety net
            }

            await db.SaveChangesAsync();
        }

        var before = await SnapshotAsync(seriesId);

        // Six episodes under ids the show never had: a different list, not an update.
        _provider.Show = Show([1], Ep(901, 1, 1), Ep(902, 1, 2), Ep(903, 1, 3), Ep(904, 1, 4), Ep(905, 1, 5), Ep(906, 1, 6));
        await using (var db = _database.CreateContext())
        {
            var result = await Service(db).RefreshAsync(seriesId, CancellationToken.None);
            Assert.Equal(409, result.Failure!.StatusCode);
        }

        Assert.Equal(before, await SnapshotAsync(seriesId));
        Assert.Contains("would remove 6 of 6 episodes and add 6", _alerts.Refused[seriesId], StringComparison.Ordinal);

        // Half of them renumbered under new ids is still an update (not more than half gone).
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3), Ep(904, 1, 4), Ep(905, 1, 5), Ep(906, 1, 6));
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        Assert.False(_alerts.Refused.ContainsKey(seriesId));
    }

    [Fact]
    public async Task An_episode_without_ids_matches_the_stored_one_by_numbers_and_air_date_or_title()
    {
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3));
        var (seriesId, _, _) = await SeedAsync();
        var ids = await EpisodeIdsAsync(seriesId);

        // The provider lists S01E02 without a TVDB id, a day off; S01E03 without one, same title, the date unknown.
        _provider.Show = Show(
            [1],
            Ep(101, 1, 1),
            Ep(102, 1, 2) with { TvdbId = null, TmdbId = 7002, AirDate = Ep(102, 1, 2).AirDate!.Value.AddDays(1) },
            Ep(103, 1, 3) with { TvdbId = null, TmdbId = 7003, AirDate = null });
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        await using var check = _database.CreateContext();
        var episodes = await check.Episodes.Where(e => e.SeriesId == seriesId).ToListAsync();
        Assert.Equal(3, episodes.Count);
        var second = episodes.Single(e => e.Id == ids[102]);
        Assert.Equal(102, second.TvdbId); // the TVDB id is kept, the TMDB id learnt
        Assert.Equal(7002, second.TmdbId);
        Assert.Equal(103, episodes.Single(e => e.Id == ids[103]).TvdbId);
    }

    [Fact]
    public async Task A_dropped_episode_a_version_monitors_is_kept()
    {
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3), Ep(104, 1, 4));
        var (seriesId, _, _) = await SeedAsync();

        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2), Ep(103, 1, 3));
        await using (var db = _database.CreateContext())
        {
            Assert.True((await Service(db).RefreshAsync(seriesId, CancellationToken.None)).IsSuccess);
        }

        await using var check = _database.CreateContext();
        Assert.True(await check.Episodes.AnyAsync(e => e.TvdbId == 104));
    }

    [Fact]
    public async Task The_refresh_job_without_a_tvdb_key_asks_nothing_about_shows_with_a_tvdb_id()
    {
        _provider.Show = Show([1], Ep(101, 1, 1), Ep(102, 1, 2));
        var (seriesId, _, _) = await SeedAsync();
        await using (var db = _database.CreateContext())
        {
            await db.Series.Where(s => s.Id == seriesId).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastMetadataRefreshAt, (DateTime?)null));
        }

        var before = await SnapshotAsync(seriesId);
        _provider.CanNumberByTvdb = false;
        var calls = _provider.Calls;
        await using (var db = _database.CreateContext())
        {
            var summary = await Job(db).RunAsync(CancellationToken.None);
            Assert.Contains("1 waiting for a TVDB key", summary, StringComparison.Ordinal);
        }

        Assert.Equal(calls, _provider.Calls);
        Assert.Equal(before, await SnapshotAsync(seriesId));
        Assert.NotNull(_alerts.TvdbUnavailable);
    }

    [Fact]
    public async Task The_refresh_job_spreads_a_library_never_refreshed_over_passes()
    {
        _provider.Show = Show([1], Ep(101, 1, 1));
        var (seriesId, _, _) = await SeedAsync();
        await using (var db = _database.CreateContext())
        {
            await db.Series.Where(s => s.Id == seriesId).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastMetadataRefreshAt, (DateTime?)null));
        }

        await using (var db = _database.CreateContext())
        {
            var summary = await Job(db, batch: 1).RunAsync(CancellationToken.None);
            Assert.StartsWith("1 of 1 due series refreshed this pass", summary, StringComparison.Ordinal);
        }

        // Refreshed: Sonarr's rule keeps it out of the next pass (refreshed less than 6 hours ago).
        await using (var db = _database.CreateContext())
        {
            Assert.StartsWith("Nothing due", await Job(db, batch: 1).RunAsync(CancellationToken.None), StringComparison.Ordinal);
        }
    }

    private static async Task<(int RootId, int ProfileId)> RootAndProfileAsync(BigarrDbContext db)
    {
        var root = db.RootFolders.Add(new RootFolder { Path = "/data/tv", Kind = MediaKind.Series }).Entity;
        var profile = db.QualityProfiles.Add(new QualityProfile { Name = "1080p", PreferredLanguage = "en", Cutoff = TestData.BlurayQualityId, Kind = ProfileKind.Series }).Entity;
        await db.SaveChangesAsync();
        return (root.Id, profile.Id);
    }

    private SeriesRefreshJob Job(BigarrDbContext db, int batch = 50) => new(
        db,
        Service(db),
        _provider,
        _alerts,
        Microsoft.Extensions.Options.Options.Create(new Bigarr.Api.Features.Movies.MetadataRefreshOptions { RefreshFilmsPerSecond = 0, SeriesRefreshBatchSize = batch }),
        new FixedTimeProvider(Now),
        NullLogger<SeriesRefreshJob>.Instance);

    /// <summary>Every episode as "tvdbId S01E02 tmdbId", in order: what a refresh must leave alone.</summary>
    private async Task<List<string>> SnapshotAsync(int seriesId)
    {
        await using var db = _database.CreateContext();
        return (await db.Episodes.Where(e => e.SeriesId == seriesId).ToListAsync())
            .OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ThenBy(e => e.Id)
            .Select(e => $"{e.TvdbId} S{e.SeasonNumber:00}E{e.EpisodeNumber:00} {e.TmdbId} #{e.Id}")
            .ToList();
    }

    private async Task<bool> HdMonitorsAsync(int hd, int tvdbId)
    {
        await using var db = _database.CreateContext();
        return await db.EpisodeTargets.Where(t => t.VersionId == hd && t.Episode.TvdbId == tvdbId).Select(t => t.Monitored).SingleAsync();
    }

    private static EpisodeMetadata TmdbEp(int tmdbId, int season, int number) => new()
    {
        TmdbId = tmdbId,
        SeasonNumber = season,
        EpisodeNumber = number,
        AirDate = new DateOnly(2021, 1, 1).AddDays(number),
        Title = $"TMDB episode {tmdbId}",
    };

    private async Task<(int SeriesId, int Hd, int Uhd)> SeedAsync()
    {
        await using var db = _database.CreateContext();
        var root = db.RootFolders.Add(new RootFolder { Path = "/data/tv", Kind = MediaKind.Series }).Entity;
        var profile = db.QualityProfiles.Add(new QualityProfile { Name = "1080p", PreferredLanguage = "en", Cutoff = TestData.BlurayQualityId, Kind = ProfileKind.Series }).Entity;
        if (!await db.QualityDefinitions.AnyAsync(q => q.Id == TestData.BlurayQualityId))
        {
            db.QualityDefinitions.Add(new QualityDefinition { Id = TestData.BlurayQualityId, Name = "Bluray-1080p", Source = QualitySource.Bluray, Resolution = 1080 });
        }

        await db.SaveChangesAsync();
        var service = Service(db);
        var added = await service.AddSeriesAsync(new AddSeriesRequest(profile.Id, root.Id, TvdbId: 1), CancellationToken.None);
        Assert.True(added.IsSuccess, added.Failure?.Detail);
        var uhd = await service.AddVersionAsync(added.Value!.Id, new AddSeriesVersionRequest("4k", profile.Id, Monitor: MonitoringOption.None, MonitorNewItems: NewItemMonitoring.None), CancellationToken.None);
        Assert.True(uhd.IsSuccess, uhd.Failure?.Detail);
        return (added.Value.Id, added.Value.Versions.Single(v => v.IsDefault).Id, uhd.Value!.Id);
    }

    private async Task<Dictionary<int, int>> EpisodeIdsAsync(int seriesId)
    {
        await using var db = _database.CreateContext();
        return await db.Episodes.Where(e => e.SeriesId == seriesId).ToDictionaryAsync(e => e.TvdbId!.Value, e => e.Id);
    }

    private static Task<int> OrphansAsync(BigarrDbContext db) =>
        db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS Value FROM AcquisitionTargets a WHERE a.Kind = {(int)TargetKind.Episode} AND NOT EXISTS (SELECT 1 FROM EpisodeTargets e WHERE e.Id = a.Id)")
            .SingleAsync();

    private SeriesService Service(BigarrDbContext db) =>
        new(db, _provider, XemFakes.Disabled(db, new FixedTimeProvider(Now)), new Bigarr.Api.Infrastructure.Files.RecycleBin(new FixedTimeProvider(Now), NullLogger<Bigarr.Api.Infrastructure.Files.RecycleBin>.Instance), _searches, new Bigarr.Api.Infrastructure.Events.OutboxWriter(db, new FixedTimeProvider(Now)), _alerts, new FixedTimeProvider(Now), NullLogger<SeriesService>.Instance);

    private static SeriesMetadata Show(int[] seasons, params EpisodeMetadata[] episodes) => new()
    {
        TvdbId = 1,
        TmdbId = 2,
        Title = "Demo Show",
        Year = 2020,
        AirTime = "20:00",
        OriginalCountry = "us",
        Seasons = seasons.Select(n => new SeasonMetadata(n)).ToList(),
        Episodes = episodes,
    };

    private static EpisodeMetadata Ep(int tvdbId, int season, int number) => new()
    {
        TvdbId = tvdbId,
        SeasonNumber = season,
        EpisodeNumber = number,
        AirDate = new DateOnly(2020, 1, 1).AddDays((season * 100) + number),
        Title = $"Episode {tvdbId}",
    };

    private sealed class FakeProvider : ISeriesMetadataProvider
    {
        public SeriesMetadata? Show { get; set; }

        public string Name => "fake";

        public bool CanNumberByTvdb { get; set; } = true;

        public int Calls { get; private set; }

        public Task<SeriesMetadata?> GetByTvdbIdAsync(int tvdbId, bool withEpisodes, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Show is not null && Show.TvdbId == tvdbId ? Show : null);
        }

        public Task<SeriesMetadata?> GetByTmdbIdAsync(int tmdbId, bool withEpisodes, CancellationToken cancellationToken) =>
            Task.FromResult(Show is not null && Show.TmdbId == tmdbId ? Show : null);

        public Task<IReadOnlyList<SeriesMetadata>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeriesMetadata>>(Show is null ? [] : [Show]);
    }
}
