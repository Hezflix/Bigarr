using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Naming;

/// <summary>One episode as a file name prints it.</summary>
public sealed record EpisodeNaming(int SeasonNumber, int EpisodeNumber, int? AbsoluteEpisodeNumber, DateOnly? AirDate, string Title);

/// <summary>
/// Everything a series naming template may print. Assembled by the slice that knows the series; the engine stays
/// type-agnostic, like <see cref="NamingContext"/> for films.
/// </summary>
public sealed record SeriesNamingContext(
    string SeriesTitle,
    int? Year,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    string VersionName)
{
    /// <summary>The episodes of the file, in order. Empty for folder names.</summary>
    public IReadOnlyList<EpisodeNaming> Episodes { get; init; } = [];

    /// <summary>Quality definition name as Bigarr stores it (<c>Remux-1080p</c>); printed with Sonarr's names.</summary>
    public string? QualityName { get; init; }

    public int RevisionVersion { get; init; } = 1;

    public int RevisionReal { get; init; }

    public bool IsRepack { get; init; }

    public string? ReleaseGroup { get; init; }

    public IReadOnlyList<string> Languages { get; init; } = [];

    public MediaInfo? MediaInfo { get; init; }

    public IReadOnlyList<string> CustomFormats { get; init; } = [];

    public string? OriginalReleaseTitle { get; init; }

    public string? OriginalFileName { get; init; }
}
