using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Where a grab ends in shadow mode: the release the grab path chose is written down with what Bigarr made of it
/// (quality, group, languages, custom formats scored against the version's profile), and nothing else happens. The
/// version's state is left alone, so the next pass judges it exactly as before.
/// </summary>
public sealed class ShadowPickRecorder(BigarrDbContext db, CustomFormatScorer scorer, TimeProvider time, ILogger<ShadowPickRecorder> logger)
{
    /// <param name="version">Loaded with its film and its profile's format scores, as <see cref="GrabService"/> loads it.</param>
    public async Task<ShadowPick> RecordAsync(MovieVersion version, GrabRequest request, string trigger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(request);

        var now = time.GetUtcNow().UtcDateTime;
        var guid = string.IsNullOrWhiteSpace(request.Guid) ? request.Title : request.Guid.Trim();
        var existing = await db.ShadowPicks.FirstOrDefaultAsync(p => p.VersionId == version.Id && p.ReleaseGuid == guid, ct);
        if (existing is not null)
        {
            existing.LastPickedAt = now;
            existing.Picks++;
            await db.SaveChangesAsync(ct);
            logger.LogDebug("Shadow mode: {Title} picked again for {Movie} [{Version}] ({Trigger})", request.Title, version.Movie.Title, version.Name, trigger);
            return existing;
        }

        // Only an indexer that still exists is linked; the name stays either way.
        var indexer = request.IndexerId > 0
            ? await db.Indexers.AsNoTracking().Where(i => i.Id == request.IndexerId).Select(i => new { i.Id, i.Name }).FirstOrDefaultAsync(ct)
            : null;
        var indexerName = indexer?.Name ?? request.Indexer;
        var release = EventSnapshots.Release(request.Title, indexerName, request.Size, request.Flags, version.Movie, version.QualityProfile, await scorer.CalculatorAsync(ct), request.Seeders, request.InfoHash);
        var parsedQuality = ReleaseMatcher.TryParse(request.Title)?.Quality?.Quality;

        var pick = new ShadowPick
        {
            VersionId = version.Id,
            MovieId = version.MovieId,
            TmdbId = version.Movie.TmdbId,
            MovieTitle = version.Movie.Title,
            VersionName = version.Name,
            Trigger = trigger,
            PickedAt = now,
            LastPickedAt = now,
            ReleaseTitle = request.Title,
            ReleaseGuid = guid,
            InfoHash = InfoHashes.Normalize(request.InfoHash),
            IndexerId = indexer?.Id,
            IndexerName = indexerName,
            Size = request.Size,
            Seeders = request.Seeders,
            IndexerFlags = request.Flags,
            PublishDate = request.PublishDate,
            Quality = release.Quality,
            QualityId = parsedQuality is null || release.Quality is null ? null : parsedQuality.Id,
            RevisionVersion = release.QualityVersion,
            ReleaseGroup = release.ReleaseGroup,
            Languages = release.Languages ?? [],
            CustomFormats = release.CustomFormats ?? [],
            CustomFormatScore = release.CustomFormatScore ?? 0,
        };
        db.ShadowPicks.Add(pick);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another path picked the same release for the same version in between: that row stands.
            db.Entry(pick).State = EntityState.Detached;
            var raced = await db.ShadowPicks.FirstOrDefaultAsync(p => p.VersionId == version.Id && p.ReleaseGuid == guid, ct);
            if (raced is null)
            {
                throw;
            }

            return raced;
        }

        logger.LogInformation("Shadow mode: {Title} picked for {Movie} [{Version}] ({Trigger}); nothing sent to a download client", request.Title, version.Movie.Title, version.Name, trigger);
        return pick;
    }
}
