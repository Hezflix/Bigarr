using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;

namespace Bigarr.Api.Infrastructure.Tv;

/// <summary>How a TVDB episode was found in the alternative order.</summary>
public enum OrderLinkRule
{
    /// <summary>Same TMDB episode id.</summary>
    TmdbId = 1,

    /// <summary>Same season/episode (TVDB vs TMDB default numbers) with an air date within two days.</summary>
    Numbers = 2,

    /// <summary>Same title with an air date within two days.</summary>
    DateAndTitle = 3,

    /// <summary>The only episode of both sides aired that day.</summary>
    AloneThatDay = 4,
}

/// <summary>An episode's numbers in the alternative order.</summary>
public sealed record OrderNumbers(int SeasonNumber, int EpisodeNumber, int? AbsoluteNumber, OrderLinkRule Rule);

/// <param name="Numbers">Episode (by reference: new episodes have no id yet) → its numbers in the order.</param>
/// <param name="NotInOrder">TVDB episodes the order has no counterpart for.</param>
/// <param name="OrderOnly">Episodes of the order with no TVDB counterpart.</param>
public sealed record OrderLink(IReadOnlyDictionary<Episode, OrderNumbers> Numbers, IReadOnlyList<Episode> NotInOrder, IReadOnlyList<OrderOnlyEpisode> OrderOnly);

/// <summary>
/// Links a series' episodes (stable TVDB identity) to an alternative order, one to one (blueprint §8, the audit's rules):
/// the TMDB episode id first, then the same season/episode with an air date within ±2 days, then the same title within
/// ±2 days, then the only episode of both sides aired that day. Whatever stays unlinked is reported on both sides,
/// never guessed. Pure.
/// </summary>
public static class EpisodeOrderLinker
{
    public const int AirDateToleranceDays = 2;

    public static OrderLink Link(IReadOnlyCollection<Episode> episodes, IReadOnlyCollection<OrderEpisode> order)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(order);

        var numbers = new Dictionary<Episode, OrderNumbers>(ReferenceEqualityComparer.Instance);
        var free = order.ToList();
        var pending = episodes.OrderBy(e => e.SeasonNumber == 0 ? int.MaxValue : e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();

        void Take(Episode episode, OrderEpisode entry, OrderLinkRule rule)
        {
            numbers[episode] = new OrderNumbers(entry.SeasonNumber, entry.EpisodeNumber, entry.AbsoluteNumber, rule);
            free.Remove(entry);
            pending.Remove(episode);
        }

        // 1. TMDB episode id: the bridge the metadata refresh already set.
        var byTmdb = free.Where(o => o.TmdbId is not null).GroupBy(o => o.TmdbId!.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var episode in pending.ToList())
        {
            if (episode.TmdbId is { } id && byTmdb.TryGetValue(id, out var entry) && free.Contains(entry))
            {
                Take(episode, entry, OrderLinkRule.TmdbId);
            }
        }

        // 2. Same numbers (TMDB default ones) and the air date within two days (or the same title when a date lacks).
        foreach (var episode in pending.ToList())
        {
            var entry = free.FirstOrDefault(o => o.DefaultSeasonNumber == episode.SeasonNumber && o.DefaultEpisodeNumber == episode.EpisodeNumber && Agrees(episode, o));
            if (entry is not null)
            {
                Take(episode, entry, OrderLinkRule.Numbers);
            }
        }

        // 3. Same title, aired within two days.
        foreach (var episode in pending.ToList())
        {
            var entry = free.FirstOrDefault(o => Near(episode.AirDate, o.AirDate) && SameTitle(episode.Title, o.Title));
            if (entry is not null)
            {
                Take(episode, entry, OrderLinkRule.DateAndTitle);
            }
        }

        // 4. Alone that day on both sides (specials with specials).
        foreach (var episode in pending.ToList())
        {
            if (episode.AirDate is not { } date)
            {
                continue;
            }

            var sameDay = free.Where(o => o.AirDate == date && (o.SeasonNumber == 0) == (episode.SeasonNumber == 0)).ToList();
            var ours = pending.Count(e => e.AirDate == date && (e.SeasonNumber == 0) == (episode.SeasonNumber == 0));
            if (sameDay.Count == 1 && ours == 1)
            {
                Take(episode, sameDay[0], OrderLinkRule.AloneThatDay);
            }
        }

        var orderOnly = free
            .OrderBy(o => o.SeasonNumber == 0 ? int.MaxValue : o.SeasonNumber).ThenBy(o => o.EpisodeNumber)
            .Select(o => new OrderOnlyEpisode(o.SeasonNumber, o.EpisodeNumber, o.AbsoluteNumber, o.Title, o.AirDate, o.TmdbId))
            .ToList();
        return new OrderLink(numbers, pending, orderOnly);
    }

    /// <summary>Writes the link onto the episodes (null numbers for those not in the order). TVDB numbers are never touched.</summary>
    public static void Apply(Series series, IEnumerable<Episode> episodes, OrderLink? link)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episodes);

        foreach (var episode in episodes)
        {
            var numbers = link?.Numbers.GetValueOrDefault(episode);
            episode.OrderSeasonNumber = numbers?.SeasonNumber;
            episode.OrderEpisodeNumber = numbers?.EpisodeNumber;
            episode.OrderAbsoluteNumber = numbers?.AbsoluteNumber;
        }

        series.OrderOnlyEpisodes = link is null ? [] : [.. link.OrderOnly];
    }

    private static bool Agrees(Episode episode, OrderEpisode entry)
    {
        if (episode.AirDate is not null && entry.AirDate is not null)
        {
            return Near(episode.AirDate, entry.AirDate) || SameTitle(episode.Title, entry.Title);
        }

        return (episode.AirDate is null && entry.AirDate is null) || SameTitle(episode.Title, entry.Title);
    }

    public static bool Near(DateOnly? a, DateOnly? b) =>
        a is { } x && b is { } y && Math.Abs(x.DayNumber - y.DayNumber) <= AirDateToleranceDays;

    public static bool SameTitle(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        var left = a.CleanMovieTitle();
        return left.Length > 0 && left == b.CleanMovieTitle();
    }
}
