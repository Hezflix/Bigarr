using System.Net.Http.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Movies;

/// <summary>
/// The films' side of the calendar: each cinema, digital and physical date of a monitored version, with the library's
/// status for that version, and Radarr's <c>/radarr/api/v3/calendar</c> read from the same dates.
/// </summary>
public sealed class MovieCalendarTests : IDisposable
{
    private const int Hdtv720p = 4;

    /// <summary>Sunday 2026-11-15, 10:00 UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly ConfiguredBigarrApiFactory _factory = new(null, null, time: new MutableTimeProvider(Now));

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Each_release_date_in_the_range_is_an_entry_with_the_library_status_of_its_version()
    {
        var session = await SessionAsync(_factory);
        await SeedAsync();

        var week = await ListAsync(session, "start=2026-11-09&end=2026-11-16");
        Assert.Equal(
            [
                ("Missing Film", ReleaseType.Physical, new DateOnly(2026, 11, 10), VersionStatus.Missing),
                ("Cinema Film", ReleaseType.InCinemas, new DateOnly(2026, 11, 11), VersionStatus.Pending),
                ("Grabbed Film", ReleaseType.Digital, new DateOnly(2026, 11, 12), VersionStatus.Downloading),
                ("Digital Film", ReleaseType.Digital, new DateOnly(2026, 11, 13), VersionStatus.Available),
            ],
            week.Select(e => (e.Title, e.ReleaseType, e.Date, e.Status)).ToList());
        var onDisk = week.Single(e => e.Title == "Digital Film");
        Assert.True(onDisk.HasFile);
        Assert.Equal("HDTV-720p", onDisk.Quality);

        // Below the profile's Bluray-1080p cutoff: the library's violet chip. No file, no verdict.
        Assert.False(onDisk.CutoffMet);
        Assert.True(week.Where(e => !e.HasFile).All(e => e.CutoffMet is null));
        Assert.True(week.All(e => e.IsDefaultVersion && e.Monitored));

        // The unmonitored 4K version of the film in cinemas shows only when asked, after the default version.
        var all = await ListAsync(session, "start=2026-11-09&end=2026-11-16&unmonitored=true");
        Assert.Equal(["1080p", "4K"], all.Where(e => e.Title == "Cinema Film").Select(e => e.VersionName).ToList());
        Assert.False(all.Single(e => e.VersionName == "4K").Monitored);

        // The end is excluded; a film's later dates show on their own days.
        Assert.DoesNotContain(await ListAsync(session, "start=2026-11-09&end=2026-11-13"), e => e.Title == "Digital Film");
        Assert.Equal(
            [ReleaseType.Digital, ReleaseType.Physical],
            (await ListAsync(session, "start=2026-11-13&end=2026-11-25")).Where(e => e.Title == "Digital Film").Select(e => e.ReleaseType).ToList());
    }

