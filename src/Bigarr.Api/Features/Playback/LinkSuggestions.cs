using System.Globalization;
using System.Text;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>Why a film was suggested, the surest first.</summary>
public enum SuggestionReason
{
    /// <summary>The server item's TMDB id is the film's.</summary>
    SameTmdbId = 1,

    /// <summary>The server item's IMDb id is the film's.</summary>
    SameImdbId = 2,

    /// <summary>TMDB's search for the item's title (and year) found the film, which is in the library.</summary>
    TmdbSearch = 3,

    /// <summary>One of the film's titles is the item's, articles, accents and punctuation aside, and the years agree.</summary>
    TitleAndYear = 4,

    /// <summary>The same title, of another year (or the item has none).</summary>
    SameTitle = 5,

    /// <summary>A title close to the item's (a typo, a word more or less).</summary>
    CloseTitle = 6,
}

/// <summary>A library film that may be the one played, with every reason it came up; <c>Score</c> orders them.</summary>
public sealed record LinkSuggestion(int MovieId, string Title, int? Year, string? PosterUrl, int Versions, int Score, List<SuggestionReason> Reasons);

/// <summary><c>TmdbSearched</c>: TMDB answered; false when it could not be reached (the library alone was looked at).</summary>
public sealed record LinkSuggestionsResponse(int ItemId, List<LinkSuggestion> Suggestions, bool TmdbSearched);

/// <summary>
/// Films of the library that a film the server played may be, for the link picker, on request only. From the item's ids,
/// TMDB's search for its title and original title (with its year), and the library's titles (original and alternative
/// ones included) compared without accents, articles or punctuation, close ones too. Never links anything itself.
/// </summary>
public sealed class LinkSuggestions(BigarrDbContext db, IMetadataProvider metadata, ILogger<LinkSuggestions> logger)
{
    /// <summary>How many suggestions the picker shows.</summary>
    public const int MaxSuggestions = 5;

    /// <summary>A close title is one at least this similar (1 is the same).</summary>
    public const double CloseEnough = 0.75;

    /// <summary>What is known of the played film.</summary>
    public sealed record ItemFacts(string Title, string? OriginalTitle, int? Year, int? TmdbId, string? ImdbId);

    /// <summary>A library film and every title it goes by.</summary>
    public sealed record Film(int Id, int TmdbId, string? ImdbId, string Title, string? OriginalTitle, int? Year, IReadOnlyList<string> AlternativeTitles);

    public async Task<Result<LinkSuggestionsResponse>> SuggestAsync(int itemId, CancellationToken ct)
    {
        var item = await db.MediaServerItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null)
        {
            return Failure.NotFound($"Media server item {itemId}");
        }

        if (item.Kind != MediaItemKind.Movie)
        {
            return Failure.Invalid("Not a film", "Only a film the server played can be linked to a Bigarr film.");
        }

