using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Naming;

/// <summary>Everything a naming template may print. Assembled by the slice that knows the film; the engine stays type-agnostic.</summary>
public sealed record NamingContext(
    string MovieTitle,
    string? OriginalTitle,
    int? Year,
    int TmdbId,
    string? ImdbId,
    string VersionName)
{
    /// <summary>Quality definition name, e.g. <c>Bluray-1080p</c>. Null for folder names.</summary>
    public string? QualityName { get; init; }

    public int RevisionVersion { get; init; } = 1;

    public int RevisionReal { get; init; }

    public bool IsRepack { get; init; }

    public string? Edition { get; init; }

    public string? ReleaseGroup { get; init; }

    /// <summary>Language names as the parser uses them ("English", "French").</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    public MediaInfo? MediaInfo { get; init; }

    /// <summary>Names of the matched custom formats flagged "include when renaming".</summary>
    public IReadOnlyList<string> CustomFormats { get; init; } = [];

    public string? OriginalReleaseTitle { get; init; }

    public string? OriginalFileName { get; init; }
}
