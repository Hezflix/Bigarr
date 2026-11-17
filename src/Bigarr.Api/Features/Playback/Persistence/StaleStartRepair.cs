using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback.Persistence;

/// <summary>
/// Repairs the live plays recorded before the tracker ignored a stale opening offset. Such a stream starts past its
/// own end: Plex reported an earlier view's resume point (or another item's position) and the player jumped back. Its
/// real start becomes its end minus its played time, the play's furthest point and watched flag are worked out again,
/// and a play the stale offset split from the one it continues goes back into it. Only live plays: imported and
/// server-history plays are never read or written. Runs once, queued by its migration.
/// </summary>
public sealed class StaleStartRepair(BigarrDbContext db, ILogger<StaleStartRepair> logger) : IStartupTask
{
    public const string Name = "playback-stale-start";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pending = await db.PendingRepairs.FindAsync([Name], cancellationToken);
        if (pending is null)
        {
            return;
        }

        var (streams, merged) = await RepairAsync(cancellationToken);
        db.PendingRepairs.Remove(pending);
        await db.SaveChangesAsync(cancellationToken);
        if (streams > 0)
        {
            logger.LogInformation("Repaired {Streams} played stream(s) that opened on a stale position; {Merged} split play(s) joined back", streams, merged);
        }
    }

    /// <summary>Applies the repair to the tracked rows, unsaved. Returns the streams repaired and the plays merged away.</summary>
    public async Task<(int Streams, int Merged)> RepairAsync(CancellationToken ct)
    {
        var tolerance = (long)PlayRules.StaleStartTolerance.TotalMilliseconds;
        var stalePlayIds = await db.PlayStreams
            .Where(s => s.Play.Source == PlaySource.Live && s.StartOffsetMs > s.EndOffsetMs + tolerance)
            .Select(s => s.PlayId)
            .Distinct()
            .ToListAsync(ct);
        if (stalePlayIds.Count == 0)
        {
            return (0, 0);
        }

        var groups = await db.Plays.Where(p => stalePlayIds.Contains(p.Id)).Select(p => new { p.UserId, p.ItemId }).Distinct().ToListAsync(ct);
        var userIds = groups.Select(g => g.UserId).Distinct().ToList();
        var itemIds = groups.Select(g => g.ItemId).Distinct().ToList();
        var plays = (await db.Plays.Include(p => p.Streams).Include(p => p.Item)
                .Where(p => p.Source == PlaySource.Live && userIds.Contains(p.UserId) && itemIds.Contains(p.ItemId))
                .ToListAsync(ct))
            .Where(p => groups.Any(g => g.UserId == p.UserId && g.ItemId == p.ItemId))
            .ToList();

        var rules = new PlayRules(await db.PlaybackSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlaybackSettings.SingletonId, ct) ?? PlaybackSettings.Default());
        var changed = new HashSet<Play>();
        var repaired = 0;
        foreach (var play in plays.Where(p => stalePlayIds.Contains(p.Id)))
        {
            var stale = play.Streams.Where(s => s.StartOffsetMs > s.EndOffsetMs + tolerance).ToList();
            var highestStale = stale.Max(s => s.StartOffsetMs);
            foreach (var stream in stale)
            {
                var playedMs = Math.Max(0, (long)(stream.StoppedAt - stream.StartedAt).TotalMilliseconds - stream.PausedSeconds * 1000L);
                stream.StartOffsetMs = Math.Max(0, stream.EndOffsetMs - playedMs);
                repaired++;
            }

            // The furthest point came from a stale start: only what the streams reached counts now. A play whose
            // furthest point lies beyond every stale start got it elsewhere, and keeps it.
            if (play.MaxOffsetMs <= highestStale)
            {
                play.MaxOffsetMs = play.Streams.Max(s => Math.Max(s.StartOffsetMs, s.EndOffsetMs));
                play.Watched = rules.IsWatched(play.MaxOffsetMs, play.DurationMs, play.Item.CreditsStartMs);
            }

            changed.Add(play);
        }

        var merged = 0;
        foreach (var group in plays.GroupBy(p => (p.UserId, p.ItemId)))
        {
            Play? current = null;
            foreach (var play in group.OrderBy(p => p.StartedAt).ThenBy(p => p.Id))
            {
                if (current is not null && (changed.Contains(current) || changed.Contains(play)) && PlayRules.Continues(current, FirstReached(play)))
                {
                    Merge(current, play, rules);
                    changed.Add(current);
                    merged++;
                    continue;
                }

                current = play;
            }
        }

        return (repaired, merged);
    }

    /// <summary>What the play's first stream reached: what the tracker weighed when it opened the play.</summary>
    private static long FirstReached(Play play)
    {
        if (play.Streams.Count <= 1)
        {
            return play.MaxOffsetMs;
        }

        var first = play.Streams.OrderBy(s => s.StartedAt).First();
        return Math.Max(first.StartOffsetMs, first.EndOffsetMs);
    }

    /// <summary>Folds <paramref name="later"/> into <paramref name="play"/> the way the tracker adds a stream to the play it continues.</summary>
    private void Merge(Play play, Play later, PlayRules rules)
    {
        foreach (var stream in later.Streams.ToList())
        {
            later.Streams.Remove(stream);
            play.Streams.Add(stream);
        }

        play.StartedAt = later.StartedAt < play.StartedAt ? later.StartedAt : play.StartedAt;
        play.StoppedAt = later.StoppedAt > play.StoppedAt ? later.StoppedAt : play.StoppedAt;
        play.PlayedSeconds += later.PlayedSeconds;
        play.PausedSeconds += later.PausedSeconds;
        play.MaxOffsetMs = Math.Max(play.MaxOffsetMs, later.MaxOffsetMs);
        play.DurationMs = later.DurationMs ?? play.DurationMs;
        play.Watched = rules.IsWatched(play.MaxOffsetMs, play.DurationMs, play.Item.CreditsStartMs);
        play.MovieId = later.MovieId ?? play.MovieId;
        if (later.VersionId is not null)
        {
            play.VersionId = later.VersionId;
            play.VersionName = later.VersionName;
        }

        db.Plays.Remove(later);
    }
}