        var facts = new ItemFacts(item.Title, item.OriginalTitle, item.Year, item.TmdbId, item.ImdbId);
        var films = await FilmsAsync(ct);
        var (hits, searched) = await SearchTmdbAsync(facts, ct);
        var suggestions = await WithPostersAsync(Rank(facts, films, hits), ct);
        return Result<LinkSuggestionsResponse>.Ok(new LinkSuggestionsResponse(itemId, suggestions, searched));
    }

    /// <summary>Every library film with the titles it goes by, read once for any number of items.</summary>
    public async Task<List<Film>> FilmsAsync(CancellationToken ct)
    {
        var rows = await db.Movies.AsNoTracking()
            .Select(m => new { m.Id, m.TmdbId, m.ImdbId, m.Title, m.OriginalTitle, m.Year, m.AlternativeTitles })
            .ToListAsync(ct);
        return rows.Select(m => new Film(m.Id, m.TmdbId, m.ImdbId, m.Title, m.OriginalTitle, m.Year, m.AlternativeTitles.Select(a => a.Title).ToList())).ToList();
    }

    /// <summary>The suggestions with each film's poster and number of versions.</summary>
    public async Task<List<LinkSuggestion>> WithPostersAsync(List<LinkSuggestion> ranked, CancellationToken ct)
    {
        if (ranked.Count == 0)
        {
            return ranked;
        }

        var ids = ranked.Select(r => r.MovieId).Distinct().ToList();
        var extra = await db.Movies.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Images, Versions = db.MovieVersions.Count(v => v.MovieId == m.Id) })
            .ToDictionaryAsync(m => m.Id, ct);
        return ranked.Select(r => r with
        {
            PosterUrl = extra[r.MovieId].Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            Versions = extra[r.MovieId].Versions,
        }).ToList();
    }

    /// <summary>TMDB's films for the item's titles, the year's first; nothing (and false) when TMDB cannot be reached.</summary>
    public async Task<(List<MovieMetadata> Hits, bool Searched)> SearchTmdbAsync(ItemFacts facts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var hits = new List<MovieMetadata>();
        try
        {
            foreach (var title in new[] { facts.Title, facts.OriginalTitle }.OfType<string>().Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                hits.AddRange(await metadata.SearchAsync(title, facts.Year, ct));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("TMDB could not be searched for {Title}: {Error}", facts.Title, ex.Message);
            return (hits, false);
        }

        return (hits, true);
    }

    /// <summary>How far a sure match must lead the runner-up: a close second means the owner should choose.</summary>
    public const int SureLead = 15;

    /// <summary>
    /// The best suggestion when it is sure enough to link without looking, else null. Sure is: the item's TMDB or IMDb id
    /// is the film's (and the runner-up has no id of its own), or its title, or TMDB's search for it, found the film with
    /// the very same year; and the film leads the runner-up by <see cref="SureLead"/> points at least. A year off by one,
    /// an item without a year, a close title or a same title alone are never sure: those stay for the picker.
    /// </summary>
    public static LinkSuggestion? Sure(ItemFacts item, IReadOnlyList<Film> films, IReadOnlyList<LinkSuggestion> ranked)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(films);
        ArgumentNullException.ThrowIfNull(ranked);

        if (ranked.Count == 0)
        {
            return null;
        }

        var best = ranked[0];
        var second = ranked.Count > 1 ? ranked[1] : null;
        static bool ById(LinkSuggestion s) => s.Reasons.Contains(SuggestionReason.SameTmdbId) || s.Reasons.Contains(SuggestionReason.SameImdbId);
        if (ById(best))
        {
            return second is not null && ById(second) ? null : best;
        }

        var year = films.FirstOrDefault(f => f.Id == best.MovieId)?.Year;
        var found = best.Reasons.Contains(SuggestionReason.TitleAndYear) || best.Reasons.Contains(SuggestionReason.TmdbSearch);
        if (!found || item.Year is null || year != item.Year)
        {
            return null;
        }

        return second is null || best.Score - second.Score >= SureLead ? best : null;
    }

    /// <summary>
    /// The library films the item may be, most likely first: each reason scores, the best one counts and every other
    /// adds a little. A TMDB hit counts only when the film is in the library; a close title only from <see cref="CloseEnough"/>.
    /// </summary>
    public static List<LinkSuggestion> Rank(ItemFacts item, IReadOnlyList<Film> films, IReadOnlyList<MovieMetadata> tmdbHits)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(films);
        ArgumentNullException.ThrowIfNull(tmdbHits);

        var found = new Dictionary<int, (Film Film, Dictionary<SuggestionReason, int> Reasons)>();
        void Add(Film film, SuggestionReason reason, int score)
        {
            if (!found.TryGetValue(film.Id, out var entry))
            {
                found[film.Id] = entry = (film, []);
            }

            entry.Reasons[reason] = Math.Max(score, entry.Reasons.GetValueOrDefault(reason));
        }

        var byTmdb = films.GroupBy(f => f.TmdbId).ToDictionary(g => g.Key, g => g.First());
        if (item.TmdbId is { } tmdbId && byTmdb.TryGetValue(tmdbId, out var sameTmdb))
        {
            Add(sameTmdb, SuggestionReason.SameTmdbId, 100);
        }

        if (!string.IsNullOrWhiteSpace(item.ImdbId))
        {
            foreach (var film in films.Where(f => string.Equals(f.ImdbId, item.ImdbId, StringComparison.OrdinalIgnoreCase)))
            {
                Add(film, SuggestionReason.SameImdbId, 95);
            }
        }

        // TMDB's own order counts a little: its first answer is its best.
        for (var i = 0; i < tmdbHits.Count; i++)
        {
            if (byTmdb.TryGetValue(tmdbHits[i].TmdbId, out var film))
            {
                Add(film, SuggestionReason.TmdbSearch, 70 + YearBonus(item.Year, tmdbHits[i].Year ?? film.Year, 15, 8) + Math.Max(0, 5 - i));
            }
        }

        var itemTitles = new[] { item.Title, item.OriginalTitle }.Select(Fold).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var film in films)
        {
            var best = 0.0;
            foreach (var title in film.AlternativeTitles.Append(film.Title).Append(film.OriginalTitle).OfType<string>().Select(Fold).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal))
            {
                foreach (var mine in itemTitles)
                {
                    best = Math.Max(best, Similarity(mine, title));
                }
            }

            if (best >= 1)
            {
                if (item.Year is null || film.Year is null)
                {
                    Add(film, SuggestionReason.SameTitle, 60);
                }
                else if (YearBonus(item.Year, film.Year, 1, 1) > 0)
                {
                    Add(film, SuggestionReason.TitleAndYear, item.Year == film.Year ? 90 : 82);
                }
                else
                {
                    Add(film, SuggestionReason.SameTitle, 45);
                }
            }
            else if (best >= CloseEnough)
            {
                Add(film, SuggestionReason.CloseTitle, 20 + (int)Math.Round(30 * best) + YearBonus(item.Year, film.Year, 15, 8));
            }
        }

        return found.Values
            .Select(e =>
            {
                var reasons = e.Reasons.OrderByDescending(r => r.Value).ThenBy(r => r.Key).ToList();
                var score = Math.Min(100, reasons[0].Value + (5 * (reasons.Count - 1)));
                return new LinkSuggestion(e.Film.Id, e.Film.Title, e.Film.Year, null, 0, score, reasons.Select(r => r.Key).ToList());
            })
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.MovieId)
            .Take(MaxSuggestions)
            .ToList();
    }

    /// <summary>The same year scores <paramref name="same"/>, a year apart (release dates differ by country) <paramref name="near"/>.</summary>
    private static int YearBonus(int? a, int? b, int same, int near) => (a, b) switch
    {
        ({ } x, { } y) when x == y => same,
        ({ } x, { } y) when Math.Abs(x - y) == 1 => near,
        _ => 0,
    };

    /// <summary>Articles a title may start or go on with, in the languages the library's films are most often titled in.</summary>
    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "le", "la", "les", "l", "un", "une", "des", "der", "die", "das", "ein", "eine", "el", "los", "las", "il", "lo", "gli", "i", "o", "os", "as",
    };

    /// <summary>"L'Écho pourpre !" → "echo pourpre": lower case, no accents, punctuation as spaces, no articles.</summary>
    public static string Fold(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        var all = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words = all.Where(w => !Articles.Contains(w)).ToArray();

        // A title made of articles only ("Les") keeps them.
        return string.Join(' ', words.Length > 0 ? words : all);
    }

    /// <summary>1 for the same folded title, down to 0: one minus the edit distance over the longer length.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1;
        }

        var longer = Math.Max(a.Length, b.Length);
        // Lengths too far apart cannot reach a close title: skip the distance.
        if (Math.Abs(a.Length - b.Length) > longer * (1 - CloseEnough))
        {
            return 0;
        }

        return 1 - ((double)Distance(a, b) / longer);
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
