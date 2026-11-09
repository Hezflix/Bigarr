namespace Bigarr.Domain.Tv;

/// <summary>
/// A name releases use for a show, from TheXEM (<c>/map/allNames</c>), with the scene season it stands for. Global,
/// keyed by TVDB id, refreshed on a slow schedule: it also lets a scan or a release find a show by an alias.
/// </summary>
public sealed class SceneName
{
    public int Id { get; set; }

    public int TvdbId { get; set; }

    public required string Title { get; set; }

    /// <summary>The title as the episode parser cleans it (<c>CleanSeriesTitle</c>): the lookup key.</summary>
    public required string CleanTitle { get; set; }

    /// <summary>The scene season this name designates ("Show S2" → 2); null when it covers every season.</summary>
    public int? SceneSeasonNumber { get; set; }
}
