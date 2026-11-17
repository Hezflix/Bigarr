using Bigarr.Api.Infrastructure;
using Bigarr.Parsing.Languages;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Downloads;

/// <summary>
/// Sonarr's "override and grab": the user grabs a release from the interactive search as what it really is, when its title
/// reads wrong. The quality and the languages given travel on the <see cref="Bigarr.Domain.Acquisition.Grab"/> to the
/// import, which takes them over the parse; for a series the episodes chosen are the grab's targets already.
/// </summary>
public static class GrabOverrides
{
    /// <summary>A known quality other than Unknown, languages Bigarr knows (not Any or Original), episodes only for a series.</summary>
    public static Failure? Validate(GrabRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.QualityId is { } id && (id == ParserQuality.Unknown.Id || ParserQuality.All.All(q => q.Id != id)))
        {
            return Failure.Invalid("Unknown quality", $"Quality {id} is not one Bigarr knows.");
        }

        if (request.Languages is { } languages)
        {
            if (languages.Count == 0)
            {
                return Failure.Invalid("No language", "Pick at least one language, or leave the languages out to keep what the title says.");
            }

            var unknown = languages.Where(l => Find(l) is null).ToList();
            if (unknown.Count > 0)
            {
                return Failure.Invalid("Unknown language", $"{string.Join(", ", unknown)}: not a language Bigarr knows.");
            }
        }

        if (request.EpisodesOverridden == true && !request.IsSeries)
        {
            return Failure.Invalid("Not a series grab", "Only a series grab has episodes to override.");
        }

        return null;
    }

    /// <summary>The languages as the parser spells them, once each, in the order given; null when not overridden.</summary>
    public static List<string>? Languages(GrabRequest request) =>
        request.Languages?.Select(Find).Where(l => l is not null).Select(l => l!.Name).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The quality's name (<c>Bluray-1080p</c>); null when not overridden.</summary>
    public static string? QualityName(int? qualityId) => qualityId is { } id ? ParserQuality.FindById(id).Name : null;

    /// <summary>
    /// The release as the events and the picks describe it: the user's quality and languages over the title's. The custom
    /// formats stay those of the title, which is what the indexer and the client know the release by.
    /// </summary>
    public static Bigarr.Domain.Events.ReleaseRef Apply(Bigarr.Domain.Events.ReleaseRef release, GrabRequest request)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(request);
        return release with { Quality = QualityName(request.QualityId) ?? release.Quality, Languages = Languages(request) ?? release.Languages };
    }

    private static Language? Find(string name) =>
        Language.All.FirstOrDefault(l => l.Id > 0 && string.Equals(l.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
