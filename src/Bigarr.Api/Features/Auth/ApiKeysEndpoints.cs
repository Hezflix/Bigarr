using System.Security.Claims;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Auth;

public static class ApiKeysEndpoints
{
    public const int MaxNameLength = 128;

    public static IEndpointRouteBuilder MapApiKeys(this IEndpointRouteBuilder app)
    {
        // Managing keys requires the human session: a key must never be able to mint another key.
        var group = app.MapGroup("/api/v1/apikeys").WithTags("ApiKeys").RequireAuthorization(AuthPolicies.Session);

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapDelete("/{id:int}", RevokeAsync);

        return app;
    }

    private static async Task<Ok<List<ApiKeyResponse>>> ListAsync(ClaimsPrincipal user, BigarrDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.UserId();
        var keys = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderBy(k => k.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(keys
            .Select(k => new ApiKeyResponse(k.Id, k.Name, k.KeyPrefix, k.CompatView.ToString(), k.CreatedAt, k.LastUsedAt, k.RevokedAt))
            .ToList());
    }

    private static async Task<Results<Created<ApiKeyCreatedResponse>, ValidationProblem>> CreateAsync(
        CreateApiKeyRequest request,
        ClaimsPrincipal user,
        BigarrDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
        {
            errors["name"] = [$"Name is required (max {MaxNameLength} characters)."];
        }

        if (!CompatView.TryParse(request.CompatView, out var view))
        {
            errors["compatView"] = ["Expected 'default', 'all' or 'version:<slug>'."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var rawKey = ApiKeyHasher.Generate();
        var key = new ApiKey
        {
            UserId = user.UserId(),
            Name = request.Name.Trim(),
            KeyHash = ApiKeyHasher.Hash(rawKey),
            KeyPrefix = ApiKeyHasher.DisplayPrefix(rawKey),
            CompatView = view,
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/apikeys/{key.Id}",
            new ApiKeyCreatedResponse(key.Id, key.Name, rawKey, key.KeyPrefix, view.ToString(), key.CreatedAt));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        int id,
        ClaimsPrincipal user,
        BigarrDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var userId = user.UserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, cancellationToken);
        if (key is null)
        {
            return TypedResults.NotFound();
        }

        if (!key.IsRevoked)
        {
            key.RevokedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }
}
