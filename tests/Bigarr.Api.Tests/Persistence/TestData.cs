using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Tests.Persistence;

public static class TestData
{
    public const int BlurayQualityId = 7;

    public static async Task<(Movie Movie, MovieVersion Version)> SeedMovieAsync(
        BigarrDbContext db,
        int tmdbId = 603,
        string versionName = "1080p")
    {
        var root = await db.RootFolders.FirstOrDefaultAsync()
                   ?? db.RootFolders.Add(new RootFolder { Path = "/data/movies" }).Entity;

        var profile = await db.QualityProfiles.FirstOrDefaultAsync()
                      ?? db.QualityProfiles.Add(new QualityProfile { Name = "HD", PreferredLanguage = "en", Cutoff = BlurayQualityId }).Entity;

        if (!await db.QualityDefinitions.AnyAsync())
        {
            db.QualityDefinitions.Add(new QualityDefinition
            {
                Id = BlurayQualityId,
                Name = "Bluray-1080p",
                Source = QualitySource.Bluray,
                Resolution = 1080,
            });
        }

        var movie = new Movie
        {
            TmdbId = tmdbId,
            Title = "The Matrix",
            SortTitle = "matrix",
            Year = 1999,
            DefaultRootFolder = root,
        };

        var version = new MovieVersion
        {
            Movie = movie,
            Name = versionName,
            IsDefault = true,
            QualityProfile = profile,
            RootFolder = root,
            FolderName = "The Matrix (1999)",
        };

        movie.Versions.Add(version);
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return (movie, version);
    }

    public static MovieVersion NewVersion(Movie movie, string name, bool isDefault = false)
    {
        var template = movie.Versions.First();
        return new MovieVersion
        {
            MovieId = movie.Id,
            Name = name,
            IsDefault = isDefault,
            QualityProfileId = template.QualityProfileId,
            RootFolderId = template.RootFolderId,
            FolderName = template.FolderName,
        };
    }

    public static MediaFile NewFile(MovieVersion version, string relativePath) => new()
    {
        VersionId = version.Id,
        RelativePath = relativePath,
        Size = 12_000_000_000,
        QualityId = BlurayQualityId,
    };

    public static Grab NewGrab(string releaseTitle = "The.Matrix.1999.1080p.BluRay.x264-GRP") => new()
    {
        ReleaseTitle = releaseTitle,
        DownloadId = Guid.NewGuid().ToString("N"),
        Size = 12_000_000_000,
    };
}
