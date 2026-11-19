namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Bound from <c>Bigarr:Shadow</c> (env: <c>Bigarr__Shadow__Enabled=true</c>). Shadow mode is Bigarr running next to the
/// Radarrs and Sonarrs it is meant to replace: every grab path (RSS pass, automatic and interactive search) records what
/// it would have sent instead of calling a download client, and the comparison pages put those picks next to the
/// followed apps' grabs.
/// </summary>
/// <remarks>
/// Films and series leave shadow mode one at a time: <c>Enabled</c> sets both, and <c>Films</c> or <c>Series</c>
/// overrides it for one kind. <c>Enabled=true</c> with <c>Films=false</c> grabs films for real while series stay
/// picks, so Radarr can be replaced while Sonarr keeps running.
/// </remarks>
public sealed class ShadowOptions
{
    public const string Section = "Bigarr:Shadow";

    /// <summary>Record picks instead of grabbing, for films and series alike unless <see cref="Films"/> or <see cref="Series"/> says otherwise.</summary>
    public bool Enabled { get; set; }

    /// <summary>Overrides <see cref="Enabled"/> for films. Unset: films follow <see cref="Enabled"/>.</summary>
    public bool? Films { get; set; }

    /// <summary>Overrides <see cref="Enabled"/> for series. Unset: series follow <see cref="Enabled"/>.</summary>
    public bool? Series { get; set; }

    /// <summary>Film grabs are recorded as picks; nothing for a film reaches a download client.</summary>
    public bool FilmsInShadow => Films ?? Enabled;

    /// <summary>Series grabs are recorded as picks; nothing for a series reaches a download client.</summary>
    public bool SeriesInShadow => Series ?? Enabled;

    /// <summary>At least one kind is still in shadow mode.</summary>
    public bool AnyInShadow => FilmsInShadow || SeriesInShadow;

    /// <summary>Is this kind in shadow mode?</summary>
    public bool InShadow(bool series) => series ? SeriesInShadow : FilmsInShadow;
}
