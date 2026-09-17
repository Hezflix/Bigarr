using Bigarr.Api.Features.Search;

namespace Bigarr.Api.Tests.Domain;

public sealed class SearchBackoffTests
{
    private static readonly SearchOptions Options = new() { BackoffBaseMinutes = 60, BackoffMaxHours = 168 };

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(6, 1920)]
    [InlineData(9, 168 * 60)]
    [InlineData(40, 168 * 60)]
    public void The_delay_doubles_with_each_empty_search_and_is_capped(int attempts, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), SearchBackoff.Delay(attempts, Options));
    }

    [Fact]
    public void A_version_never_searched_is_due_now()
    {
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Null(SearchBackoff.NextAllowed(null, 0, Options));
        Assert.True(SearchBackoff.IsDue(null, 5, Options, now));
        Assert.Equal(now.AddHours(2), SearchBackoff.NextAllowed(now, 2, Options));
        Assert.False(SearchBackoff.IsDue(now, 2, Options, now.AddMinutes(119)));
        Assert.True(SearchBackoff.IsDue(now, 2, Options, now.AddMinutes(120)));
    }
}
