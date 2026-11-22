namespace Bigarr.Api.Tests.Series;

using Bigarr.Api.Features.Series;
using Bigarr.Domain.Tv;

/// <summary>Sonarr's <c>ShouldRefreshSeries</c>, case by case.</summary>
public sealed class SeriesRefreshRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, SeriesStatus.Ended, -400, false, true)] // never refreshed
    [InlineData(-31 * 24, SeriesStatus.Ended, -400, false, true)] // more than 30 days ago
    [InlineData(-1, SeriesStatus.Ended, -400, true, true)] // an aired episode still titled TBA, even an hour after the last refresh
    [InlineData(-5, SeriesStatus.Continuing, 3, false, false)] // refreshed less than 6 hours ago
    [InlineData(-7, SeriesStatus.Continuing, 3, false, true)] // not ended
    [InlineData(-7, SeriesStatus.Upcoming, null, false, true)]
    [InlineData(-7, SeriesStatus.Ended, -10, false, true)] // ended, last episode aired less than 30 days ago
    [InlineData(-7, SeriesStatus.Ended, -40, false, false)] // ended long ago
    [InlineData(-29 * 24, SeriesStatus.Ended, null, false, false)]
    public void Decides_like_sonarr(int? refreshedHoursAgo, SeriesStatus status, int? lastAiredDaysAgo, bool tba, bool due)
    {
        var candidate = new SeriesRefreshCandidate(
            1,
            "show",
            refreshedHoursAgo is { } hours ? Now.AddHours(hours) : null,
            status,
            lastAiredDaysAgo is { } days ? Now.AddDays(days) : null,
            tba);

        Assert.Equal(due, SeriesRefreshRule.IsDue(candidate, Now));
    }

    [Fact]
    public void A_show_never_refreshed_is_not_starved_by_shows_refreshed_before()
    {
        // As in a library imported from Sonarr: continuing shows come due every 6 hours and fill the batch on their own.
        var refreshed = Enumerable.Range(1, 3)
            .Select(i => new SeriesRefreshCandidate(i, $"a show {i}", Now.AddHours(-7 - i), SeriesStatus.Continuing, null, false))
            .ToList();
        var neverRefreshed = new SeriesRefreshCandidate(10, "midnight diner", null, SeriesStatus.Ended, null, false);

        var batch = SeriesRefreshRule.NextBatch([.. refreshed, neverRefreshed], 2);

        Assert.Equal([10, 3], batch.Select(s => s.SeriesId));
    }
}
