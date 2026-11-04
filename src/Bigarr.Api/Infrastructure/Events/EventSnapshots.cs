using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Parsing;
using Bigarr.Parsing.Languages;
using Bigarr.Parsing.Model;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>Snapshots the slices put in their events when the entity alone does not say it all.</summary>
public static class EventSnapshots
{
    /// <summary>
    /// A release as the search would judge it now: what the parser reads in the title (quality, revision, group,
    /// languages, the film's original language when it reads none, like Radarr) and the custom formats it matches,
    /// scored against <paramref name="profile"/>.
    /// </summary>
    public static ReleaseRef Release(
        string title,
        string? indexer,
        long size,
        IndexerFlags flags,
        Movie? movie,
        QualityProfile? profile,
        CustomFormatCalculator? calculator,
        int? seeders = null,
        string? infoHash = null)
    {
        ParsedMovieInfo? parsed = null;
        try
        {
            parsed = Parser.ParseMovieTitle(title);
        }
        catch (Exception)
        {
            // an unparseable title still has a size and an indexer
        }

        var quality = parsed?.Quality?.Quality?.Name;
        var languages = (parsed?.Languages ?? []).Where(l => l.Id != Language.Unknown.Id).Select(l => l.Name).Distinct(StringComparer.Ordinal).ToList();
        if (languages.Count == 0 && IsoLanguages.Find(movie?.OriginalLanguage ?? string.Empty) is { } original)
        {
            languages.Add(original.Language.Name);
        }

        List<CustomFormatDefinition> matched = [];
        if (parsed is not null && calculator is not null)
        {
            var release = new ReleaseInfo { Guid = title, Title = title, IndexerName = indexer ?? string.Empty, Size = size, Flags = flags };
            matched = [.. calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie?.Year, movie?.OriginalLanguage))];
        }

        return new ReleaseRef(
            title,
            string.IsNullOrEmpty(quality) || quality == "Unknown" ? null : quality,
            indexer,
            size,
            string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed.ReleaseGroup,
            seeders,
            infoHash,
            parsed?.Quality?.Revision?.Version,
            languages,
            flags,
            matched.Select(m => m.Name).ToList(),
            matched.Select(m => m.Id).ToList(),
            profile is null ? 0 : ProfileRanking.FormatScore(profile, matched.Select(m => m.Id)));
    }

    /// <summary>Radarr's <c>downloadClientType</c>: the implementation of the client, <c>QBittorrent</c>.</summary>
    public static async Task<string?> DownloadClientTypeAsync(BigarrDbContext db, int? clientId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (clientId is null)
        {
            return null;
        }

        var implementation = await db.DownloadClients.AsNoTracking().Where(c => c.Id == clientId).Select(c => (DownloadClientImplementation?)c.Implementation).FirstOrDefaultAsync(ct);
        return implementation?.ToString();
    }
}
