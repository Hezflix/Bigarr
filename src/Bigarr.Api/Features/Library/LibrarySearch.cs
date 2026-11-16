using System.Globalization;
using System.Numerics;
using System.Text;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Library;

public enum LibraryItemKind
{
    Movie = 1,
    Series = 2,
}

/// <summary>One film or series of the library, as the command palette lists it.</summary>
/// <param name="MatchedTitle">The alternative title that matched, when the main title did not.</param>
public sealed record LibrarySearchResult(LibraryItemKind Kind, int Id, string Title, int? Year, string? PosterUrl, string? MatchedTitle);

public sealed record LibrarySearchResponse(List<LibrarySearchResult> Movies, List<LibrarySearchResult> Series);

/// <summary>
/// Films and series of the library by title, alternative titles and year, for the command palette. The library page
/// filters its own full list; the palette must not pull that list (versions, files, formats) on every keystroke, so it
/// reads only titles and ranks them here.
/// </summary>
public static class LibrarySearchEndpoints
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 25;

    public static IEndpointRouteBuilder MapLibrarySearch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/library/search", SearchAsync).WithTags("Library").RequireAuthorization(AuthPolicies.Api);
        return app;
    }

    private static async Task<Ok<LibrarySearchResponse>> SearchAsync(BigarrDbContext db, CancellationToken ct, string? term = null, int limit = DefaultLimit)
    {
        var query = LibraryTitleQuery.Parse(term);
        if (query is null)
        {
            return TypedResults.Ok(new LibrarySearchResponse([], []));
        }

        limit = Math.Clamp(limit, 1, MaxLimit);

        // Alternative titles are a JSON column: read whole, picked apart here. Images are another one, and only the few
        // rows that rank need their poster: they are read afterwards.
        var movies = (await db.Movies.AsNoTracking()
                .Select(m => new { m.Id, m.Title, m.Year, m.AlternativeTitles })
                .ToListAsync(ct))
            .Select(m => new LibraryTitleRow(m.Id, m.Title, m.Year, [], [.. m.AlternativeTitles.Select(a => a.Title)]));
        var series = (await db.Series.AsNoTracking()
                .Select(s => new { s.Id, s.Title, s.Year, s.AlternativeTitles })
                .ToListAsync(ct))
            .Select(s => new LibraryTitleRow(s.Id, s.Title, s.Year, [], [.. s.AlternativeTitles.Select(a => a.Title)]));

        var foundMovies = query.Rank(movies, LibraryItemKind.Movie, limit);
        var foundSeries = query.Rank(series, LibraryItemKind.Series, limit);

        var movieIds = foundMovies.Select(r => r.Id).ToList();
        var posters = (await db.Movies.AsNoTracking().Where(m => movieIds.Contains(m.Id)).Select(m => new { m.Id, m.Images }).ToListAsync(ct))
            .ToDictionary(m => m.Id, m => Poster(m.Images));
        var seriesIds = foundSeries.Select(r => r.Id).ToList();
        var seriesPosters = (await db.Series.AsNoTracking().Where(s => seriesIds.Contains(s.Id)).Select(s => new { s.Id, s.Images }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => Poster(s.Images));

        return TypedResults.Ok(new LibrarySearchResponse(
            [.. foundMovies.Select(r => r with { PosterUrl = posters.GetValueOrDefault(r.Id) })],
            [.. foundSeries.Select(r => r with { PosterUrl = seriesPosters.GetValueOrDefault(r.Id) })]));
    }

    private static string? Poster(List<MediaImage> images) => images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl;
}

public sealed record LibraryTitleRow(int Id, string Title, int? Year, List<MediaImage> Images, List<string> AlternativeTitles);

/// <summary>
/// A palette term against library titles. Case, accents and punctuation do not count ("amelie" finds "Amélie"), every
/// word of the term must start a word of the title, and a trailing year narrows to that year unless it is part of the
/// title ("blade runner 2049" still finds the film). A leading article does not count either way ("the matrix" finds
/// "Matrix", "reine des neiges" ranks "La Reine des neiges" as an exact title).
/// <para>
/// When that finds nothing worth showing, the term is read again with typos allowed: one slip in a word of five letters,
/// two in a longer one (a wrong, missing, extra or swapped letter), or a space left out or added. Those hits always rank
/// below every exact, prefix and contained match.
/// </para>
/// </summary>
public sealed class LibraryTitleQuery
{
    // Strict tiers, best first. The fuzzy tiers (below) all sit under the last one.
    private const int Exact = 100;
    private const int Prefix = 80;
    private const int WordsInOrder = 60;
    private const int WordsShuffled = 50;
    private const int Contained = 30;
    private const int ArticleCost = 2;
    private const int AlternativeCost = 5;

