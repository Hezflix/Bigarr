namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Bound from <c>Bigarr:Shadow</c> (env: <c>Bigarr__Shadow__Enabled=true</c>). Shadow mode is Bigarr running next to the
/// Radarrs it is meant to replace: every grab path (RSS pass, automatic and interactive search) records what it would
/// have sent instead of calling a download client, and the comparison page puts those picks next to Radarr's grabs.
/// </summary>
public sealed class ShadowOptions
{
    public const string Section = "Bigarr:Shadow";

    public const int MinimumFollowIntervalMinutes = 5;

    /// <summary>Record picks instead of grabbing. Nothing is ever sent to a download client while it is on.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long the releases the RSS pass saw are kept, to tell "never seen" from "seen and rejected".</summary>
    public int SightingRetentionDays { get; set; } = 30;

    /// <summary>How often the followed Radarrs are read (history, library). Radarr's own RSS default is 15 minutes too.</summary>
    public int FollowIntervalMinutes { get; set; } = 15;

    /// <summary>Releases picked since the last pass that are shown to Radarr's <c>/parse</c> per followed Radarr and pass.</summary>
    public int RadarrViewsPerPass { get; set; } = 50;

    public TimeSpan FollowInterval => TimeSpan.FromMinutes(Math.Max(MinimumFollowIntervalMinutes, FollowIntervalMinutes));
}