    [Fact]
    public async Task Radarr_calendar_lists_the_films_with_a_date_in_the_range_both_ends_included()
    {
        var session = await SessionAsync(_factory);
        await SeedAsync();
        var (client, _) = await KeyClientAsync(_factory, session, "calendar", "default");

        // 10:00 on the 12th starts on the 13th (a date is midnight UTC); the 24th is included.
        var films = await client.GetFromJsonAsync<List<RadarrCalendarMovie>>("/radarr/api/v3/calendar?start=2026-11-12T10:00:00Z&end=2026-11-24T00:00:00Z", Json);
        Assert.Equal(["Digital Film"], films!.Select(f => f.Title).ToList());

        // No range: today and the next two days, Radarr's default; nothing of the seed falls there.
        Assert.Empty((await client.GetFromJsonAsync<List<RadarrCalendarMovie>>("/radarr/api/v3/calendar", Json))!);

        var week = await client.GetFromJsonAsync<List<RadarrCalendarMovie>>("/radarr/api/v3/calendar?start=2026-11-09&end=2026-11-15", Json);
        Assert.Equal(["Cinema Film", "Digital Film", "Grabbed Film", "Missing Film"], week!.Select(f => f.Title).ToList());
        Assert.Equal(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), week!.Single(f => f.Title == "Cinema Film").InCinemas);
    }

    [Fact]
    public async Task The_ical_feed_has_an_all_day_event_per_film_release_date()
    {
        var session = await SessionAsync(_factory);
        await SeedAsync();

        // 8 November to 13 December by default (7 days back, 28 ahead): one all-day event per film and date, the
        // unmonitored 4K version folded into its film, confirmed once the file is on disk.
        var ics = await session.GetStringAsync("/api/v1/calendar/bigarr.ics?unmonitored=true");
        var cinema = "SUMMARY:Cinema Film (2026) - In cinemas\r\nSTATUS:TENTATIVE";
        Assert.Contains(cinema, ics, StringComparison.Ordinal);
        Assert.Equal(1, ics.Split(cinema).Length - 1);
        Assert.Contains("DTSTART;VALUE=DATE:20261111\r\nDTEND;VALUE=DATE:20261112\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Digital Film (2026) - Digital release\r\nSTATUS:CONFIRMED", ics, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Digital Film (2026) - Physical release", ics, StringComparison.Ordinal);
        Assert.Contains("UID:bigarr-movie-", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("Cinema Film (2026) - Digital release", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("Old Film", ics, StringComparison.Ordinal);
    }

    private static async Task<List<CalendarMovieResponse>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<List<CalendarMovieResponse>>($"/api/v1/calendar/movies?{query}", Json))!;

    /// <summary>A film in cinemas with an unmonitored 4K version, one on disk, one missing, one downloading, one out of range.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var hd = await db.QualityProfiles.SingleAsync(p => p.Name == QualitySeeder.HdProfileName);
        var root = new RootFolder { Path = "/data/movies" };
        db.RootFolders.Add(root);

        var tmdbId = 950_000;
        Movie Film(string title, DateOnly? inCinemas, DateOnly? digital, DateOnly? physical, params (string Name, bool Monitored)[] versions)
        {
            var movie = new Movie
            {
                TmdbId = tmdbId++,
                Title = title,
                SortTitle = SortTitle.Build(title),
                Year = 2026,
                TmdbStatus = MovieStatus.Released,
                InCinemasDate = inCinemas,
                DigitalReleaseDate = digital,
                PhysicalReleaseDate = physical,
                DefaultRootFolder = root,
            };
            foreach (var (name, monitored) in versions)
            {
                movie.Versions.Add(new MovieVersion
                {
                    Movie = movie,
                    Name = name,
                    IsDefault = movie.Versions.Count == 0,
                    Monitored = monitored,
                    QualityProfile = hd,
                    RootFolder = root,
                    FolderName = $"{title} (2026) {name}",
                    MinimumAvailability = MinimumAvailability.Released,
                });
            }

            db.Movies.Add(movie);
            return movie;
        }

        Film("Cinema Film", new DateOnly(2026, 11, 11), new DateOnly(2026, 12, 20), null, ("1080p", true), ("4K", false));
        var onDisk = Film("Digital Film", new DateOnly(2026, 8, 1), new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 24), ("1080p", true));
        onDisk.Versions.Single().File = new MediaFile { RelativePath = "Digital Film (2026).mkv", Size = 2_000_000_000, QualityId = Hdtv720p, Languages = ["English"] };
        Film("Missing Film", new DateOnly(2026, 6, 1), null, new DateOnly(2026, 11, 10), ("1080p", true));
        var grabbed = Film("Grabbed Film", new DateOnly(2026, 7, 1), new DateOnly(2026, 11, 12), null, ("1080p", true));
        var grab = new Grab { ReleaseTitle = "Grabbed.Film.2026.1080p.WEB-DL-FLUX", Size = 1 };
        grab.AddTarget(grabbed.Versions.Single());
        db.Grabs.Add(grab);
        Film("Old Film", new DateOnly(2020, 1, 1), new DateOnly(2020, 3, 1), new DateOnly(2020, 4, 1), ("1080p", true));
        await db.SaveChangesAsync();
    }

    private sealed record RadarrCalendarMovie(int Id, string Title, DateTime? InCinemas, DateTime? DigitalRelease, DateTime? PhysicalRelease);
}
