namespace Bigarr.Api.Features.Series;

using System.Collections.Concurrent;

/// <summary>
/// What the series refresh had to leave alone, for the health check: TVDB missing or failing (episode lists of shows
/// with a TVDB id are then not refreshed), and refreshes refused by the safety net. In memory: a restart forgets them,
/// the next refresh says them again.
/// </summary>
public sealed class SeriesRefreshAlerts
{
    private readonly ConcurrentDictionary<int, string> _refused = new();

    /// <summary>Why TVDB could not number the last refresh that needed it; null once one went through.</summary>
    public (DateTime At, string Reason)? TvdbUnavailable { get; private set; }

    public IReadOnlyDictionary<int, string> Refused => _refused;

    public void TvdbFailed(string reason, DateTime at) => TvdbUnavailable = (at, reason);

    public void TvdbAnswered() => TvdbUnavailable = null;

    public void Refuse(int seriesId, string detail) => _refused[seriesId] = detail;

    public void Accepted(int seriesId) => _refused.TryRemove(seriesId, out _);
}
