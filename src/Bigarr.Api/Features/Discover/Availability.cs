namespace Bigarr.Api.Features.Discover;

/// <summary>Where a title (or a season) stands in the library for one version, as Discover shows it to everyone.</summary>
public enum AvailabilityState
{
    /// <summary>Not in the library, or in it but not wanted in this version (unmonitored, no file).</summary>
    NotInLibrary = 0,

    /// <summary>Wanted (monitored) and no file yet: on its way.</summary>
    Processing = 1,

    /// <summary>Some aired episodes have a file, others not. Series only.</summary>
    PartiallyAvailable = 2,

    /// <summary>The film has a file; every aired episode has one.</summary>
    Available = 3,
}

/// <summary>One episode of one version, reduced to what availability needs.</summary>
public readonly record struct EpisodeSlot(bool Aired, bool HasFile, bool Monitored);

/// <summary>
/// The availability rules, read straight from the library: no scan, no stored state. A film version is available with a
/// file, processing while monitored without one. A season, or a whole series, is available when every aired episode has
/// a file (an episode that has not aired yet never makes it partial), partially available when only some do, processing
/// when none does yet but episodes are monitored.
/// </summary>
public static class Availability
{
    public static AvailabilityState OfFilm(bool hasFile, bool monitored) =>
        hasFile ? AvailabilityState.Available : monitored ? AvailabilityState.Processing : AvailabilityState.NotInLibrary;

    public static AvailabilityState OfEpisodes(IEnumerable<EpisodeSlot> episodes)
    {
        var withFile = 0;
        var airedWithoutFile = 0;
        var monitored = false;
        foreach (var episode in episodes)
        {
            monitored |= episode.Monitored;
            if (episode.HasFile)
            {
                withFile++;
            }
            else if (episode.Aired)
            {
                airedWithoutFile++;
            }
        }

        if (withFile == 0)
        {
            return monitored ? AvailabilityState.Processing : AvailabilityState.NotInLibrary;
        }

        return airedWithoutFile == 0 ? AvailabilityState.Available : AvailabilityState.PartiallyAvailable;
    }
}
