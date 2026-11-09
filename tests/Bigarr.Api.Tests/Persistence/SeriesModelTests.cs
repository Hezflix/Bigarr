namespace Bigarr.Api.Tests.Persistence;

using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

/// <summary>The series model's rules, enforced by the schema: one default per series, shared episodes, TPT targets.</summary>
public sealed class SeriesModelTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_series_has_exactly_one_default_version_and_unique_version_names()
    {
        await using var db = _database.CreateContext();
        var (series, _, _) = await SeedAsync(db);

        db.SeriesVersions.Add(Version(series, "4k", isDefault: true));
        var twoDefaults = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("UNIQUE constraint failed: SeriesVersions.SeriesId", twoDefaults.InnerException!.Message);
        db.ChangeTracker.Clear();

        db.SeriesVersions.Add(Version(series, "1080p"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.SeriesVersions.Add(Version(series, "4k"));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.SeriesVersions.CountAsync(v => v.SeriesId == series.Id));
    }

    [Fact]
    public async Task Tvdb_ids_are_unique_but_shows_without_one_can_coexist()
    {
        await using var db = _database.CreateContext();
        var (series, _, _) = await SeedAsync(db);

        db.Series.Add(new Series { TvdbId = series.TvdbId, Title = "Twin", SortTitle = "twin", DefaultRootFolderId = series.DefaultRootFolderId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.Series.Add(new Series { TmdbId = 501, Title = "TMDB only A", SortTitle = "a", DefaultRootFolderId = series.DefaultRootFolderId });
        db.Series.Add(new Series { TmdbId = 502, Title = "TMDB only B", SortTitle = "b", DefaultRootFolderId = series.DefaultRootFolderId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Episode_targets_are_stored_through_the_acquisition_target_base_table_and_resolve_the_version_profile()
    {
        await using (var db = _database.CreateContext())
        {
            await SeedAsync(db);
        }

        await using var fresh = _database.CreateContext();
        var target = Assert.IsType<EpisodeTarget>(Assert.Single(await fresh.AcquisitionTargets.ToListAsync()));
        Assert.Equal(TargetKind.Episode, target.Kind);

        var loaded = await fresh.EpisodeTargets.Include(t => t.Version).ThenInclude(v => v.QualityProfile).SingleAsync();
        IAcquisitionTarget asTarget = loaded;
        Assert.Equal(loaded.Version.QualityProfileId, asTarget.QualityProfileId);
        Assert.True(asTarget.UpgradesAllowed);
    }

    [Fact]
    public async Task A_version_wants_an_episode_once_and_tvdb_episode_ids_are_unique()
    {
        await using var db = _database.CreateContext();
        var (_, version, episode) = await SeedAsync(db);

        db.EpisodeTargets.Add(new EpisodeTarget { VersionId = version.Id, EpisodeId = episode.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.Episodes.Add(new Episode { SeriesId = episode.SeriesId, SeasonId = episode.SeasonId, TvdbId = episode.TvdbId, SeasonNumber = 1, EpisodeNumber = 2, Title = "Dup" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task One_file_can_hold_several_episodes()
    {
        await using var db = _database.CreateContext();
        var (series, version, first) = await SeedAsync(db);
        var second = new Episode { Series = series, SeasonId = first.SeasonId, TvdbId = 1002, SeasonNumber = 1, EpisodeNumber = 2, Title = "Two" };
        db.Episodes.Add(second);
        db.EpisodeTargets.Add(new EpisodeTarget { Version = version, Episode = second });
        var file = new EpisodeFile { Version = version, RelativePath = "Season 01/Show - S01E01E02.mkv", SeasonNumber = 1, QualityId = TestData.BlurayQualityId };
        db.EpisodeFiles.Add(file);
        await db.SaveChangesAsync();

        foreach (var target in await db.EpisodeTargets.ToListAsync())
        {
            target.EpisodeFile = file;
        }

        await db.SaveChangesAsync();

        await using var fresh = _database.CreateContext();
        var loaded = await fresh.EpisodeFiles.Include(f => f.Episodes).SingleAsync();
        Assert.Equal(2, loaded.Episodes.Count);
    }

    private static async Task<(Series Series, SeriesVersion Version, Episode Episode)> SeedAsync(Bigarr.Api.Infrastructure.Persistence.BigarrDbContext db)
    {
        var root = db.RootFolders.Add(new RootFolder { Path = "/data/tv", Kind = MediaKind.Series }).Entity;
        var profile = db.QualityProfiles.Add(new QualityProfile { Name = "1080p", PreferredLanguage = "en", Cutoff = TestData.BlurayQualityId }).Entity;
        if (!await db.QualityDefinitions.AnyAsync(q => q.Id == TestData.BlurayQualityId))
        {
            db.QualityDefinitions.Add(new QualityDefinition { Id = TestData.BlurayQualityId, Name = "Bluray-1080p", Source = QualitySource.Bluray, Resolution = 1080 });
        }

        var series = new Series { TvdbId = 81189, TmdbId = 1396, Title = "Breaking Bad", SortTitle = "breaking bad", DefaultRootFolder = root };
        var season = new Season { Series = series, SeasonNumber = 1 };
        var episode = new Episode { Series = series, Season = season, TvdbId = 349232, SeasonNumber = 1, EpisodeNumber = 1, Title = "Pilot" };
        var version = new SeriesVersion { Series = series, Name = "1080p", IsDefault = true, QualityProfile = profile, RootFolder = root, FolderName = "Breaking Bad (2008)" };
        version.Episodes.Add(new EpisodeTarget { Version = version, Episode = episode });
        db.Series.Add(series);
        db.Episodes.Add(episode);
        db.SeriesVersions.Add(version);
        await db.SaveChangesAsync();
        return (series, version, episode);
    }

    private static SeriesVersion Version(Series series, string name, bool isDefault = false) => new()
    {
        SeriesId = series.Id,
        Name = name,
        IsDefault = isDefault,
        QualityProfileId = series.Versions.First().QualityProfileId,
        RootFolderId = series.DefaultRootFolderId,
        FolderName = series.Title,
    };
}
