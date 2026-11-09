namespace Bigarr.Domain.Tv;

/// <summary>Sonarr's "Monitor" choice when adding a series (or reapplying it to a version). Not stored: it sets episode flags.</summary>
public enum MonitoringOption
{
    /// <summary>Every episode except specials.</summary>
    All = 1,

    /// <summary>Episodes that have not aired yet.</summary>
    Future = 2,

    /// <summary>Episodes without a file (specials excluded).</summary>
    Missing = 3,

    /// <summary>Episodes with a file (specials excluded).</summary>
    Existing = 4,

    /// <summary>Only the first episode of the first season.</summary>
    Pilot = 5,

    /// <summary>Every episode of the first regular season.</summary>
    FirstSeason = 6,

    /// <summary>Every episode of the last regular season.</summary>
    LatestSeason = 7,

    /// <summary>Nothing.</summary>
    None = 8,
}
