using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Search;

/// <param name="Episodes"><c>S01E05</c>, <c>S01E05-E06</c>: TVDB numbers.</param>
public sealed record HeldReleaseResponse(
    int Id,
    int SeriesId,
    string SeriesTitle,
    int? SeriesYear,
    string? PosterUrl,
    int VersionId,
    string VersionName,
    List<int> EpisodeIds,
    string Episodes,
    string Title,
    string? Quality,
    int CustomFormatScore,
    long Size,
    int? Seeders,
    string? Indexer,
    DateTimeOffset? PublishDate,
    string Reason,
    string Source,
    DateTime HeldAt,
    DateTime LastSeenAt,
    DateTime? DismissedAt);

/// <summary>
/// The "held for review" list (S6's held releases surfaced): what RSS or a search would have taken for a series version
/// but matched on the series' order only, confirmed by nothing in the release. The user grabs one (the version's own
/// grab path: a pick in shadow mode) or dismisses it; a dismissed release is never held again.
/// </summary>
public sealed class HeldReleaseService(BigarrDbContext db, GrabService grabs, TimeProvider time, ILogger<HeldReleaseService> logger)
{
    public const string GrabTrigger = "held";

    /// <summary>The release would pass for <paramref name="versionId"/> but for the hold: the only rejection the version gives it.</summary>
    public static bool OnlyHeld(SeriesJudgement judgement, int versionId)
    {
        ArgumentNullException.ThrowIfNull(judgement);
        return judgement.HeldReason is not null && judgement.Decisions.Any(d =>
            d.Target.Target is EpisodeTarget target && target.VersionId == versionId
            && d.Rejections.Count == 1 && d.Rejections[0].Reason == judgement.HeldReason);
    }

    /// <summary>The versions a held release would go to, default first.</summary>
    public static IEnumerable<int> HeldFor(SeriesJudgement judgement)
    {
        ArgumentNullException.ThrowIfNull(judgement);
        return judgement.HeldReason is null
            ? []
            : judgement.Decisions.Where(d => d.Rejections.Count == 1 && d.Rejections[0].Reason == judgement.HeldReason)
                .Select(d => ((EpisodeTarget)d.Target.Target).VersionId)
                .Distinct();
    }

    /// <summary>Adds what is new, refreshes what was already held, leaves the dismissed alone. Saves.</summary>
    public async Task<int> RecordAsync(int versionId, int seriesId, IReadOnlyCollection<(ReleaseInfo Release, SeriesJudgement Judgement)> releases, string source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(releases);
        var now = time.GetUtcNow().UtcDateTime;
        var guids = releases.Select(r => r.Release.Guid).Distinct().ToList();
        var existing = await db.HeldReleases.Where(h => h.VersionId == versionId && guids.Contains(h.Guid)).ToDictionaryAsync(h => h.Guid, StringComparer.Ordinal, ct);
        var added = 0;
        foreach (var (release, judgement) in releases.DistinctBy(r => r.Release.Guid))
        {
            if (existing.TryGetValue(release.Guid, out var row))
            {
                row.LastSeenAt = now;
                continue;
            }

            var decision = judgement.Decisions.FirstOrDefault(d => d.Target.Target is EpisodeTarget t && t.VersionId == versionId);
            db.HeldReleases.Add(new HeldRelease
            {
                VersionId = versionId,
                SeriesId = seriesId,
                EpisodeIds = judgement.Episodes.Select(e => e.Id).ToList(),
                Episodes = Label(judgement.Episodes),
                Title = release.Title,
                Guid = release.Guid,
                IndexerId = release.IndexerId,
                IndexerName = release.IndexerName,
                DownloadUrl = release.DownloadUrl,
                MagnetUrl = release.MagnetUrl,
                InfoHash = release.InfoHash,
                Size = release.Size,
                Seeders = release.Seeders,
                Flags = release.Flags,
                Protocol = release.Protocol,
                PublishDate = release.PublishDate,
                Quality = judgement.Parsed?.Quality?.Quality?.Name,
                CustomFormatScore = decision?.CustomFormatScore ?? 0,
                Numbering = judgement.Numbering,
                Reason = judgement.HeldReason ?? SeriesReleaseJudge.HeldMessage,
                Source = source,
                HeldAt = now,
                LastSeenAt = now,
            });
            added++;
        }

        await db.SaveChangesAsync(ct);
        if (added > 0)
        {
            logger.LogInformation("{Count} release(s) held for review for series version {Version} ({Source})", added, versionId, source);
        }

        return added;
    }

