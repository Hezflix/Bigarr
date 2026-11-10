namespace Bigarr.Decision.Specifications;

/// <summary>Quality definitions give MB per minute of runtime (series sizes for episodes); unknown runtime falls back to 110 minutes.</summary>
public sealed class SizeSpecification : IReleaseSpecification
{
    private const long Megabyte = 1024L * 1024L;

    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        var size = candidate.Release.Size;
        if (size <= 0)
        {
            return null; // Some indexers do not report sizes; nothing to check.
        }

        var qualityId = candidate.Quality?.Quality?.Id ?? 0;
        if (!target.Definitions.TryGetValue(qualityId, out var definition))
        {
            return null;
        }

        var runtime = target.EffectiveRuntimeMinutes;
        var forWhat = $"{runtime} min";
        if (target.Episodes is { } episodes)
        {
            // Sonarr's AcceptableSizeSpecification: specials are not checked, the sizes are per minute of every episode covered.
            if (candidate.ParsedEpisode?.Special == true)
            {
                return null;
            }

            var seriesRuntime = episodes.SeriesRuntimeMinutes ?? 0;
            if (seriesRuntime == 0 && GuessNewShowRuntime(candidate, episodes))
            {
                seriesRuntime = NewShowRuntimeMinutes;
            }

            runtime = episodes.Slots.Sum(s => s.RuntimeMinutes is > 0 ? s.RuntimeMinutes.Value : seriesRuntime);
            if (runtime == 0)
            {
                return new Rejection("Runtime of the episodes is unknown: the size cannot be checked until it is available");
            }

            forWhat = episodes.Slots.Count == 1 ? $"{runtime} min" : $"{episodes.Slots.Count} episodes totalling {runtime} min";
        }

        // Episodes have their own limits (Sonarr's defaults), films theirs (Radarr's).
        var (minSize, maxSize) = target.Episodes is null ? (definition.MinSize, definition.MaxSize) : (definition.SeriesMinSize, definition.SeriesMaxSize);
        if (minSize is > 0)
        {
            var min = (long)(minSize.Value * runtime * Megabyte);
            if (size < min)
            {
                return new Rejection($"{Format(size)} is smaller than the minimum {Format(min)} for {definition.Name} ({forWhat})");
            }
        }

        if (maxSize is > 0)
        {
            var max = (long)(maxSize.Value * runtime * Megabyte);
            if (size > max)
            {
                return new Rejection($"{Format(size)} is larger than the maximum {Format(max)} for {definition.Name} ({forWhat})");
            }
        }

        return null;
    }

    /// <summary>Sonarr's guess for a show whose runtime is not known yet.</summary>
    public const int NewShowRuntimeMinutes = 45;

    /// <summary>
    /// Sonarr's <c>AcceptableSizeSpecification</c>: a new show often has no runtime yet. For releases of its first season
    /// whose episodes air (or have no air date) before 24 hours after the pilot (or after the release's publication
    /// when the pilot has no date), the series is taken to run 45 minutes.
    /// </summary>
    private static bool GuessNewShowRuntime(ReleaseCandidate candidate, EpisodeCoverage episodes)
    {
        if (episodes.FirstSeasonNumber is not { } firstSeason || episodes.Slots.Count == 0 || episodes.Slots[0].SeasonNumber != firstSeason)
        {
            return false;
        }

        var graceEnd = (episodes.PilotAirDateUtc ?? candidate.Release.PublishDate.UtcDateTime).AddHours(24);
        return episodes.Slots.All(s => s.AirDateUtc is null || s.AirDateUtc.Value < graceEnd);
    }

    private static string Format(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024 * 1024):0.#} GB"),
        >= 1024L * 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0} MB"),
        _ => $"{bytes} B",
    };
}
