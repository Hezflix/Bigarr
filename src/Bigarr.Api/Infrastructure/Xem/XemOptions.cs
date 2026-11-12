namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>Bound from <c>Bigarr:Xem</c>. TheXEM is public and keyless; off, scene numbering simply stays empty.</summary>
public sealed class XemOptions
{
    public const string Section = "Bigarr:Xem";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://thexem.info/map/";

    /// <summary>How often the slow schedule looks for scene names and mappings that are due.</summary>
    public int RefreshIntervalHours { get; set; } = 12;

    /// <summary>
    /// How long a show's mapping, the list of mapped shows and the scene names are kept before TheXEM is asked again.
    /// A show's mapping is read earlier only when a refresh brought it new episodes.
    /// </summary>
    public int MapMaxAgeHours { get; set; } = 24;

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Minimum pause between two requests to TheXEM, whoever asks (a small free service: never hammered).</summary>
    public int MinRequestIntervalMilliseconds { get; set; } = 1000;

    /// <summary>The pause taken when TheXEM answers 429 or 503 without a usable <c>Retry-After</c>.</summary>
    public int DefaultRetryAfterSeconds { get; set; } = 60;

    /// <summary>A <c>Retry-After</c> longer than this ends the pass instead of holding it: it resumes on the next schedule.</summary>
    public int MaxPassPauseSeconds { get; set; } = 600;

    /// <summary>Consecutive refused or failed requests after which a pass stops.</summary>
    public int MaxConsecutiveFailures { get; set; } = 3;
}
