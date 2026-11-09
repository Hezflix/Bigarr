namespace Bigarr.Api.Features.Series;

using Bigarr.Domain.Tv;

/// <summary>What the rules need to know about an episode for one version.</summary>
public sealed record EpisodeFacts(int EpisodeId, int SeasonNumber, int EpisodeNumber, bool HasAired, bool HasFile);

/// <summary>
/// Sonarr's "Monitor" options, applied to one version. Specials (season 0) are never monitored by an option; they are
/// switched on episode by episode. A season is monitored when one of its episodes is, and for the open-ended options
/// (all, future, missing) the latest regular season is monitored even before it has episodes, so new ones are wanted.
/// </summary>
public static class MonitoringRules
{
    public static IReadOnlySet<int> MonitoredEpisodes(MonitoringOption option, IReadOnlyCollection<EpisodeFacts> episodes)
    {
        var regular = episodes.Where(e => e.SeasonNumber > 0).ToList();
        var firstSeason = regular.Count == 0 ? (int?)null : regular.Min(e => e.SeasonNumber);
        var latestSeason = regular.Count == 0 ? (int?)null : regular.Max(e => e.SeasonNumber);
        var pilot = firstSeason is null ? null : regular.Where(e => e.SeasonNumber == firstSeason).MinBy(e => e.EpisodeNumber);

        Func<EpisodeFacts, bool> wanted = option switch
        {
            MonitoringOption.All => _ => true,
            MonitoringOption.Future => e => !e.HasAired,
            MonitoringOption.Missing => e => !e.HasFile,
            MonitoringOption.Existing => e => e.HasFile,
            MonitoringOption.Pilot => e => e.EpisodeId == pilot?.EpisodeId,
            MonitoringOption.FirstSeason => e => e.SeasonNumber == firstSeason,
            MonitoringOption.LatestSeason => e => e.SeasonNumber == latestSeason,
            _ => _ => false,
        };

        return regular.Where(wanted).Select(e => e.EpisodeId).ToHashSet();
    }

    /// <summary>Season flags that go with <see cref="MonitoredEpisodes"/>, keyed by season number.</summary>
    public static IReadOnlyDictionary<int, bool> MonitoredSeasons(
        MonitoringOption option, IReadOnlyCollection<int> seasonNumbers, IReadOnlyCollection<EpisodeFacts> episodes, IReadOnlySet<int> monitoredEpisodes)
    {
        var regularSeasons = seasonNumbers.Where(n => n > 0).ToList();
        var latest = regularSeasons.Count == 0 ? (int?)null : regularSeasons.Max();
        var openEnded = option is MonitoringOption.All or MonitoringOption.Future or MonitoringOption.Missing or MonitoringOption.LatestSeason;

        return seasonNumbers.Distinct().ToDictionary(
            n => n,
            n => episodes.Any(e => e.SeasonNumber == n && monitoredEpisodes.Contains(e.EpisodeId))
                 || (n > 0 && option == MonitoringOption.All)
                 || (openEnded && n == latest));
    }

    /// <summary>A season that appears on a refresh: wanted when the version takes new items, never for specials.</summary>
    public static bool NewSeasonMonitored(SeriesVersion version, int seasonNumber) =>
        seasonNumber > 0 && version.MonitorNewItems == NewItemMonitoring.All;
}
