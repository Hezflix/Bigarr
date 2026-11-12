using System.Globalization;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

/// <summary>
/// Turns TMDB's answers into the numbers of an alternative order. Pure, shared by the live provider and
/// the fixtures. TMDB's default order keeps its season/episode numbers; an episode group numbers its parts as seasons
/// (part order, 0 for a specials part, shifted by one when the regular parts start at 0) and its episodes by their place.
/// Absolute numbers run over the regular seasons in order.
/// </summary>
public static class TmdbOrders
{
    public static List<OrderEpisode> FromDefaultOrder(IEnumerable<TmdbTvSeason> seasons)
    {
        ArgumentNullException.ThrowIfNull(seasons);

        var absolute = 0;
        var result = new List<OrderEpisode>();
        foreach (var episode in seasons.SelectMany(s => s.Episodes).OrderBy(e => e.SeasonNumber == 0 ? int.MaxValue : e.SeasonNumber).ThenBy(e => e.EpisodeNumber))
        {
            result.Add(new OrderEpisode
            {
                TmdbId = episode.Id > 0 ? episode.Id : null,
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                AbsoluteNumber = episode.SeasonNumber > 0 ? ++absolute : null,
                DefaultSeasonNumber = episode.SeasonNumber,
                DefaultEpisodeNumber = episode.EpisodeNumber,
                Title = Blank(episode.Name),
                AirDate = Date(episode.AirDate),
                Runtime = episode.Runtime is > 0 ? episode.Runtime : null,
            });
        }

        return result;
    }

    public static List<OrderEpisode> FromEpisodeGroup(TmdbEpisodeGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var parts = group.Groups.OrderBy(p => p.Order).ToList();
        var regular = parts.Where(p => !IsSpecials(p)).ToList();
        var shift = regular.Count > 0 && regular.Min(p => p.Order) == 0 ? 1 : 0;
        var absolute = 0;
        var result = new List<OrderEpisode>();
        var seen = new HashSet<int>();
        foreach (var part in parts.OrderBy(p => IsSpecials(p) ? 1 : 0).ThenBy(p => p.Order))
        {
            var specials = IsSpecials(part);
            var season = specials ? 0 : part.Order + shift;
            var number = 0;
            foreach (var episode in part.Episodes.OrderBy(e => e.Order))
            {
                // A group may list an episode twice (two arcs share it): the first place wins.
                if (episode.Id > 0 && !seen.Add(episode.Id))
                {
                    continue;
                }

                result.Add(new OrderEpisode
                {
                    TmdbId = episode.Id > 0 ? episode.Id : null,
                    SeasonNumber = season,
                    EpisodeNumber = ++number,
                    AbsoluteNumber = specials ? null : ++absolute,
                    DefaultSeasonNumber = episode.SeasonNumber,
                    DefaultEpisodeNumber = episode.EpisodeNumber,
                    Title = Blank(episode.Name),
                    AirDate = Date(episode.AirDate),
                    Runtime = episode.Runtime is > 0 ? episode.Runtime : null,
                });
            }
        }

        return result;
    }

    public static EpisodeGroupSummary Summary(TmdbEpisodeGroupSummary group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new EpisodeGroupSummary(group.Id, Blank(group.Name) ?? group.Id, GroupType(group.Type), group.GroupCount, group.EpisodeCount);
    }

    public static EpisodeGroupType GroupType(int type) =>
        Enum.IsDefined(typeof(EpisodeGroupType), type) ? (EpisodeGroupType)type : EpisodeGroupType.Tv;

    /// <summary>A part named "Specials" (any language TMDB uses it in) or holding only TMDB specials.</summary>
    private static bool IsSpecials(TmdbEpisodeGroupPart part) =>
        (part.Name?.Contains("special", StringComparison.OrdinalIgnoreCase) ?? false)
        || (part.Episodes.Count > 0 && part.Episodes.All(e => e.SeasonNumber == 0));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly? Date(string? value) =>
        value is { Length: >= 10 } && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
