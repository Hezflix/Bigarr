using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Domain;

public sealed class AvailabilityTests
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public void Dates_drive_the_effective_status()
    {
        var movie = NewMovie(inCinemas: new DateOnly(2026, 8, 1), digital: new DateOnly(2026, 10, 1));
        Assert.Equal(MovieStatus.InCinemas, Availability.EffectiveStatus(movie, Today));

        movie.DigitalReleaseDate = new DateOnly(2026, 9, 1);
        Assert.Equal(MovieStatus.Released, Availability.EffectiveStatus(movie, Today));

        var future = NewMovie(inCinemas: new DateOnly(2027, 6, 1));
        Assert.Equal(MovieStatus.Announced, Availability.EffectiveStatus(future, Today));
    }

    [Fact]
    public void Without_dates_a_released_tmdb_status_only_counts_as_in_cinemas()
    {
        var movie = NewMovie();
        movie.TmdbStatus = MovieStatus.Released;

        Assert.Equal(MovieStatus.InCinemas, Availability.EffectiveStatus(movie, Today));
        Assert.False(Availability.IsAvailable(movie, MinimumAvailability.Released, Today));
        Assert.True(Availability.IsAvailable(movie, MinimumAvailability.InCinemas, Today));
    }

    [Fact]
    public void Without_a_home_release_date_the_cinema_run_stands_for_one_like_radarr()
    {
        // TMDB often knows only the cinema release of an older or foreign film.
        var old = NewMovie(inCinemas: new DateOnly(1963, 8, 29));
        Assert.Equal(MovieStatus.Released, Availability.EffectiveStatus(old, Today));
        Assert.True(Availability.IsAvailable(old, MinimumAvailability.Released, Today));

        // Released 90 days after the cinema release, not a day before.
        var recent = NewMovie(inCinemas: Today.AddDays(-Availability.CinemaRunDays + 1));
        Assert.Equal(MovieStatus.InCinemas, Availability.EffectiveStatus(recent, Today));
        Assert.False(Availability.IsAvailable(recent, MinimumAvailability.Released, Today));
        recent.InCinemasDate = Today.AddDays(-Availability.CinemaRunDays);
        Assert.Equal(MovieStatus.Released, Availability.EffectiveStatus(recent, Today));

        // A home-release date still to come wins over the cinema run.
        var waiting = NewMovie(inCinemas: new DateOnly(2026, 1, 1), digital: new DateOnly(2026, 10, 1));
        Assert.Equal(MovieStatus.InCinemas, Availability.EffectiveStatus(waiting, Today));
        Assert.False(Availability.IsAvailable(waiting, MinimumAvailability.Released, Today));
    }

    [Fact]
    public void Version_status_follows_file_grab_then_availability()
    {
        var movie = NewMovie(inCinemas: new DateOnly(2027, 6, 1));
        var version = new MovieVersion { Movie = movie, Name = "1080p", FolderName = "x", MinimumAvailability = MinimumAvailability.Released };
        movie.Versions.Add(version);

        Assert.Equal(VersionStatus.Pending, Availability.StatusOf(version, hasActiveGrab: false, Today));

        version.MinimumAvailability = MinimumAvailability.Announced;
        Assert.Equal(VersionStatus.Missing, Availability.StatusOf(version, hasActiveGrab: false, Today));
        Assert.Equal(VersionStatus.Downloading, Availability.StatusOf(version, hasActiveGrab: true, Today));

        version.File = new MediaFile { RelativePath = "x.mkv" };
        Assert.Equal(VersionStatus.Available, Availability.StatusOf(version, hasActiveGrab: true, Today));
    }

    [Theory]
    [InlineData("The Matrix", 1999, "The Matrix (1999)")]
    [InlineData("Mission: Impossible", 1996, "Mission - Impossible (1996)")]
    [InlineData("What If...?", 2021, "What If... (2021)")]
    [InlineData("Face/Off", 1997, "FaceOff (1997)")]
    [InlineData("Untitled", null, "Untitled")]
    public void Folder_names_are_sanitized(string title, int? year, string expected) =>
        Assert.Equal(expected, FolderNameBuilder.Build(title, year));

    [Theory]
    [InlineData("The Matrix", "matrix")]
    [InlineData("Le Fabuleux Destin d'Amélie Poulain", "fabuleux destin d amélie poulain")]
    [InlineData("A.I. Artificial Intelligence", "a i artificial intelligence")]
    [InlineData("The", "the")]
    public void Sort_titles_drop_articles_and_punctuation(string title, string expected) =>
        Assert.Equal(expected, SortTitle.Build(title));

    [Theory]
    [InlineData(1080, "Bluray-1080p", "1080p")]
    [InlineData(2160, "Remux-2160p", "2160p")]
    [InlineData(0, "DVD", "dvd")]
    [InlineData(0, "Unknown", "default")]
    public void Version_names_come_from_the_resolution_first(int resolution, string quality, string expected) =>
        Assert.Equal(expected, VersionNaming.FromQuality(resolution, quality));

    [Fact]
    public void Version_names_are_made_unique_with_a_suffix() =>
        Assert.Equal("2160p-3", VersionNaming.Unique("2160p", ["1080p", "2160p", "2160p-2"]));

    private static Movie NewMovie(DateOnly? inCinemas = null, DateOnly? digital = null, DateOnly? physical = null) => new()
    {
        TmdbId = 1,
        Title = "Film",
        SortTitle = "film",
        DefaultRootFolder = new RootFolder { Path = "/data" },
        InCinemasDate = inCinemas,
        DigitalReleaseDate = digital,
        PhysicalReleaseDate = physical,
        TmdbStatus = MovieStatus.Announced,
    };
}
