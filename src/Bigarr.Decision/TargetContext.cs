using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;

namespace Bigarr.Decision;

/// <summary>The current file of a target, reduced to what the engine compares.</summary>
public sealed record ExistingFile(int QualityId, int RevisionVersion, int RevisionReal, bool IsRepack);

/// <summary>
/// Everything the engine may know about a target. Assembled by the slice that owns the target type; the engine itself
/// never sees a <c>Movie</c> (blueprint §3.10).
/// </summary>
public sealed record TargetContext(
    IAcquisitionTarget Target,
    QualityProfile Profile,
    IReadOnlyDictionary<int, QualityDefinition> Definitions,
    ExistingFile? File,
    /// <summary>Drives the size check. Null falls back to <see cref="DefaultRuntimeMinutes"/>.</summary>
    int? RuntimeMinutes,
    IReadOnlyCollection<BlocklistEntry> Blocklist,
    bool HasActiveGrab)
{
    /// <summary>Radarr's assumption when TMDB has no runtime yet.</summary>
    public const int DefaultRuntimeMinutes = 110;

    public int EffectiveRuntimeMinutes => RuntimeMinutes is > 0 ? RuntimeMinutes.Value : DefaultRuntimeMinutes;
}
