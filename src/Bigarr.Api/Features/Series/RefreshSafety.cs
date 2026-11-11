namespace Bigarr.Api.Features.Series;

/// <summary>
/// The refresh's safety net: an update that would drop more than half of a show's episodes and bring more than half as
/// many new ones replaces the list rather than updating it (a numbering that no longer matches the stored ids). Shows
/// with a handful of episodes are left to the normal rules.
/// </summary>
public static class RefreshSafety
{
    public const int MinimumEpisodes = 4;

    public static bool Refuses(int existing, int removed, int added) =>
        existing >= MinimumEpisodes && removed * 2 > existing && added * 2 > existing;
}
