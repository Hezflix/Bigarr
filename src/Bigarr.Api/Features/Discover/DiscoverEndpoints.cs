using System.Globalization;
using System.Security.Claims;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.Discover;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Discover;

/// <summary>
/// Discover: the TMDB catalogue for every signed-in user, with each title's availability read from the library. These
/// are the only endpoints opened to people who are not the admin (policy <see cref="AuthPolicies.SignedIn"/>); their
/// answers carry TMDB data and availability states, nothing else of the library. Row settings stay the admin's.
/// </summary>
public static class DiscoverEndpoints
{
    public static IEndpointRouteBuilder MapDiscover(this IEndpointRouteBuilder app)
    {
        var open = app.MapGroup("/api/v1/discover").WithTags("Discover").RequireAuthorization(AuthPolicies.SignedIn);
        open.MapGet("/", HomeAsync);
        open.MapGet("/sliders/{id:int}", SliderAsync);
        open.MapGet("/movies", (int? genre, int? keyword, int? studio, int? page, DiscoverService discover, CancellationToken ct) =>
            Guard(discover, async () => TypedResults.Ok(await discover.MoviesAsync(genre, keyword, studio, Page(page), ct))));
        open.MapGet("/series", (int? genre, int? keyword, int? network, int? page, DiscoverService discover, CancellationToken ct) =>
            Guard(discover, async () => TypedResults.Ok(await discover.SeriesAsync(genre, keyword, network, Page(page), ct))));
        open.MapGet("/search", SearchAsync);
        open.MapGet("/movies/{tmdbId:int}", MovieAsync);
        open.MapGet("/series/{tmdbId:int}", SeriesAsync);
        open.MapGet("/people/{tmdbId:int}", PersonAsync);

        var admin = app.MapGroup("/api/v1/discover").WithTags("Discover").RequireAuthorization(AuthPolicies.Session);
        admin.MapGet("/settings", SettingsAsync);
        admin.MapPut("/settings", UpdateSettingsAsync);
        admin.MapGet("/lookup", LookupAsync);
        return app;
    }

    private static int Page(int? page) => Math.Clamp(page ?? 1, 1, TmdbDiscoverClient.MaxPage);

    private static async Task<Results<Ok<T>, ProblemHttpResult>> Guard<T>(DiscoverService discover, Func<Task<Ok<T>>> run) =>
        discover.Configured ? await run() : NotConfigured();