    /// <summary><c>S01E05</c>, or <c>S01E05-E06</c> for consecutive episodes of one season, else a comma list.</summary>
    public static string Label(IReadOnlyCollection<Episode> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        var ordered = episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
        if (ordered.Count == 0)
        {
            return string.Empty;
        }

        if (ordered.Count > 1 && ordered.All(e => e.SeasonNumber == ordered[0].SeasonNumber))
        {
            return $"{GrabService.EpisodeLabel(ordered[0])}-E{ordered[^1].EpisodeNumber:00}";
        }

        return string.Join(", ", ordered.Select(GrabService.EpisodeLabel));
    }

    public async Task<List<HeldReleaseResponse>> ListAsync(int? seriesId, bool includeDismissed, CancellationToken ct)
    {
        var query = db.HeldReleases.AsNoTracking();
        if (seriesId is { } id)
        {
            query = query.Where(h => h.SeriesId == id);
        }

        if (!includeDismissed)
        {
            query = query.Where(h => h.DismissedAt == null);
        }

        var rows = await query.OrderByDescending(h => h.HeldAt).ThenByDescending(h => h.Id)
            .Select(h => new { Held = h, h.Version.Name, h.Version.Series.Title, h.Version.Series.Year, h.Version.Series.Images })
            .ToListAsync(ct);
        return rows.Select(r => new HeldReleaseResponse(
            r.Held.Id,
            r.Held.SeriesId,
            r.Title,
            r.Year,
            r.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            r.Held.VersionId,
            r.Name,
            r.Held.EpisodeIds,
            r.Held.Episodes,
            r.Held.Title,
            r.Held.Quality,
            r.Held.CustomFormatScore,
            r.Held.Size,
            r.Held.Seeders,
            r.Held.IndexerName,
            r.Held.PublishDate,
            r.Held.Reason,
            r.Held.Source,
            r.Held.HeldAt,
            r.Held.LastSeenAt,
            r.Held.DismissedAt)).ToList();
    }

    public Task<int> CountAsync(CancellationToken ct) => db.HeldReleases.CountAsync(h => h.DismissedAt == null, ct);

    /// <summary>The user decided: the release goes through the version's grab path (a pick in shadow mode) and leaves the list.</summary>
    public async Task<Result<GrabResponse>> GrabAsync(int id, CancellationToken ct)
    {
        var row = await db.HeldReleases.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (row is null)
        {
            return Failure.NotFound("Held release");
        }

        var request = new GrabRequest(
            row.VersionId,
            row.IndexerId ?? 0,
            row.Guid,
            row.Title,
            row.DownloadUrl,
            row.MagnetUrl,
            row.InfoHash,
            row.Size,
            row.Seeders,
            row.Flags,
            row.Protocol,
            row.IndexerName,
            row.PublishDate,
            row.EpisodeIds,
            row.Numbering);
        var grab = await grabs.GrabAsync(request, GrabTrigger, ct);
        if (grab.IsSuccess)
        {
            db.ChangeTracker.Clear();
            await db.HeldReleases.Where(h => h.Id == id).ExecuteDeleteAsync(ct);
        }

        return grab;
    }

    public async Task<Result<bool>> DismissAsync(int id, CancellationToken ct)
    {
        var updated = await db.HeldReleases.Where(h => h.Id == id && h.DismissedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.DismissedAt, time.GetUtcNow().UtcDateTime), ct);
        return updated > 0 || await db.HeldReleases.AnyAsync(h => h.Id == id, ct) ? Result<bool>.Ok(true) : Failure.NotFound("Held release");
    }
}

public static class HeldReleaseEndpoints
{
    public static IEndpointRouteBuilder MapHeldReleases(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/series/held").WithTags("Series").RequireAuthorization(Infrastructure.Auth.AuthPolicies.Api);
        group.MapGet("/", async (HeldReleaseService held, CancellationToken ct, int? seriesId = null, bool includeDismissed = false) =>
            TypedResults.Ok(await held.ListAsync(seriesId, includeDismissed, ct)));
        group.MapPost("/{id:int}/grab", async Task<Microsoft.AspNetCore.Http.HttpResults.Results<Microsoft.AspNetCore.Http.HttpResults.Ok<GrabResponse>, Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>> (int id, HeldReleaseService held, CancellationToken ct) =>
        {
            var result = await held.GrabAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });
        group.MapPost("/{id:int}/dismiss", async Task<Microsoft.AspNetCore.Http.HttpResults.Results<Microsoft.AspNetCore.Http.HttpResults.NoContent, Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>> (int id, HeldReleaseService held, CancellationToken ct) =>
        {
            var result = await held.DismissAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });
        return app;
    }
}
