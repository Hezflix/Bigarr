using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// The RSS pass's memory is lost on restart; in shadow mode what it saw is also written down, for good, so the
/// comparison can tell a release Bigarr never saw from one it saw and turned down (and why). One row per indexer and
/// guid, however many feeds list it and however many passes see it again: writing is an upsert, never a plain insert.
/// </summary>
public sealed class ReleaseSightings(BigarrDbContext db, IOptions<ShadowOptions> options, ILogger<ReleaseSightings> logger)
{
    private const int IndexerNameLength = 128;
    private const int GuidLength = 1024;
    private const int LookupChunk = 500;

    public bool Enabled => options.Value.Enabled;

    /// <param name="seen">Each release of the pass with its verdicts and the TMDB id of the film (or TVDB id of the series) it was matched to.</param>
    public async Task RecordAsync(IEnumerable<(RssReleaseResponse Entry, ReleaseInfo Release, int? TmdbId, int? TvdbId)> seen, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(seen);

        // The same release can come back several times in one pass (a film feed and a series feed listing it): one row
        // per key, keeping what each listing taught.
        var candidates = new Dictionary<(string IndexerName, string Guid), ReleaseSighting>();
        foreach (var (entry, release, tmdbId, tvdbId) in seen)
        {
            var sighting = Build(entry, release, tmdbId, tvdbId, now);
            var key = (sighting.IndexerName, sighting.Guid);
            if (candidates.TryGetValue(key, out var first))
            {
                Absorb(first, sighting);
            }
            else
            {
                candidates[key] = sighting;
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        // Another path may write the same release between the lookup and the save: the second try then finds it.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await UpsertAsync(candidates, ct);
                return;
            }
            catch (DbUpdateException ex)
            {
                Forget();
                if (attempt >= 2)
                {
                    // Sightings are bookkeeping: the grabs and picks of the pass already stand, so the pass goes on.
                    logger.LogError(ex, "Release sightings of the RSS pass could not be written");
                    return;
                }
            }
        }
    }

    private async Task UpsertAsync(Dictionary<(string IndexerName, string Guid), ReleaseSighting> candidates, CancellationToken ct)
    {
        // What the table holds already, found by the (truncated) key the rows are stored under.
        var existing = new Dictionary<(string IndexerName, string Guid), ExistingRow>();
        foreach (var chunk in candidates.Keys.Select(k => k.Guid).Distinct(StringComparer.Ordinal).Chunk(LookupChunk))
        {
            var rows = await db.ReleaseSightings.AsNoTracking()
                .Where(s => chunk.Contains(s.Guid))
                .Select(s => new ExistingRow(s.Id, s.IndexerName, s.Guid, s.TmdbId, s.TvdbId))
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                existing[(row.IndexerName, row.Guid)] = row;
            }
        }

        var toComplete = new Dictionary<long, ReleaseSighting>();
        foreach (var (key, sighting) in candidates)
        {
            if (!existing.TryGetValue(key, out var row))
            {
                db.ReleaseSightings.Add(sighting);
            }
            else if (Teaches(sighting, row.TmdbId, row.TvdbId))
            {
                toComplete[row.Id] = sighting;
            }
        }

        if (toComplete.Count > 0)
        {
            foreach (var chunk in toComplete.Keys.Chunk(LookupChunk))
            {
                foreach (var tracked in await db.ReleaseSightings.Where(s => chunk.Contains(s.Id)).ToListAsync(ct))
                {
                    Absorb(tracked, toComplete[tracked.Id]);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private void Forget()
    {
        foreach (var entry in db.ChangeTracker.Entries<ReleaseSighting>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static ReleaseSighting Build(RssReleaseResponse entry, ReleaseInfo release, int? tmdbId, int? tvdbId, DateTime now) => new()
    {
        IndexerId = entry.IndexerId > 0 ? entry.IndexerId : null,
        IndexerName = Truncate(entry.Indexer, IndexerNameLength)!,
        Guid = Truncate(entry.Guid, GuidLength)!,
        InfoHash = InfoHashes.Normalize(release.InfoHash),
        Title = Truncate(entry.Title, 1024)!,
        SeenAt = now,
        PublishDate = entry.PublishDate,
        TmdbId = tmdbId is > 0 ? tmdbId : null,
        TvdbId = tvdbId is > 0 ? tvdbId : null,
        Episodes = Truncate(entry.Episodes, 128),
        UnmatchedReason = Truncate(entry.UnmatchedReason, 512),
        AttributedVersion = entry.AttributedVersionName,
        Verdicts = entry.Versions.Select(v => new SightingVerdict(v.VersionName, v.Approved, v.Rejections.Select(r => r.Reason).ToList(), v.CustomFormatScore)).ToList(),
    };

    private static bool IsMatched(int? tmdbId, int? tvdbId) => tmdbId is not null || tvdbId is not null;

    /// <summary>
    /// A row is written down once, with the verdict of the first pass that saw it, and only completed after that: a
    /// later listing that matches a film or a series the row did not know (the TV feed carries a release the film feed
    /// left unmatched) adds that match. A listing that matches nothing never takes a match away.
    /// </summary>
    private static bool Teaches(ReleaseSighting seen, int? knownTmdbId, int? knownTvdbId) =>
        (seen.TmdbId is not null && knownTmdbId is null) || (seen.TvdbId is not null && knownTvdbId is null);

    private static void Absorb(ReleaseSighting target, ReleaseSighting seen)
    {
        if (!Teaches(seen, target.TmdbId, target.TvdbId))
        {
            return;
        }

        if (!IsMatched(target.TmdbId, target.TvdbId))
        {
            // The row was a release nothing matched: the match, its episodes and its verdicts replace that.
            target.TmdbId = seen.TmdbId;
            target.TvdbId = seen.TvdbId;
            target.Episodes = seen.Episodes;
            target.UnmatchedReason = seen.UnmatchedReason;
            target.AttributedVersion = seen.AttributedVersion;
            target.Verdicts = seen.Verdicts;
            return;
        }

        // Matched as a film and as a series: both outcomes stay on the row.
        target.TmdbId ??= seen.TmdbId;
        target.TvdbId ??= seen.TvdbId;
        target.Episodes ??= seen.Episodes;
        target.AttributedVersion ??= seen.AttributedVersion;
        target.Verdicts = target.Verdicts.Concat(seen.Verdicts).ToList();
    }

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];

    private sealed record ExistingRow(long Id, string IndexerName, string Guid, int? TmdbId, int? TvdbId);
}