    private static ProblemHttpResult NotConfigured() =>
        TypedResults.Problem(title: "Discover needs a TMDB API key.", statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<Ok<DiscoverHomeResponse>> HomeAsync(BigarrDbContext db, DiscoverService discover, LibraryAvailability library, CancellationToken ct)
    {
        var fourK = await library.FourKVersionAsync(ct) is not null;
        if (!discover.Configured)
        {
            return TypedResults.Ok(new DiscoverHomeResponse(false, fourK, []));
        }

        var sliders = await db.DiscoverSliders.AsNoTracking().Where(s => s.Enabled).OrderBy(s => s.Position).ThenBy(s => s.Id).ToListAsync(ct);
        var rows = new List<DiscoverSliderResponse>();
        foreach (var slider in sliders)
        {
            var tiles = DiscoverSlider.ShowsTiles(slider.Kind) ? await discover.TilesAsync(slider.Kind, ct) : null;
            rows.Add(new DiscoverSliderResponse(slider.Id, slider.Kind, slider.Title, slider.Value, tiles));
        }

        return TypedResults.Ok(new DiscoverHomeResponse(true, fourK, rows));
    }

    private static async Task<Results<Ok<DiscoverPage>, NotFound, ProblemHttpResult>> SliderAsync(
        int id, int? page, ClaimsPrincipal principal, BigarrDbContext db, DiscoverService discover, CancellationToken ct)
    {
        if (!discover.Configured)
        {
            return NotConfigured();
        }

        // A switched-off row stays reachable for the admin, who previews it from the settings.
        var slider = await db.DiscoverSliders.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (slider is null || (!slider.Enabled && !principal.IsAdmin()))
        {
            return TypedResults.NotFound();
        }

        var result = await discover.SliderPageAsync(slider, Page(page), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<DiscoverPage>, ProblemHttpResult>> SearchAsync(string? query, int? page, DiscoverService discover, CancellationToken ct)
    {
        if (!discover.Configured)
        {
            return NotConfigured();
        }

        return string.IsNullOrWhiteSpace(query)
            ? TypedResults.Ok(new DiscoverPage(1, 0, 0, []))
            : TypedResults.Ok(await discover.SearchAsync(query.Trim(), Page(page), ct));
    }

    private static async Task<Results<Ok<DiscoverMovieResponse>, NotFound, ProblemHttpResult>> MovieAsync(int tmdbId, ClaimsPrincipal principal, DiscoverService discover, CancellationToken ct)
    {
        if (!discover.Configured)
        {
            return NotConfigured();
        }

        var movie = await discover.MovieAsync(tmdbId, principal.IsAdmin(), ct);
        return movie is null ? TypedResults.NotFound() : TypedResults.Ok(movie);
    }

    private static async Task<Results<Ok<DiscoverSeriesResponse>, NotFound, ProblemHttpResult>> SeriesAsync(int tmdbId, ClaimsPrincipal principal, DiscoverService discover, CancellationToken ct)
    {
        if (!discover.Configured)
        {
            return NotConfigured();
        }

        var series = await discover.SeriesAsync(tmdbId, principal.IsAdmin(), ct);
        return series is null ? TypedResults.NotFound() : TypedResults.Ok(series);
    }

    private static async Task<Results<Ok<DiscoverPersonResponse>, NotFound, ProblemHttpResult>> PersonAsync(int tmdbId, DiscoverService discover, CancellationToken ct)
    {
        if (!discover.Configured)
        {
            return NotConfigured();
        }

        var person = await discover.PersonAsync(tmdbId, ct);
        return person is null ? TypedResults.NotFound() : TypedResults.Ok(person);
    }

    // ---- settings (the admin's) ----------------------------------------------------------------------------------

    private static async Task<Ok<DiscoverSettingsResponse>> SettingsAsync(BigarrDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ReadSettingsAsync(db, ct));

    private static async Task<DiscoverSettingsResponse> ReadSettingsAsync(BigarrDbContext db, CancellationToken ct)
    {
        var settings = await UserSettings.ReadAsync(db, ct);
        var names = (await db.MovieVersions.AsNoTracking().Select(v => v.Name).Distinct().ToListAsync(ct))
            .Concat(await db.SeriesVersions.AsNoTracking().Select(v => v.Name).Distinct().ToListAsync(ct))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var sliders = await db.DiscoverSliders.AsNoTracking().OrderBy(s => s.Position).ThenBy(s => s.Id).ToListAsync(ct);
        return new DiscoverSettingsResponse(
            string.IsNullOrWhiteSpace(settings.DiscoverFourKVersion) ? null : settings.DiscoverFourKVersion,
            names,
            [.. sliders.Select(s => new DiscoverSliderSettings(s.Id, s.Kind, s.Title, s.Value, s.Enabled, s.IsBuiltIn))]);
    }

    private static async Task<Results<Ok<DiscoverSettingsResponse>, ProblemHttpResult>> UpdateSettingsAsync(
        UpdateDiscoverSettingsRequest request, BigarrDbContext db, CancellationToken ct)
    {
        var existing = await db.DiscoverSliders.ToDictionaryAsync(s => s.Id, ct);
        var seen = new HashSet<int>();
        var position = 0;
        foreach (var row in request.Sliders)
        {
            if (!Enum.IsDefined(row.Kind))
            {
                return Invalid("Unknown row kind.");
            }

            DiscoverSlider slider;
            if (row.Id is { } id)
            {
                if (!existing.TryGetValue(id, out slider!) || !seen.Add(id))
                {
                    return Invalid($"Unknown or repeated row {id}.");
                }

                if (slider.Kind != row.Kind)
                {
                    return Invalid("A row's kind cannot change.");
                }
            }
            else
            {
                if (DiscoverSlider.IsBuiltInKind(row.Kind))
                {
                    return Invalid("Built-in rows are already there: switch them on instead of adding them.");
                }

                slider = db.DiscoverSliders.Add(new DiscoverSlider { Kind = row.Kind }).Entity;
            }

            if (!slider.IsBuiltIn)
            {
                var title = row.Title?.Trim();
                var value = row.Value?.Trim();
                if (string.IsNullOrEmpty(title) || title.Length > 100)
                {
                    return Invalid("A custom row needs a title (100 characters at most).");
                }

                if (string.IsNullOrEmpty(value) || value.Length > 200
                    || (DiscoverSlider.TakesId(row.Kind) && !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                {
                    return Invalid(DiscoverSlider.TakesId(row.Kind) ? "This row needs a TMDB id." : "This row needs the words to search for.");
                }

                slider.Title = title;
                slider.Value = value;
            }

            slider.Enabled = row.Enabled;
            slider.Position = position++;
        }

        foreach (var (id, slider) in existing)
        {
            if (seen.Contains(id))
            {
                continue;
            }

            if (slider.IsBuiltIn)
            {
                return Invalid("Built-in rows cannot be removed, only switched off.");
            }

            db.DiscoverSliders.Remove(slider);
        }

        var fourK = request.FourKVersion?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(fourK) && !Slug.IsValid(fourK))
        {
            return Invalid($"'{fourK}' is not a valid version name.");
        }

        var settings = await UserSettings.EditAsync(db, ct);
        settings.DiscoverFourKVersion = string.IsNullOrEmpty(fourK) ? null : fourK;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ReadSettingsAsync(db, ct));
    }

    private static ProblemHttpResult Invalid(string title) => TypedResults.Problem(title: title, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// The TMDB ids a custom row can take: the genres, a keyword or a studio found by name, a network from the tiles' list
    /// or by its id (TMDB has no network search).
    /// </summary>
    private static async Task<Results<Ok<List<DiscoverLookupResult>>, ProblemHttpResult>> LookupAsync(
        DiscoverSliderKind kind, string? query, TmdbDiscoverClient tmdb, CancellationToken ct)
    {
        if (!tmdb.Configured)
        {
            return NotConfigured();
        }

        var words = query?.Trim() ?? string.Empty;
        if (kind is DiscoverSliderKind.MovieGenre or DiscoverSliderKind.SeriesGenre)
        {
            var genres = kind == DiscoverSliderKind.MovieGenre ? await tmdb.MovieGenresAsync(ct) : await tmdb.SeriesGenresAsync(ct);
            return TypedResults.Ok((genres?.Genres ?? [])
                .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Name.Contains(words, StringComparison.OrdinalIgnoreCase))
                .Select(g => new DiscoverLookupResult(g.Id, g.Name!))
                .ToList());
        }

        if (words.Length == 0)
        {
            return TypedResults.Ok(new List<DiscoverLookupResult>());
        }

        static List<DiscoverLookupResult> Of(IEnumerable<TmdbNamed> items) =>
            [.. items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Take(20).Select(i => new DiscoverLookupResult(i.Id, i.Name!))];

        var byId = int.TryParse(words, NumberStyles.None, CultureInfo.InvariantCulture, out var id);
        switch (kind)
        {
            case DiscoverSliderKind.MovieKeyword or DiscoverSliderKind.SeriesKeyword:
                return TypedResults.Ok(byId ? Of([.. new[] { await tmdb.KeywordAsync(id, ct) }.OfType<TmdbNamed>()]) : Of((await tmdb.SearchKeywordsAsync(words, ct))?.Results ?? []));
            case DiscoverSliderKind.Studio:
                return TypedResults.Ok(byId ? Of([.. new[] { await tmdb.CompanyAsync(id, ct) }.OfType<TmdbNamed>()]) : Of((await tmdb.SearchCompaniesAsync(words, ct))?.Results ?? []));
            case DiscoverSliderKind.Network:
                var known = DiscoverService.Networks.Where(n => n.Name.Contains(words, StringComparison.OrdinalIgnoreCase)).Select(n => new DiscoverLookupResult(n.Id, n.Name)).ToList();
                return TypedResults.Ok(byId ? Of([.. new[] { await tmdb.NetworkAsync(id, ct) }.OfType<TmdbNamed>()]) : known);
            default:
                return Invalid("Only genres, keywords, studios and networks are looked up.");
        }
    }
}
