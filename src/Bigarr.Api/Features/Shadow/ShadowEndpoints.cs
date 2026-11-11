using System.Text.Json;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Shadow;

/// <summary>Shadow mode: its status, the comparison with Radarr and Sonarr (Activity → Compare), the Radarrs and Sonarrs it follows (Settings).</summary>
public static class ShadowEndpoints
{
    public static IEndpointRouteBuilder MapShadow(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shadow").WithTags("Shadow").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<ShadowStatusResponse>> (FollowedRadarrService service, FollowedSonarrService sonarrs, CancellationToken ct) =>
            TypedResults.Ok(await service.StatusAsync(ct) with { Sonarrs = await sonarrs.ListAsync(ct) }));

        // Activity → Compare, and the film page with tmdbId. outcome is matched by name (same | different | bigarrOnly |
        // radarrOnly); days=0 means all time.
        group.MapGet("/compare", async Task<Results<Ok<CompareResponse>, ValidationProblem>> (
            string? version, string? outcome, int? days, int? windowHours, int? page, int? pageSize, int? tmdbId, CompareService compare, CancellationToken ct) =>
        {
            CompareOutcome? kind = null;
            if (!string.IsNullOrWhiteSpace(outcome))
            {
                if (!Enum.TryParse<CompareOutcome>(outcome, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["outcome"] = [$"Unknown outcome '{outcome}'."] });
                }

                kind = parsed;
            }

            return TypedResults.Ok(await compare.CompareAsync(version, kind, days ?? 30, windowHours ?? CompareService.DefaultWindowHours, page ?? 1, pageSize ?? 50, ct, tmdbId));
        });

        // Activity → Compare, series: rows per series, version and episode set; the series page with tvdbId. outcome is
        // matched by name (same | different | bigarrOnly | sonarrOnly); days=0 means all time.
        group.MapGet("/compare/series", async Task<Results<Ok<SeriesCompareResponse>, ValidationProblem>> (
            string? version, string? outcome, int? days, int? windowHours, int? page, int? pageSize, int? tvdbId, SeriesCompareService compare, CancellationToken ct) =>
        {
            SeriesCompareOutcome? kind = null;
            if (!string.IsNullOrWhiteSpace(outcome))
            {
                if (!Enum.TryParse<SeriesCompareOutcome>(outcome, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["outcome"] = [$"Unknown outcome '{outcome}'."] });
                }

                kind = parsed;
            }

            return TypedResults.Ok(await compare.CompareAsync(version, kind, days ?? 30, windowHours ?? CompareService.DefaultWindowHours, page ?? 1, pageSize ?? 50, ct, tvdbId));
        });

        MapSonarrs(group.MapGroup("/sonarrs"));

        var radarrs = group.MapGroup("/radarrs");

        radarrs.MapGet("/", async Task<Ok<List<FollowedRadarrResponse>>> (FollowedRadarrService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)));

        radarrs.MapGet("/{id:int}", async Task<Results<Ok<FollowedRadarrResponse>, NotFound>> (int id, FollowedRadarrService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } found ? TypedResults.Ok(found) : TypedResults.NotFound());

        radarrs.MapPost("/", async Task<Results<Created<FollowedRadarrResponse>, ProblemHttpResult>> (FollowedRadarrRequest request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/shadow/radarrs/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        radarrs.MapPut("/{id:int}", async Task<Results<Ok<FollowedRadarrResponse>, ProblemHttpResult>> (int id, FollowedRadarrRequest request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        radarrs.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        // An unsaved form; a saved Radarr with its stored key (the form may change the URL, a masked key keeps the stored one).
        radarrs.MapPost("/test", async Task<Results<Ok<FollowedRadarrTestResponse>, ProblemHttpResult>> (FollowedRadarrRequest request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        radarrs.MapPost("/{id:int}/test", async Task<Results<Ok<FollowedRadarrTestResponse>, ProblemHttpResult>> (int id, FollowedRadarrRequest? request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.TestAsync(request, id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // The import's mapping screen data for a saved Radarr (its key never comes back to the browser).
        radarrs.MapPost("/{id:int}/preview", async Task<Results<Ok<RadarrImportPreviewResponse>, ProblemHttpResult>> (int id, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.PreviewAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // Radarr → Settings → Connect → Webhook, with a Bigarr API key in the URL (?apikey=): any event but Test wakes a
        // pass within seconds. Only eventType is read from Radarr's body; a body that is not JSON is no reason to refuse.
        radarrs.MapPost("/{id:int}/webhook", async Task<Results<Accepted, ProblemHttpResult>> (int id, HttpRequest request, FollowedRadarrService service, CancellationToken ct) =>
        {
            string? eventType = null;
            try
            {
                using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                if (body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("eventType", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    eventType = value.GetString();
                }
            }
            catch (JsonException)
            {
                // an empty or foreign body still wakes the pass
            }

            var result = await service.WebhookAsync(id, eventType, ct);
            return result.IsSuccess ? TypedResults.Accepted((string?)null) : result.Failure!.ToProblem();
        });

        radarrs.MapPost("/{id:int}/sync", async Task<Results<Ok<RadarrFollowPass>, ProblemHttpResult>> (int id, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.SyncAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }

    /// <summary>Settings → Followed Sonarrs: the same routes as the Radarr ones.</summary>
    private static void MapSonarrs(RouteGroupBuilder sonarrs)
    {
        sonarrs.MapGet("/", async Task<Ok<List<FollowedSonarrResponse>>> (FollowedSonarrService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)));

        sonarrs.MapGet("/{id:int}", async Task<Results<Ok<FollowedSonarrResponse>, NotFound>> (int id, FollowedSonarrService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } found ? TypedResults.Ok(found) : TypedResults.NotFound());

        sonarrs.MapPost("/", async Task<Results<Created<FollowedSonarrResponse>, ProblemHttpResult>> (FollowedSonarrRequest request, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/shadow/sonarrs/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        sonarrs.MapPut("/{id:int}", async Task<Results<Ok<FollowedSonarrResponse>, ProblemHttpResult>> (int id, FollowedSonarrRequest request, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        sonarrs.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        sonarrs.MapPost("/test", async Task<Results<Ok<FollowedSonarrTestResponse>, ProblemHttpResult>> (FollowedSonarrRequest request, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        sonarrs.MapPost("/{id:int}/test", async Task<Results<Ok<FollowedSonarrTestResponse>, ProblemHttpResult>> (int id, FollowedSonarrRequest? request, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.TestAsync(request, id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        sonarrs.MapPost("/{id:int}/preview", async Task<Results<Ok<SonarrImportPreviewResponse>, ProblemHttpResult>> (int id, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.PreviewAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // Sonarr → Settings → Connect → Webhook, with a Bigarr API key in the URL (?apikey=): any event but Test wakes a
        // pass within seconds. Only eventType is read from Sonarr's body.
        sonarrs.MapPost("/{id:int}/webhook", async Task<Results<Accepted, ProblemHttpResult>> (int id, HttpRequest request, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.WebhookAsync(id, await EventTypeAsync(request, ct), ct);
            return result.IsSuccess ? TypedResults.Accepted((string?)null) : result.Failure!.ToProblem();
        });

        sonarrs.MapPost("/{id:int}/sync", async Task<Results<Ok<SonarrFollowPass>, ProblemHttpResult>> (int id, FollowedSonarrService service, CancellationToken ct) =>
        {
            var result = await service.SyncAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });
    }

    /// <summary>The <c>eventType</c> of an *arr webhook body; an empty or foreign body still wakes the pass.</summary>
    private static async Task<string?> EventTypeAsync(HttpRequest request, CancellationToken ct)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            return body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("eventType", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
