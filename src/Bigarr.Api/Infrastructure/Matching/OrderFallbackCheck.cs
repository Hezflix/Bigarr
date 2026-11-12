using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Domain.Tv;
using Bigarr.Parsing.Episodes;
using Bigarr.Parsing.Episodes.Model;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

namespace Bigarr.Api.Infrastructure.Matching;

/// <param name="Confirmed">Something in the release agrees with the episodes found on the series' order.</param>
/// <param name="Contradiction">Something in the release disagrees (an air date too far): not these episodes.</param>
public sealed record OrderFallbackVerdict(bool Confirmed, string? Contradiction);

/// <summary>
/// Checks a release found on the series' alternative order: an air date in its name within ±2 days of the
/// episode's, or the episode's title in its name, confirms it; an air date too far contradicts it. Releases carry no
/// runtime (Torznab gives none), so runtime cannot weigh in. Without any of these, the hit stays ambiguous.
/// </summary>
public static class OrderFallbackCheck
{
    /// <summary>Titles shorter than this ("Pilot" aside) say too little to confirm anything.</summary>
    public const int MinimumTitleLength = 4;

    public static OrderFallbackVerdict Verify(string releaseTitle, ParsedEpisodeInfo parsed, IReadOnlyList<Episode> episodes)
    {
        ArgumentNullException.ThrowIfNull(releaseTitle);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(episodes);

        if (parsed.IsDaily && DateOnly.TryParseExact(parsed.AirDate, "yyyy-MM-dd", out var date))
        {
            return episodes.Any(e => EpisodeOrderLinker.Near(e.AirDate, date))
                ? new OrderFallbackVerdict(true, null)
                : new OrderFallbackVerdict(false, $"its air date {parsed.AirDate} is not the episode's");
        }

        var normalized = EpisodeParser.NormalizeEpisodeTitle(releaseTitle).CleanSeriesTitle();
        var titled = episodes.Where(e => !string.IsNullOrWhiteSpace(e.Title) && e.Title != Metadata.SeriesMetadataProvider.UntitledEpisode).ToList();
        var confirmed = titled.Count > 0 && titled.All(e =>
        {
            var title = EpisodeParser.NormalizeEpisodeTitle(e.Title).CleanSeriesTitle();
            return title.Length >= MinimumTitleLength && normalized.Contains(title, StringComparison.OrdinalIgnoreCase);
        });
        return new OrderFallbackVerdict(confirmed, null);
    }
}
