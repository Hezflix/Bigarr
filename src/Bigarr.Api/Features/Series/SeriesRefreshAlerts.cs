namespace Bigarr.Api.Features.Series;

/// <summary>
/// TVDB missing or failing, for the health check (episode lists of shows with a TVDB id are then not refreshed). In
/// memory: a restart forgets it, the next refresh says it again. Refreshes refused by the safety net are stored on the
/// show instead (<see cref="Bigarr.Domain.Tv.Series.RefreshRefusedDetail"/>).
/// </summary>
public sealed class SeriesRefreshAlerts
{
    /// <summary>Why TVDB could not number the last refresh that needed it; null once one went through.</summary>
    public (DateTime At, string Reason)? TvdbUnavailable { get; private set; }

    public void TvdbFailed(string reason, DateTime at) => TvdbUnavailable = (at, reason);

    public void TvdbAnswered() => TvdbUnavailable = null;
}
