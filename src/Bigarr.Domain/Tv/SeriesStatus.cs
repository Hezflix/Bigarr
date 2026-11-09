namespace Bigarr.Domain.Tv;

public enum SeriesStatus
{
    /// <summary>Announced, nothing aired yet.</summary>
    Upcoming = 1,

    Continuing = 2,

    Ended = 3,

    /// <summary>The providers no longer know the series (set by a refresh, like a film's <c>deleted</c>).</summary>
    Deleted = 4,
}
