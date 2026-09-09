using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Quality;

public sealed record QualityProfileSummary(int Id, string Name, int Cutoff, bool UpgradeAllowed, bool IsSystem, string PreferredLanguage);

public sealed record QualityDefinitionResponse(int Id, string Name, string? Title, string Source, int Resolution, string Modifier, double? MinSize, double? PreferredSize, double? MaxSize);

/// <summary>Read side only for now; profile and custom format editing arrive with M3.</summary>
public static class QualityEndpoints
{
    public static IEndpointRouteBuilder MapQuality(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Quality").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/qualityprofiles", async Task<Ok<List<QualityProfileSummary>>> (BigarrDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.QualityProfiles.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new QualityProfileSummary(p.Id, p.Name, p.Cutoff, p.UpgradeAllowed, p.IsSystem, p.PreferredLanguage))
                .ToListAsync(ct)));

        group.MapGet("/qualitydefinitions", async Task<Ok<List<QualityDefinitionResponse>>> (BigarrDbContext db, CancellationToken ct) =>
            TypedResults.Ok((await db.QualityDefinitions.AsNoTracking().ToListAsync(ct))
                .OrderBy(q => q.Resolution).ThenBy(q => q.Source)
                .Select(q => new QualityDefinitionResponse(q.Id, q.Name, q.Title, q.Source.ToString(), q.Resolution, q.Modifier.ToString(), q.MinSize, q.PreferredSize, q.MaxSize))
                .ToList()));

        return app;
    }
}
