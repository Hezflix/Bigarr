using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Search;

public static class SearchEndpoints
{
    public const int MaxQueryLength = 200;

    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/search").WithTags("Search").RequireAuthorization(AuthPolicies.Api);

        // Interactive search: one round of indexer queries for the film, judged by every version (or one). A query
        // replaces the film's ids and titles with the user's own text (a film listed under another name or spelling).
        group.MapGet("/", async Task<Results<Ok<SearchResponse>, ValidationProblem, ProblemHttpResult>> (
            int? movieId, int? versionId, string? query, InteractiveSearchService search, CancellationToken ct) =>
        {
            if (movieId is null or <= 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["movieId"] = ["A movie id is required."] });
            }

            if (query?.Trim().Length > MaxQueryLength)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["query"] = [$"The search text is limited to {MaxQueryLength} characters."] });
            }

            var result = await search.SearchAsync(movieId.Value, versionId, query, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // Interactive search for a series: an episode (episodeId), a season (season) or the whole series (neither), judged
        // by every version of the series (or one). A query replaces the show's ids and titles with the user's own text.
        app.MapGet("/api/v1/series/{id:int}/search", async Task<Results<Ok<SeriesSearchResponse>, ValidationProblem, ProblemHttpResult>> (
                int id, int? episodeId, int? season, int? versionId, string? query, SeriesSearchService search, CancellationToken ct, bool orderSeason = false) =>
            {
                if (query?.Trim().Length > MaxQueryLength)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["query"] = [$"The search text is limited to {MaxQueryLength} characters."] });
                }

                if (episodeId is not null && season is not null)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["season"] = ["Search an episode or a season, not both."] });
                }

                var result = await search.SearchAsync(id, episodeId, season, versionId, query, ct, orderSeason);
                return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
            })
            .WithTags("Search")
            .RequireAuthorization(AuthPolicies.Api);

        // Automatic search asked by the user: the best approved release is grabbed, one per version, and the outcome comes back.
        app.MapPost("/api/v1/movies/{id:int}/search", async Task<Results<Ok<List<AutomaticSearchResult>>, NotFound>> (
                int id, int? versionId, MovieQueries movies, AutomaticSearchService search, CancellationToken ct) =>
            {
                var versionIds = await movies.VersionIdsAsync(id, versionId, ct);
                if (versionIds is null)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(await search.SearchAsync(versionIds, SearchTrigger.User, ct));
            })
            .WithTags("Search")
            .RequireAuthorization(AuthPolicies.Api);

        var wanted = app.MapGroup("/api/v1/wanted").WithTags("Search").RequireAuthorization(AuthPolicies.Api);

        // filter is matched by name, case-insensitive (missing | cutoffUnmet); absent means both.
        wanted.MapGet("/", async Task<Results<Ok<WantedPageResponse>, ValidationProblem>> (string? filter, int? page, int? pageSize, WantedService service, CancellationToken ct) =>
        {
            if (!TryParseFilter(filter, out var kind))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["filter"] = [$"Unknown filter '{filter}'."] });
            }

            return TypedResults.Ok(await service.PageAsync(kind, page ?? 1, pageSize ?? 50, ct));
        });

        // Queues an automatic search for everything on the list, backoff ignored: the user asked. Runs in the background, one version at a time.
        wanted.MapPost("/search", async Task<Results<Accepted<WantedSearchResponse>, ValidationProblem>> (string? filter, WantedService service, SearchQueue queue, CancellationToken ct) =>
        {
            if (!TryParseFilter(filter, out var kind))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["filter"] = [$"Unknown filter '{filter}'."] });
            }

            var versionIds = await service.VersionIdsAsync(kind, ct);
            var queued = versionIds.Count(id => queue.TryEnqueue(id, SearchTrigger.User));
            return TypedResults.Accepted("/api/v1/wanted", new WantedSearchResponse(queued));
        });

        return app;
    }

    private static bool TryParseFilter(string? filter, out WantedFilter? kind)
    {
        kind = null;
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        if (Enum.TryParse<WantedFilter>(filter, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            kind = parsed;
            return true;
        }

        return false;
    }
}

/// <summary>Small read helper so the search endpoints do not depend on the Movies slice's service.</summary>
public sealed class MovieQueries(Infrastructure.Persistence.BigarrDbContext db)
{
    /// <summary>Every version of the film, or the one asked; null when the film (or that version) does not exist.</summary>
    public async Task<List<int>?> VersionIdsAsync(int movieId, int? versionId, CancellationToken ct)
    {
        var ids = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.MovieVersions.Where(v => v.MovieId == movieId).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).Select(v => v.Id), ct);
        if (ids.Count == 0)
        {
            return null;
        }

        if (versionId is not null)
        {
            return ids.Contains(versionId.Value) ? [versionId.Value] : null;
        }

        return ids;
    }
}
