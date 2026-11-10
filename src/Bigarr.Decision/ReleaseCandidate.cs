using Bigarr.Domain.Indexers;
using Bigarr.Parsing.Episodes.Model;
using Bigarr.Parsing.Model;
using Bigarr.Parsing.Qualities;

namespace Bigarr.Decision;

/// <summary>
/// A release together with what the parser made of its title: a film name (<see cref="Parsed"/>) or an episode name
/// (<see cref="ParsedEpisode"/>). Matching to a film or to episodes happened before this point. The specifications read
/// the shared facts (<see cref="Quality"/>, <see cref="Edition"/>, <see cref="ReleaseGroup"/>), so the same rules judge
/// both kinds.
/// </summary>
public sealed record ReleaseCandidate
{
    public ReleaseCandidate(ReleaseInfo release, ParsedMovieInfo parsed, int minimumSeeders)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(parsed);

        Release = release;
        Parsed = parsed;
        MinimumSeeders = minimumSeeders;
        Quality = parsed.Quality;
        Edition = string.IsNullOrWhiteSpace(parsed.Edition) ? null : parsed.Edition;
        ReleaseGroup = string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? null : parsed.ReleaseGroup;
    }

    /// <summary>An episode release. Sonarr's parser reads no edition: an edition constraint is matched on the title instead.</summary>
    public ReleaseCandidate(ReleaseInfo release, ParsedEpisodeInfo parsed, int minimumSeeders)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(parsed);

        Release = release;
        ParsedEpisode = parsed;
        MinimumSeeders = minimumSeeders;
        Quality = parsed.Quality;
        ReleaseGroup = string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? null : parsed.ReleaseGroup;
    }

    public ReleaseInfo Release { get; }

    /// <summary>Set for a film release.</summary>
    public ParsedMovieInfo? Parsed { get; }

    /// <summary>Set for an episode release.</summary>
    public ParsedEpisodeInfo? ParsedEpisode { get; }

    public int MinimumSeeders { get; }

    public QualityModel? Quality { get; }

    public string? Edition { get; }

    public string? ReleaseGroup { get; }

    /// <summary>Custom formats the release matches. Profile-independent, computed once per release; scores come from each profile.</summary>
    public IReadOnlyList<int> CustomFormatIds { get; init; } = [];

    /// <summary>What a series search asked for, when the release comes from one (Sonarr's search criteria). Null for RSS.</summary>
    public EpisodeSearchScope? Search { get; init; }

    public bool IsEpisode => ParsedEpisode is not null;
}

public enum EpisodeSearchKind
{
    Episode = 1,
    Season = 2,
    Series = 3,
}

/// <summary>
/// The episodes a search was for, by shared episode id. An episode search rejects packs and other episodes, a season
/// search other seasons (Sonarr's <c>SingleEpisodeSearchMatchSpecification</c>, <c>SeasonMatchSpecification</c> and
/// <c>EpisodeRequestedSpecification</c>).
/// </summary>
public sealed record EpisodeSearchScope(EpisodeSearchKind Kind, IReadOnlySet<int> EpisodeIds, int? SeasonNumber = null, string? Label = null);
