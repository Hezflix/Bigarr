using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>What one pass over a server's watch history did.</summary>
public sealed record BackstopPass(string Server, int Read, int Added, string? Error);

/// <summary>
/// The safety net under the live tracking: Plex's own watch history, read once a day from a checkpoint, adds a play
/// for every viewed mark Bigarr has no play for (Bigarr was down, the event stream missed a stream). Such a play only
/// knows who finished what and when: <see cref="PlaySource.ServerHistory"/>, no stream, watched, its length taken as
/// the item's duration.
/// <list type="bullet">
/// <item>A mark is covered by any play of the same user and item within <see cref="Window"/> of it.</item>
/// <item>Marks younger than <see cref="Settle"/> wait for the next pass: the live play of a film watched to the
/// credits closes after Plex marks it viewed.</item>
/// <item>The first pass starts at the server's first live play: what came before is the Tautulli import's business.</item>
/// </list>
/// It only reads from Plex.
/// </summary>
public sealed class PlexHistoryBackstop(BigarrDbContext db, PlexClient plex, PlaybackTracker tracker, PlexLibraries libraries, VersionResolver resolver, TimeProvider time, ILogger<PlexHistoryBackstop> logger)
{
    public static readonly TimeSpan Settle = TimeSpan.FromHours(6);

    public static readonly TimeSpan Window = TimeSpan.FromHours(6);

    public const int PageSize = 200;

    /// <summary>The server's accounts, read once per pass and only when a mark names a user Bigarr never saw.</summary>
    private Dictionary<string, PlexAccount>? _accounts;

    public async Task<List<BackstopPass>> RunAllAsync(CancellationToken ct)
    {
        var ids = await db.MediaServers.AsNoTracking()
            .Where(s => s.Enabled && s.Kind == MediaServerKind.Plex)
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var passes = new List<BackstopPass>();
        foreach (var id in ids)
        {
            passes.Add(await RunAsync(id, ct));
        }

        return passes;
    }

    public async Task<BackstopPass> RunAsync(int serverId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var server = await db.MediaServers.FirstAsync(s => s.Id == serverId, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var until = now - Settle;
        var since = server.PlayHistoryCheckedUntil ?? await db.Plays
            .Where(p => p.User.MediaServerId == serverId && p.Source == PlaySource.Live)
            .MinAsync(p => (DateTime?)p.StartedAt, ct);
        if (since is null || since >= until)
        {
            // Nothing followed yet: start from now on.
            server.PlayHistoryCheckedUntil ??= until;
            await db.SaveChangesAsync(ct);
            return new BackstopPass(server.Name, 0, 0, null);
        }

        var read = 0;
        var added = 0;
        try
        {
            _accounts = null;
            for (var start = 0; ; start += PageSize)
            {
                var page = await plex.HistoryAsync(server, since.Value, start, PageSize, ct);
                foreach (var entry in page.Entries.Where(e => e.ViewedAt < until))
                {
                    read++;
                    if (await AddAsync(server, entry, now, ct))
                    {
                        added++;
                    }
                }

                if (page.Entries.Count == 0 || start + PageSize >= page.Total)
                {
                    break;
                }
            }
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Plex's watch history of {Server} unavailable: {Error}", server.Name, ex.Message);
            return new BackstopPass(server.Name, read, added, ex.Message);
        }

        server.PlayHistoryCheckedUntil = until;
        await db.SaveChangesAsync(ct);
        if (added > 0)
        {
            logger.LogInformation("Plex's watch history of {Server} gave {Added} play(s) the live tracking missed", server.Name, added);
        }

        return new BackstopPass(server.Name, read, added, null);
    }

    private async Task<bool> AddAsync(MediaServer server, PlexHistoryEntry entry, DateTime now, CancellationToken ct)
    {
        var user = await db.MediaServerUsers.FirstOrDefaultAsync(u => u.MediaServerId == server.Id && u.ExternalId == entry.AccountId, ct);
        if (user is { KeepHistory: false })
        {
            return false;
        }

        var item = await db.MediaServerItems.FirstOrDefaultAsync(i => i.MediaServerId == server.Id && i.RatingKey == entry.RatingKey, ct);
        if (user is not null && item is not null)
        {
            var from = entry.ViewedAt - Window;
            var to = entry.ViewedAt + Window;
            if (await db.Plays.AnyAsync(p => p.UserId == user.Id && p.ItemId == item.Id && p.StartedAt <= to && p.StoppedAt >= from, ct))
            {
                return false;
            }

            // A stream of it still open (paused on the credits for hours) is the live tracking's to record when it
            // closes: a play added here would make that viewing count twice.
            if (await db.PlaybackSessions.AnyAsync(s => s.UserId == user.Id && s.ItemId == item.Id && s.StartedAt <= to, ct))
            {
                return false;
            }
        }

        if (user is null)
        {
            _accounts ??= (await plex.AccountsAsync(server, ct)).ToDictionary(a => a.Id, StringComparer.Ordinal);
            var account = _accounts.GetValueOrDefault(entry.AccountId);
            user = new MediaServerUser
            {
                MediaServerId = server.Id,
                ExternalId = entry.AccountId,
                Name = account?.Name ?? $"Account {entry.AccountId}",
                ThumbUrl = account?.Thumb,
                IsOwner = entry.AccountId == "1",
                FirstSeenAt = entry.ViewedAt,
                LastSeenAt = entry.ViewedAt,
            };
            db.MediaServerUsers.Add(user);
        }

        item = await tracker.HistoryItemAsync(server, entry, now, ct);
        var folders = await libraries.FoldersAsync(server, item.LibrarySectionId, ct);
        var resolved = await resolver.ResolveAsync(server, item, null, null, folders, ct);
        var duration = item.DurationMs ?? 0;
        db.Plays.Add(new Play
        {
            User = user,
            Item = item,
            StartedAt = entry.ViewedAt.AddMilliseconds(-duration),
            StoppedAt = entry.ViewedAt,
            PlayedSeconds = (int)(duration / 1000),
            MaxOffsetMs = duration,
            DurationMs = item.DurationMs,
            Watched = true,
            Source = PlaySource.ServerHistory,
            MovieId = resolved.MovieId,
            VersionId = resolved.VersionId,
            VersionName = resolved.VersionName,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>The backstop as a daily task. It only reads Plex, so it keeps its schedule in shadow mode.</summary>
public sealed class PlexHistoryJob(PlexHistoryBackstop backstop) : IScheduledJob
{
    public const string JobName = "plexHistory";

    public string Name => JobName;

    public string DisplayName => "Plex watch history";

    public TimeSpan Interval => TimeSpan.FromDays(1);

    public bool Enabled => true;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(10);

    public bool RunsWhenSchedulerDisabled => true;

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var passes = await backstop.RunAllAsync(ct);
        if (passes.Count == 0)
        {
            return "No Plex server";
        }

        if (passes.All(p => p.Error is not null))
        {
            throw new InvalidOperationException(string.Join(" · ", passes.Select(p => $"{p.Server}: {p.Error}")));
        }

        return string.Join(" · ", passes.Select(p => p.Error is null ? $"{p.Server}: {p.Read} viewed, {p.Added} added" : $"{p.Server}: failed, {p.Error}"));
    }
}
