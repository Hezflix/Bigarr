using Bigarr.Api.Features.Movies;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Movies;

/// <summary>Radarr's ShouldRefreshMovie branch by branch, plus "never refreshed is due", on a clock the test moves.</summary>
public sealed class MetadataRefreshRuleTests
{
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(Now);

    [Fact]
    public void A_film_never_refreshed_is_due_whatever_its_status()
    {
        Assert.True(IsDue(OutOnDisc(lastRefreshAt: null)));
        Assert.True(IsDue(OutOnDisc(lastRefreshAt: null) with { TmdbStatus = MovieStatus.Deleted }));
    }

    [Fact]
    public void A_film_that_came_out_long_ago_waits_180_days()
    {
        var film = OutOnDisc(lastRefreshAt: Now);

        _clock.Advance(TimeSpan.FromHours(13));
        Assert.False(IsDue(film));

        _clock.Advance(TimeSpan.FromDays(179)); // 179 days and 13 hours
        Assert.False(IsDue(film));

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.True(IsDue(film));
    }

    [Fact]
    public void Nothing_is_refreshed_twice_within_12_hours()
    {
        var announced = new MetadataRefreshCandidate(1, Now, MovieStatus.Announced, Today.AddDays(60), null, null);

        _clock.Advance(TimeSpan.FromHours(12));
        Assert.False(IsDue(announced));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(IsDue(announced));
    }

    [Fact]
    public void Announced_and_in_cinemas_films_are_refreshed_once_12_hours_old()
    {
        var refreshedAt = Now;
        _clock.Advance(TimeSpan.FromHours(13));

        Assert.True(IsDue(new MetadataRefreshCandidate(1, refreshedAt, MovieStatus.Announced, Today.AddDays(60), null, null)));
        Assert.True(IsDue(new MetadataRefreshCandidate(2, refreshedAt, MovieStatus.Released, Today.AddDays(-10), null, Today.AddDays(60))));

        // No date at all: Radarr calls it announced, whatever TMDB says.
        Assert.True(IsDue(new MetadataRefreshCandidate(3, refreshedAt, MovieStatus.Tba, null, null, null)));
        Assert.True(IsDue(new MetadataRefreshCandidate(4, refreshedAt, MovieStatus.Released, null, null, null)));
    }

    [Fact]
    public void A_released_film_is_refreshed_until_its_physical_release_is_30_days_old()
    {
        var film = new MetadataRefreshCandidate(1, Now, MovieStatus.Released, Today.AddDays(-120), Today.AddDays(-20), Today.AddDays(-20));
        _clock.Advance(TimeSpan.FromHours(13));
        Assert.True(IsDue(film));

        _clock.Advance(TimeSpan.FromDays(10)); // the disc came out 31 days ago
        Assert.False(IsDue(film));

        // Out digitally, the disc still to come: its dates can still move.
        Assert.True(IsDue(new MetadataRefreshCandidate(2, Now.AddHours(-13), MovieStatus.Released, Today.AddDays(-60), Today.AddDays(-1), Today.AddDays(40))));
    }

    [Fact]
    public void Without_a_disc_date_a_film_counts_as_released_90_days_after_cinemas()
    {
        var film = new MetadataRefreshCandidate(1, Now, MovieStatus.Released, Today.AddDays(-50), null, null);
        _clock.Advance(TimeSpan.FromHours(13));
        Assert.True(IsDue(film)); // still in cinemas

        _clock.Advance(TimeSpan.FromDays(50)); // the 90-day cinema run ended 11 days ago: released, recently
        Assert.True(IsDue(film));

        _clock.Advance(TimeSpan.FromDays(21)); // released 32 days ago
        Assert.False(IsDue(film));
    }

    [Fact]
    public void A_film_deleted_from_tmdb_waits_180_days_even_with_upcoming_dates()
    {
        var film = new MetadataRefreshCandidate(1, Now, MovieStatus.Deleted, Today.AddDays(60), null, null);

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.False(IsDue(film));

        _clock.Advance(TimeSpan.FromDays(180));
        Assert.True(IsDue(film));
    }

    private bool IsDue(MetadataRefreshCandidate film) => MetadataRefreshRule.IsDue(film, Now);

    /// <summary>The Matrix: in cinemas, on disc and digital in 1999.</summary>
    private static MetadataRefreshCandidate OutOnDisc(DateTime? lastRefreshAt) =>
        new(1, lastRefreshAt, MovieStatus.Released, new DateOnly(1999, 3, 31), new DateOnly(1999, 9, 21), new DateOnly(1999, 9, 21));
}
