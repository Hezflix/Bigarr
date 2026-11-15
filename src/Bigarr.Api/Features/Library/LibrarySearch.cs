using System.Globalization;
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

        // Alternative titles and images are JSON columns: read whole, picked apart here.
        var movies = (await db.Movies.AsNoTracking()
                .Select(m => new { m.Id, m.Title, m.Year, m.Images, m.AlternativeTitles })
                .ToListAsync(ct))
            .Select(m => new LibraryTitleRow(m.Id, m.Title, m.Year, m.Images, [.. m.AlternativeTitles.Select(a => a.Title)]));
        var series = (await db.Series.AsNoTracking()
                .Select(s => new { s.Id, s.Title, s.Year, s.Images, s.AlternativeTitles })
                .ToListAsync(ct))
            .Select(s => new LibraryTitleRow(s.Id, s.Title, s.Year, s.Images, [.. s.AlternativeTitles.Select(a => a.Title)]));

        return TypedResults.Ok(new LibrarySearchResponse(
            query.Rank(movies, LibraryItemKind.Movie, limit),
            query.Rank(series, LibraryItemKind.Series, limit)));
    }
}

public sealed record LibraryTitleRow(int Id, string Title, int? Year, List<MediaImage> Images, List<string> AlternativeTitles);

/// <summary>
/// A palette term against library titles. Case, accents and punctuation do not count ("amelie" finds "Amélie"), every
/// word of the term must start a word of the title, and a trailing year narrows to that year unless it is part of the
/// title ("blade runner 2049" still finds the film).
/// </summary>
public sealed class LibraryTitleQuery
{
    private readonly string[] _words;
    private readonly string _phrase;
    private readonly int? _year;

    private LibraryTitleQuery(string[] words, int? year)
    {
        _words = words;
        _phrase = string.Join(' ', words);
        _year = year;
    }

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

    public List<LibrarySearchResult> Rank(IEnumerable<LibraryTitleRow> rows, LibraryItemKind kind, int limit) =>
        rows.Select(row => (row, match: Match(row)))
            .Where(x => x.match.Score > 0)
            .OrderByDescending(x => x.match.Score)
            .ThenBy(x => x.row.Title, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.row.Year)
            .Take(limit)
            .Select(x => new LibrarySearchResult(
                kind,
                x.row.Id,
                x.row.Title,
                x.row.Year,
                x.row.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                x.match.Alternative))
            .ToList();

    private (int Score, string? Alternative) Match(LibraryTitleRow row)
    {
        var main = Score(row.Title, row.Year);
        var best = main;
        string? alternative = null;
        foreach (var title in row.AlternativeTitles)
        {
            // An alternative title ranks a little below the same match on the main title.
            var score = Score(title, row.Year) - 5;
            if (score > best)
            {
                best = score;
                alternative = title;
            }
        }

        // Named only when the main title does not match: "Matrix" under "The Matrix" would say nothing new.
        return (best, main > 0 ? null : alternative);
    }

    private int Score(string title, int? year)
    {
        var normalized = Normalize(title);
        var score = Score(normalized, _words, _phrase);
        if (_year is { } wanted)
        {
            // With the year as a filter: the title matches the other words and the year is the film's.
            var withoutYear = _words[..^1];
            var yearScore = year == wanted ? Score(normalized, withoutYear, string.Join(' ', withoutYear)) : 0;
            if (yearScore > 0)
            {
                score = Math.Max(score, yearScore + 1);
            }
        }

        return score;
    }

    private static int Score(string title, string[] words, string phrase)
    {
        if (title.Length == 0)
        {
            return 0;
        }

        if (title == phrase)
        {
            return 100;
        }

        if (title.StartsWith(phrase + " ", StringComparison.Ordinal))
        {
            return 80;
        }

        var titleWords = title.Split(' ');
        if (words.All(w => titleWords.Any(t => t.StartsWith(w, StringComparison.Ordinal))))
        {
            // Words in the title's order ("dark knight") rank above the same words shuffled.
            return title.Contains(phrase, StringComparison.Ordinal) ? 60 : 50;
        }

        return title.Contains(phrase, StringComparison.Ordinal) ? 30 : 0;
    }

    /// <summary>Lower case, no accents, letters and digits only, single spaces.</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = true;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (c is '\'' or '’')
            {
                // "Schindler's" is one word.
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