    // Fuzzy tiers: the compact (no spaces) reading, then a typo in a word, one point lost per slip.
    private const int Compact = 28;
    private const int Typo = 20;

    /// <summary>The words a title may start with and a search may leave out or add: English, French, Spanish, Italian, German, Dutch, Portuguese.</summary>
    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "le", "la", "les", "l", "un", "une", "el", "los", "las", "una", "il", "lo", "gli", "der", "die", "das", "ein", "eine", "het", "een", "o", "os", "as",
    };

    private readonly Reading[] _readings;
    private readonly int? _year;

    private LibraryTitleQuery(string[] words, int? year)
    {
        _year = year;
        _readings = Readings(words, year);
    }

    /// <summary>One way to read the term: all its words, or those left once the year or a leading article is set aside.</summary>
    private readonly record struct Reading(string[] Words, string Phrase, string Compact, uint[] Masks, bool NeedsYear, int Cost);

    private static Reading[] Readings(string[] words, int? year)
    {
        var readings = new List<Reading>();
        void Add(string[] w, bool needsYear, int cost)
        {
            if (w.Length > 0 && readings.All(r => r.NeedsYear != needsYear || !r.Words.AsSpan().SequenceEqual(w)))
            {
                readings.Add(new Reading(w, string.Join(' ', w), string.Concat(w), [.. w.Select(x => Mask(x))], needsYear, cost));
            }
        }

        Add(words, false, 0);
        Add(WithoutArticle(words), false, ArticleCost);
        if (year is not null)
        {
            // The year is a filter here: the title matches the other words and the year is the film's, so it ranks just above.
            var rest = words[..^1];
            Add(rest, true, -1);
            Add(WithoutArticle(rest), true, ArticleCost - 1);
        }

        return [.. readings];
    }

    private static string[] WithoutArticle(string[] words) => words.Length > 1 && Articles.Contains(words[0]) ? words[1..] : words;

    public static LibraryTitleQuery? Parse(string? term)
    {
        var words = Normalize(term ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        // "alien 1979", "alien (1979)": the last word is a year when it looks like one and is not the only word.
        if (words.Length > 1 && int.TryParse(words[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 1880 and <= 2200)
        {
            return new LibraryTitleQuery(words, year);
        }

        return new LibraryTitleQuery(words, null);
    }

    public List<LibrarySearchResult> Rank(IEnumerable<LibraryTitleRow> rows, LibraryItemKind kind, int limit)
    {
        // Titles are folded once, for the strict pass and the fuzzy one.
        var folded = rows.Select(row => (Row: row, Titles: Fold(row))).ToList();
        var matches = new (LibraryTitleRow Row, Match Match)[folded.Count];
        var hits = 0;
        for (var i = 0; i < folded.Count; i++)
        {
            var match = Best(folded[i].Titles, folded[i].Row.Year, fuzzy: false, memo: null);
            matches[i] = (folded[i].Row, match);
            hits += match.Score > 0 ? 1 : 0;
        }

        // Typos only matter when the strict matches do not fill the list: they could never rank in it.
        if (hits < limit)
        {
            var memo = new Dictionary<(string, string), int>();
            for (var i = 0; i < folded.Count; i++)
            {
                if (matches[i].Match.Score == 0)
                {
                    matches[i].Match = Best(folded[i].Titles, folded[i].Row.Year, fuzzy: true, memo);
                }
            }
        }

        return matches
            .Where(x => x.Match.Score > 0)
            .OrderByDescending(x => x.Match.Score)
            .ThenBy(x => x.Row.Title, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.Row.Year)
            .Take(limit)
            .Select(x => new LibrarySearchResult(
                kind,
                x.Row.Id,
                x.Row.Title,
                x.Row.Year,
                x.Row.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                x.Match.Alternative))
            .ToList();
    }

    private readonly record struct Match(int Score, string? Alternative);

    /// <summary>A title as shown, folded, with where its leading article ends; words are only cut when a typo makes them matter.</summary>
    private sealed class Title(string original, string text)
    {
        private string[]? _words;
        private string? _compact;

        public string Original { get; } = original;

        public string Text { get; } = text;

        /// <summary>Where the title starts once a leading article is set aside: 0 when it has none.</summary>
        public int CoreStart { get; } = CoreOffset(text);

        public ReadOnlySpan<char> Core => Text.AsSpan(CoreStart);

        public string[] Words => _words ??= Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        public string Compact => _compact ??= string.Concat(Words);
    }

    private static Title[] Fold(LibraryTitleRow row)
    {
        var titles = new Title[row.AlternativeTitles.Count + 1];
        titles[0] = new Title(row.Title, Normalize(row.Title));
        for (var i = 0; i < row.AlternativeTitles.Count; i++)
        {
            titles[i + 1] = new Title(row.AlternativeTitles[i], Normalize(row.AlternativeTitles[i]));
        }

        return titles;
    }

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> ArticleLookup = Articles.GetAlternateLookup<ReadOnlySpan<char>>();

    private static int CoreOffset(string text)
    {
        var space = text.IndexOf(' ');
        return space > 0 && ArticleLookup.Contains(text.AsSpan(0, space)) ? space + 1 : 0;
    }

    private Match Best(Title[] titles, int? year, bool fuzzy, Dictionary<(string, string), int>? memo)
    {
        var main = Score(titles[0], year, fuzzy, memo);
        var best = main;
        string? alternative = null;
        for (var i = 1; i < titles.Length; i++)
        {
            // An alternative title ranks a little below the same match on the main title.
            var score = Score(titles[i], year, fuzzy, memo);
            score = score > 0 ? score - AlternativeCost : 0;
            if (score > best)
            {
                best = score;
                alternative = titles[i].Original;
            }
        }

        // Named only when the main title does not match: "Matrix" under "The Matrix" would say nothing new.
        return new Match(best, main > 0 ? null : alternative);
    }

    private int Score(Title title, int? year, bool fuzzy, Dictionary<(string, string), int>? memo)
    {
        if (title.Text.Length == 0)
        {
            return 0;
        }

        var best = 0;
        foreach (var reading in _readings)
        {
            if (reading.NeedsYear && year != _year)
            {
                continue;
            }

            int score;
            if (fuzzy)
            {
                score = FuzzyScore(title, reading, memo!);
            }
            else
            {
                score = StrictScore(title.Text, reading);
                if (title.CoreStart > 0)
                {
                    score = Math.Max(score, StrictScore(title.Core, reading) - ArticleCost);
                }
            }

            if (score > 0)
            {
                best = Math.Max(best, score - reading.Cost);
            }
        }

        return best;
    }

    private static int StrictScore(ReadOnlySpan<char> title, Reading reading)
    {
        var phrase = reading.Phrase.AsSpan();
        if (title.SequenceEqual(phrase))
        {
            return Exact;
        }

        if (title.Length > phrase.Length && title[phrase.Length] == ' ' && title.StartsWith(phrase))
        {
            return Prefix;
        }

        foreach (var word in reading.Words)
        {
            if (!StartsAWord(title, word))
            {
                return title.Contains(phrase, StringComparison.Ordinal) ? Contained : 0;
            }
        }

        // Words in the title's order ("dark knight") rank above the same words shuffled.
        return title.Contains(phrase, StringComparison.Ordinal) ? WordsInOrder : WordsShuffled;
    }

    private static bool StartsAWord(ReadOnlySpan<char> title, string word)
    {
        for (var start = 0; start + word.Length <= title.Length; start++)
        {
            if ((start == 0 || title[start - 1] == ' ') && title[start..].StartsWith(word, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Typos allowed, word by word; or, when the spaces are what is wrong, on the title read as one word.</summary>
    private int FuzzyScore(Title title, Reading reading, Dictionary<(string, string), int> memo)
    {
        var total = 0;
        var matched = true;
        for (var q = 0; q < reading.Words.Length && matched; q++)
        {
            var word = reading.Words[q];
            var allowed = AllowedEdits(word.Length);
            var best = BestWordEdits(word, reading.Masks[q], allowed, title.Text, memo);
            if (best > allowed)
            {
                matched = false;
            }

            total += best;
        }

        if (matched && total is > 0 and <= 2)
        {
            return Typo - total;
        }

        // "lareine des neiges", "l'amour" typed "lamour": the same letters, other spaces. Compared on the start of the
        // title only, so the first letter must be right.
        if (reading.Compact.Length >= 5 && title.Text.Length > 0 && title.Text[0] == reading.Compact[0])
        {
            var allowed = reading.Compact.Length >= 8 ? 1 : 0;
            Span<char> compact = stackalloc char[reading.Compact.Length + 2];
            var length = 0;
            foreach (var c in title.Text)
            {
                if (c != ' ')
                {
                    compact[length++] = c;
                    if (length == compact.Length)
                    {
                        break;
                    }
                }
            }

            var edits = PrefixEdits(reading.Compact, compact[..length], allowed);
            if (edits <= allowed)
            {
                return Compact - edits;
            }
        }

        return 0;
    }

    /// <summary>The fewest edits for the word against any word of the title; more than <paramref name="allowed"/> when none is close enough.</summary>
    private static int BestWordEdits(string word, uint mask, int allowed, string title, Dictionary<(string, string), int> memo)
    {
        var best = allowed + 1;
        var rest = title.AsSpan();
        while (rest.Length > 0)
        {
            var space = rest.IndexOf(' ');
            var candidate = space < 0 ? rest : rest[..space];
            rest = space < 0 ? default : rest[(space + 1)..];

            if (candidate.StartsWith(word, StringComparison.Ordinal))
            {
                return 0;
            }

            // Cheapest checks first: too short to be the start of the word, or lacking letters no edit could supply.
            if (allowed == 0 || candidate.Length + allowed < word.Length || BitOperations.PopCount(mask & ~Mask(candidate)) > allowed)
            {
                continue;
            }

            var text = candidate.ToString();
            if (!memo.TryGetValue((word, text), out var edits))
            {
                edits = PrefixEdits(word, candidate, allowed);
                memo[(word, text)] = edits;
            }

            best = Math.Min(best, edits);
        }

        return best;
    }

    /// <summary>Slips a word of that length may carry: none up to three letters, one up to six, two beyond.</summary>
    private static int AllowedEdits(int length) => length <= 3 ? 0 : length <= 6 ? 1 : 2;

    /// <summary>
    /// The fewest edits (insert, delete, replace, swap two neighbours) turning <paramref name="word"/> into the start of
    /// <paramref name="target"/>: a word typed so far, with a slip in it. More than <paramref name="max"/> reports max + 1.
    /// </summary>
    private static int PrefixEdits(ReadOnlySpan<char> word, ReadOnlySpan<char> target, int max)
    {
        var m = word.Length;
        var limit = Math.Min(target.Length, m + max);
        if (limit < m - max || m > 48)
        {
            return max + 1;
        }

        Span<int> two = stackalloc int[limit + 1];
        Span<int> one = stackalloc int[limit + 1];
        Span<int> row = stackalloc int[limit + 1];
        for (var j = 0; j <= limit; j++)
        {
            one[j] = j;
        }

        for (var i = 1; i <= m; i++)
        {
            row[0] = i;
            var rowMin = i;
            for (var j = 1; j <= limit; j++)
            {
                var cost = word[i - 1] == target[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(one[j] + 1, row[j - 1] + 1), one[j - 1] + cost);
                if (i > 1 && j > 1 && word[i - 1] == target[j - 2] && word[i - 2] == target[j - 1])
                {
                    value = Math.Min(value, two[j - 2] + 1);
                }

                row[j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > max)
            {
                return max + 1;
            }

            var spare = two;
            two = one;
            one = row;
            row = spare;
        }

        var best = max + 1;
        for (var j = Math.Max(0, m - max); j <= limit; j++)
        {
            best = Math.Min(best, one[j]);
        }

        return best;
    }

    /// <summary>The letters a word holds, one bit each (the others share the last bits).</summary>
    private static uint Mask(ReadOnlySpan<char> text)
    {
        var mask = 0u;
        foreach (var c in text)
        {
            mask |= 1u << (c is >= 'a' and <= 'z' ? c - 'a' : 26 + c % 6);
        }

        return mask;
    }

    /// <summary>Elided articles and pronouns of French, Italian and Catalan: "l'amour" is "l amour", "schindler's" stays one word.</summary>
    private static readonly HashSet<string> Elisions = new(StringComparer.Ordinal) { "l", "d", "j", "m", "n", "s", "t", "c", "qu", "jusqu", "lorsqu", "puisqu", "dell", "all", "nell", "sull", "un" };

    /// <summary>Lower case, no accents, letters and digits only, single spaces.</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = true;
        var wordStart = 0;
        var source = IsAscii(text) ? text : text.Normalize(NormalizationForm.FormD);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c < 128 ? char.IsAsciiLetterOrDigit(c) : IsLetterOrDigit(c))
            {
                if (space)
                {
                    wordStart = builder.Length;
                }

                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (c >= 128 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                // The accent of a letter, once the text is decomposed.
            }
            else if (c is '\'' or '\u2019')
            {
                // "Schindler's" is one word; "l'amour" is two.
                if (!space && i + 1 < source.Length && char.IsLetterOrDigit(source[i + 1]) && Elisions.Contains(builder.ToString(wordStart, builder.Length - wordStart)))
                {
                    builder.Append(' ');
                    space = true;
                }
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsAscii(string text) => System.Text.Ascii.IsValid(text);

    private static bool IsLetterOrDigit(char c) => char.IsLetterOrDigit(c);
}
