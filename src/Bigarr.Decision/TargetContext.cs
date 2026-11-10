using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;

namespace Bigarr.Decision;

/// <summary>The current file of a target, reduced to what the engine compares.</summary>
public sealed record ExistingFile(int QualityId, int RevisionVersion, int RevisionReal, bool IsRepack)
{
    /// <summary>Custom formats the file matches, evaluated at read time like a release's.</summary>
    public IReadOnlyList<int> CustomFormatIds { get; init; } = [];

    /// <summary>
    /// Shadow mode: not a file on disk but a release Bigarr picked, standing in for the file it would have become had it
    /// been grabbed. Only changes how the reasons name it.
    /// </summary>
    public bool IsPick { get; init; }

    /// <summary>Release group of the file (or pick): anime v2 upgrades must come from the same group.</summary>
    public string? ReleaseGroup { get; init; }
}

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
    bool HasActiveGrab,
    /// <summary>Minimum availability reached. Only the automatic paths (RSS, auto search) reject on it.</summary>
    bool IsAvailable = true,
    /// <summary>Tie-breaker for attribution after constraint specificity (blueprint §3.3).</summary>
    bool IsDefaultTarget = false)
{
    /// <summary>Radarr's assumption when TMDB has no runtime yet.</summary>
    public const int DefaultRuntimeMinutes = 110;

    /// <summary>Custom format names, for the reasons shown to the user.</summary>
    public IReadOnlyDictionary<int, string> CustomFormatNames { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// Set for a series version: the episodes the release covers, each with its own file, grab and monitoring. Then
    /// <see cref="Target"/> is the first of them (it resolves the version's profile and constraints) and
    /// <see cref="File"/> stays null: files are per episode.
    /// </summary>
    public EpisodeCoverage? Episodes { get; init; }

    public int EffectiveRuntimeMinutes => RuntimeMinutes is > 0 ? RuntimeMinutes.Value : DefaultRuntimeMinutes;
}
