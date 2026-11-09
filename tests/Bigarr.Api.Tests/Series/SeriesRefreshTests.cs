namespace Bigarr.Api.Tests.Series;

using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Refresh keeps each episode's row and every version's state on it, whatever the provider renumbers.</summary>
public sealed class SeriesRefreshTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeProvider _provider = new();

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

    private async Task<(int SeriesId, int Hd, int Uhd)> SeedAsync()
    {
        await using var db = _database.CreateContext();
        var root = db.RootFolders.Add(new RootFolder { Path = "/data/tv", Kind = MediaKind.Series }).Entity;
        var profile = db.QualityProfiles.Add(new QualityProfile { Name = "1080p", PreferredLanguage = "en", Cutoff = TestData.BlurayQualityId }).Entity;
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
        new(db, _provider, XemFakes.Disabled(db, new FixedTimeProvider(Now)), new FixedTimeProvider(Now), NullLogger<SeriesService>.Instance);

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

        public Task<SeriesMetadata?> GetByTvdbIdAsync(int tvdbId, bool withEpisodes, CancellationToken cancellationToken) =>
            Task.FromResult(Show is not null && Show.TvdbId == tvdbId ? Show : null);

        public Task<SeriesMetadata?> GetByTmdbIdAsync(int tmdbId, bool withEpisodes, CancellationToken cancellationToken) =>
            Task.FromResult(Show is not null && Show.TmdbId == tmdbId ? Show : null);

        public Task<IReadOnlyList<SeriesMetadata>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeriesMetadata>>(Show is null ? [] : [Show]);
    }
}
