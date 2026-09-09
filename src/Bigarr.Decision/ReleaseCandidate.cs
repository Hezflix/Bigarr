using Bigarr.Domain.Indexers;
using Bigarr.Parsing.Model;

namespace Bigarr.Decision;

/// <summary>A release together with what the parser made of its title. Movie matching happened before this point.</summary>
public sealed record ReleaseCandidate(ReleaseInfo Release, ParsedMovieInfo Parsed, int MinimumSeeders);
