using System.Collections.Concurrent;
using System.Diagnostics;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>A film the server played and linked to no Bigarr film: its title on the server, its year, its plays.</summary>
public sealed record SureMatchItem(int Id, string Title, int? Year, int Plays);

/// <summary>An item and the film it surely is, with the picker's reasons and score.</summary>
public sealed record SureMatch(SureMatchItem Item, LinkSuggestion Film);

/// <summary>
/// The sure matches, most played first; <c>WithoutSureMatch</c>: the items left for the picker. <c>TmdbSearched</c> is
/// false when TMDB could not be reached for some of them (the library alone was looked at).
/// </summary>
public sealed record SureMatchesResponse(List<SureMatch> Matches, int WithoutSureMatch, bool TmdbSearched);

/// <summary>
/// Every film the server played that is linked to no Bigarr film, ranked like the link picker, keeping the ones whose
/// best film is sure (<see cref="LinkSuggestions.Sure"/>). Read only, on request: nothing is linked here. An item
/// unlinked by hand is left alone. TMDB is searched a few items at a time, and not again once it cannot be reached.
/// </summary>
public sealed class SureMatches(BigarrDbContext db, LinkSuggestions suggestions, ILogger<SureMatches> logger)
{
    /// <summary>TMDB searches at once: about a hundred items take a few seconds without crowding its rate limit.</summary>
    public const int Searches = 6;

    public async Task<SureMatchesResponse> PreviewAsync(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();

        // The Plays page's "Films not linked": film plays tied to no film, on an item tied to none.
        var items = await db.Plays.AsNoTracking()
            .Where(p => p.Item.Kind == MediaItemKind.Movie && p.MovieId == null && p.Item.MovieId == null)
            .GroupBy(p => new { p.Item.Id, p.Item.Title, p.Item.OriginalTitle, p.Item.Year, p.Item.TmdbId, p.Item.ImdbId, p.Item.LinkedByHand })
            .Select(g => new { g.Key.Id, g.Key.Title, g.Key.OriginalTitle, g.Key.Year, g.Key.TmdbId, g.Key.ImdbId, g.Key.LinkedByHand, Plays = g.Count() })
            .ToListAsync(ct);
        var candidates = items.Where(i => !i.LinkedByHand).ToList();
        var films = await suggestions.FilmsAsync(ct);

        var sure = new ConcurrentBag<(SureMatchItem Item, LinkSuggestion Film)>();
        var unreachable = 0;
        await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = Searches, CancellationToken = ct }, async (item, token) =>
        {
            var facts = new LinkSuggestions.ItemFacts(item.Title, item.OriginalTitle, item.Year, item.TmdbId, item.ImdbId);
            IReadOnlyList<MovieMetadata> hits = [];
            if (Volatile.Read(ref unreachable) == 0)
            {
                var (found, searched) = await suggestions.SearchTmdbAsync(facts, token);
                hits = found;
                if (!searched)
                {
                    Interlocked.Exchange(ref unreachable, 1);
                }
            }

            if (LinkSuggestions.Sure(facts, films, LinkSuggestions.Rank(facts, films, hits)) is { } best)
            {
                sure.Add((new SureMatchItem(item.Id, item.Title, item.Year, item.Plays), best));
            }
        });

        var found = sure.ToList();
        var withPosters = await suggestions.WithPostersAsync(found.Select(s => s.Film).ToList(), ct);
        var matches = found.Zip(withPosters, (s, film) => new SureMatch(s.Item, film))
            .OrderByDescending(m => m.Item.Plays)
            .ThenBy(m => m.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Item.Id)
            .ToList();

        logger.LogInformation("Sure matches: {Sure} of {Items} film(s) not linked, in {Elapsed} ms", matches.Count, items.Count, watch.ElapsedMilliseconds);
        return new SureMatchesResponse(matches, items.Count - matches.Count, unreachable == 0);
    }
}
