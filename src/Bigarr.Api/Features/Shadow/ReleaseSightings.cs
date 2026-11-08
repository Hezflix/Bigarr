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
/// comparison can tell a release Bigarr never saw from one it saw and turned down (and why).
/// </summary>
public sealed class ReleaseSightings(BigarrDbContext db, IOptions<ShadowOptions> options)
{
    public bool Enabled => options.Value.Enabled;

    /// <param name="seen">Each release of the pass with its verdicts and the TMDB id of the film it was matched to.</param>
    public async Task RecordAsync(IEnumerable<(RssReleaseResponse Entry, ReleaseInfo Release, int? TmdbId)> seen, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(seen);

        var batch = seen.ToList();
        var guids = batch.Select(s => s.Entry.Guid).Distinct().ToList();
        var known = (await db.ReleaseSightings.AsNoTracking().Where(s => guids.Contains(s.Guid)).Select(s => new { s.IndexerName, s.Guid }).ToListAsync(ct))
            .Select(s => (s.IndexerName, s.Guid))
            .ToHashSet();

        // Every pass judges the whole feed again: a release is written down once, with the verdict of the first pass that saw it.
        foreach (var (entry, release, tmdbId) in batch)
        {
            if (!known.Add((entry.Indexer, entry.Guid)))
            {
                continue;
            }

            db.ReleaseSightings.Add(new ReleaseSighting
            {
                IndexerId = entry.IndexerId > 0 ? entry.IndexerId : null,
                IndexerName = Truncate(entry.Indexer, 128)!,
                Guid = Truncate(entry.Guid, 1024)!,
                InfoHash = InfoHashes.Normalize(release.InfoHash),
                Title = Truncate(entry.Title, 1024)!,
                SeenAt = now,
                PublishDate = entry.PublishDate,
                TmdbId = tmdbId is > 0 ? tmdbId : null,
                UnmatchedReason = Truncate(entry.UnmatchedReason, 512),
                AttributedVersion = entry.AttributedVersionName,
                Verdicts = entry.Versions.Select(v => new SightingVerdict(v.VersionName, v.Approved, v.Rejections.Select(r => r.Reason).ToList(), v.CustomFormatScore)).ToList(),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
