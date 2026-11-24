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
        group.MapPut("/{id:int}", UpdateAsync);
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
            .Select(ToResponse)
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

        if (!TryParseScope(request.Scope, out var scope))
        {
            errors["scope"] = [ScopeMessage];
        }
        else if (scope == ApiKeyScope.Calendar && view != CompatView.Default && !errors.ContainsKey("compatView"))
        {
            errors["compatView"] = [CalendarKeyHasNoView];
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
            Scope = scope,
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/apikeys/{key.Id}",
            new ApiKeyCreatedResponse(key.Id, key.Name, rawKey, key.KeyPrefix, view.ToString(), ScopeName(scope), key.CreatedAt));
    }

    /// <summary>
    /// Changes what a key may reach, so a script's key can be made <c>full</c> without replacing it in the script. Only
    /// the session reaches this route (the group's policy): a key can never widen its own scope.
    /// </summary>
    private static async Task<Results<Ok<ApiKeyResponse>, NotFound, ValidationProblem>> UpdateAsync(
        int id,
        UpdateApiKeyRequest request,
        ClaimsPrincipal user,
        BigarrDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = user.UserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId && k.RevokedAt == null, cancellationToken);
        if (key is null)
        {
            return TypedResults.NotFound();
        }

        if (!TryParseScope(request.Scope, out var scope) || string.IsNullOrWhiteSpace(request.Scope))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["scope"] = [ScopeMessage] });
        }

        if (scope == ApiKeyScope.Calendar && key.CompatView != CompatView.Default)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["scope"] = [CalendarKeyHasNoView] });
        }

        key.Scope = scope;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(key));
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

    private const string ScopeMessage = "Expected 'compat', 'full' or 'calendar'.";

    private const string CalendarKeyHasNoView = "A calendar key has no view.";

    /// <summary>Blank means <see cref="ApiKeyScope.Compat"/>; a number, which <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> would take, is refused.</summary>
    private static bool TryParseScope(string? value, out ApiKeyScope scope)
    {
        scope = ApiKeyScope.Compat;
        return string.IsNullOrWhiteSpace(value)
            || (Enum.TryParse(value.Trim(), ignoreCase: true, out scope) && Enum.IsDefined(scope) && !char.IsDigit(value.Trim()[0]));
    }

    private static string ScopeName(ApiKeyScope scope) => scope.ToString().ToLowerInvariant();

    private static ApiKeyResponse ToResponse(ApiKey k) =>
        new(k.Id, k.Name, k.KeyPrefix, k.CompatView.ToString(), ScopeName(k.Scope), k.CreatedAt, k.LastUsedAt, k.RevokedAt);
}
