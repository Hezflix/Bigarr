namespace Bigarr.Api.Features.Search;

/// <summary>Bound from <c>Bigarr:Search</c> (env: <c>Bigarr__Search__CutoffUnmetIntervalMinutes</c>…).</summary>
public sealed class SearchOptions
{
    public const string Section = "Bigarr:Search";

    public const int MinimumIntervalMinutes = 10;

    /// <summary>The cutoff-unmet job: searches the missing and upgradable versions whose backoff has elapsed.</summary>
    public bool CutoffUnmetEnabled { get; set; } = true;

    public int CutoffUnmetIntervalMinutes { get; set; } = 60;

    /// <summary>Versions searched per pass, oldest search first: the load on the trackers stays bounded.</summary>
    public int CutoffUnmetBatchSize { get; set; } = 20;

    public int CutoffUnmetInitialDelayMinutes { get; set; } = 5;

    /// <summary>Series versions searched per pass (each with its due episodes, grouped by season), the longest-unsearched first.</summary>
    public int SeriesBatchSize { get; set; } = 10;

    /// <summary>First wait after an empty search; doubles with every further empty search.</summary>
    public int BackoffBaseMinutes { get; set; } = 60;

    public int BackoffMaxHours { get; set; } = 168;

    public TimeSpan CutoffUnmetInterval => TimeSpan.FromMinutes(Math.Max(MinimumIntervalMinutes, CutoffUnmetIntervalMinutes));
}

/// <summary>Interval doubling after each empty search, capped ("jamais un balayage complet périodique").</summary>
public static class SearchBackoff
{
    public static TimeSpan Delay(int attempts, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var baseDelay = TimeSpan.FromMinutes(Math.Max(1, options.BackoffBaseMinutes));
        var max = TimeSpan.FromHours(Math.Max(1, options.BackoffMaxHours));
        var exponent = Math.Clamp(attempts - 1, 0, 20);
        var delay = baseDelay * Math.Pow(2, exponent);
        return delay > max ? max : delay;
    }

    /// <summary>When the next automatic search may run; null when it never ran, which means now.</summary>
    public static DateTime? NextAllowed(DateTime? lastSearchAt, int attempts, SearchOptions options) =>
        lastSearchAt is { } last ? last + Delay(attempts, options) : null;

    public static bool IsDue(DateTime? lastSearchAt, int attempts, SearchOptions options, DateTime now) =>
        NextAllowed(lastSearchAt, attempts, options) is not { } next || next <= now;
}
