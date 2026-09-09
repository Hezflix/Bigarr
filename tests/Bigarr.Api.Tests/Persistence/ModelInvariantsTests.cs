using Bigarr.Domain.Acquisition;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>The decisions of blueprint §3, enforced by the schema itself.</summary>
public sealed class ModelInvariantsTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_movie_has_exactly_one_default_version()
    {
        await using var db = _database.CreateContext();
        var (movie, _) = await TestData.SeedMovieAsync(db);

        db.MovieVersions.Add(TestData.NewVersion(movie, "4k", isDefault: true));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("UNIQUE constraint failed: MovieVersions.MovieId", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Additional_non_default_versions_are_accepted()
    {
        await using var db = _database.CreateContext();
        var (movie, _) = await TestData.SeedMovieAsync(db);

        db.MovieVersions.Add(TestData.NewVersion(movie, "4k"));
        db.MovieVersions.Add(TestData.NewVersion(movie, "directors-cut"));
        await db.SaveChangesAsync();

        Assert.Equal(3, await db.MovieVersions.CountAsync(v => v.MovieId == movie.Id));
    }

    [Fact]
    public async Task Version_names_are_unique_per_movie()
    {
        await using var db = _database.CreateContext();
        var (movie, _) = await TestData.SeedMovieAsync(db);

        db.MovieVersions.Add(TestData.NewVersion(movie, "1080p"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Versions_are_stored_through_the_acquisition_target_base_table()
    {
        await using var db = _database.CreateContext();
        var (_, version) = await TestData.SeedMovieAsync(db);

        await using var fresh = _database.CreateContext();
        var targets = await fresh.AcquisitionTargets.ToListAsync();

        var target = Assert.Single(targets);
        Assert.IsType<MovieVersion>(target);
        Assert.Equal(TargetKind.MovieVersion, target.Kind);
        Assert.Equal(version.Id, target.Id);
    }

    [Fact]
    public async Task Only_one_active_grab_per_target()
    {
        await using var db = _database.CreateContext();
        var (_, version) = await TestData.SeedMovieAsync(db);

        var first = TestData.NewGrab();
        first.AddTarget(version);
        db.Grabs.Add(first);
        await db.SaveChangesAsync();

        var second = TestData.NewGrab("The.Matrix.1999.1080p.WEB-DL-OTHER");
        second.AddTarget(version);
        db.Grabs.Add(second);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("UNIQUE constraint failed: GrabTargets.TargetId", ex.InnerException!.Message);
    }

    [Fact]
    public async Task A_finished_grab_frees_the_target()
    {
        await using var db = _database.CreateContext();
        var (_, version) = await TestData.SeedMovieAsync(db);

        var first = TestData.NewGrab();
        first.AddTarget(version);
        db.Grabs.Add(first);
        await db.SaveChangesAsync();

        first.TransitionTo(GrabState.Downloading);
        first.TransitionTo(GrabState.ImportPending);
        first.TransitionTo(GrabState.Importing);
        first.TransitionTo(GrabState.Imported);
        await db.SaveChangesAsync();

        var second = TestData.NewGrab("The.Matrix.1999.1080p.BluRay.REMUX-GRP");
        second.AddTarget(version);
        db.Grabs.Add(second);
        await db.SaveChangesAsync();

        await using var fresh = _database.CreateContext();
        var links = await fresh.GrabTargets.OrderBy(t => t.GrabId).ToListAsync();
        Assert.Equal([false, true], links.Select(t => t.Active));
        Assert.Throws<InvalidOperationException>(() => first.TransitionTo(GrabState.Downloading));
    }

    [Fact]
    public async Task A_version_has_at_most_one_file()
    {
        await using var db = _database.CreateContext();
        var (_, version) = await TestData.SeedMovieAsync(db);

        db.MediaFiles.Add(TestData.NewFile(version, "The Matrix (1999) Bluray-1080p.mkv"));
        await db.SaveChangesAsync();

        // A fresh context: no tracked principal, so nothing but the unique index can stop the second file.
        await using var fresh = _database.CreateContext();
        fresh.MediaFiles.Add(TestData.NewFile(version, "The Matrix (1999) Bluray-1080p PROPER.mkv"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => fresh.SaveChangesAsync());
        Assert.Contains("UNIQUE constraint failed: MediaFiles.VersionId", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Deleting_a_version_keeps_the_history_and_drops_its_blocklist()
    {
        int versionId;
        await using (var db = _database.CreateContext())
        {
            var (movie, version) = await TestData.SeedMovieAsync(db);
            versionId = version.Id;

            db.History.Add(new HistoryEvent
            {
                TargetId = version.Id,
                MovieId = movie.Id,
                TmdbId = movie.TmdbId,
                MovieTitle = movie.Title,
                VersionName = version.Name,
                EventType = HistoryEventType.Grabbed,
                SourceTitle = "The.Matrix.1999.1080p.BluRay.x264-GRP",
                Data = new Dictionary<string, string> { ["size"] = "12000000000" },
            });
            db.Blocklist.Add(new BlocklistEntry { TargetId = version.Id, SourceTitle = "bad release", ReleaseGuid = "guid-1" });
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            var version = await db.MovieVersions.SingleAsync(v => v.Id == versionId);
            db.MovieVersions.Remove(version);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            var history = Assert.Single(await db.History.ToListAsync());
            Assert.Null(history.TargetId);
            Assert.Equal("1080p", history.VersionName);
            Assert.Equal("12000000000", history.Data["size"]);
            Assert.Equal(DateTimeKind.Utc, history.CreatedAt.Kind);

            Assert.Equal(0, await db.Blocklist.CountAsync());
            Assert.Equal(0, await db.AcquisitionTargets.CountAsync());
        }
    }

    [Fact]
    public async Task Blocklist_guid_is_unique_per_target_but_missing_guids_are_not()
    {
        await using var db = _database.CreateContext();
        var (_, version) = await TestData.SeedMovieAsync(db);

        db.Blocklist.Add(new BlocklistEntry { TargetId = version.Id, SourceTitle = "a", ReleaseGuid = "guid-1" });
        db.Blocklist.Add(new BlocklistEntry { TargetId = version.Id, SourceTitle = "b" });
        db.Blocklist.Add(new BlocklistEntry { TargetId = version.Id, SourceTitle = "c" });
        await db.SaveChangesAsync();

        db.Blocklist.Add(new BlocklistEntry { TargetId = version.Id, SourceTitle = "a again", ReleaseGuid = "guid-1" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Json_columns_round_trip()
    {
        int versionId;
        await using (var db = _database.CreateContext())
        {
            var (movie, version) = await TestData.SeedMovieAsync(db);
            versionId = version.Id;

            movie.Genres = ["Action", "Science Fiction"];
            movie.Images = [new MediaImage(ImageType.Poster, "https://image.tmdb.org/t/p/original/poster.jpg")];
            movie.AlternativeTitles = [new AlternativeTitle("Matrix", "fr")];
            version.Constraints = new AcquisitionConstraints { Edition = "director'?s cut", RequiredCustomFormatIds = [1, 2] };
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            var version = await db.MovieVersions.Include(v => v.Movie).SingleAsync(v => v.Id == versionId);

            Assert.Equal(["Action", "Science Fiction"], version.Movie.Genres);
            Assert.Equal(ImageType.Poster, Assert.Single(version.Movie.Images).Type);
            Assert.Equal("fr", Assert.Single(version.Movie.AlternativeTitles).Language);
            Assert.NotNull(version.Constraints);
            Assert.Equal("director'?s cut", version.Constraints.Edition);
            Assert.Equal([1, 2], version.Constraints.RequiredCustomFormatIds);
            Assert.Equal(3, version.Constraints.Specificity);
            Assert.Equal(DateTimeKind.Utc, version.CreatedAt.Kind);
        }
    }
}
