using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>What one pass did, for the tests and the logs.</summary>
public sealed record NewEpisodePass(int Ready, int Waiting, int Sent, int Failed);

/// <summary>
/// One pass over the new-episode announcements. A batch closes <see cref="NewEpisodeOptions.Debounce"/> after its last
/// episode; then, when someone follows the show, Plex is asked whether it lists every episode of the batch (by file,
/// else by number) in the show the followers played. Seen, or <see cref="NewEpisodeOptions.PlexWait"/> later anyway,
/// the message is built once and one row per recipient is created, in one save. Then each due message is sent: a
/// refusal of the user (403) gives up on that user only; a 429 holds every message of the pass until Discord's delay
/// is over, without spending an attempt; other failures retry like webhook deliveries.
/// </summary>
public sealed class NewEpisodeAnnouncements(
    BigarrDbContext db,
    NewEpisodeFollowers followers,
    PlexClient plex,
    DiscordBotClient discord,
    IOptions<NewEpisodeOptions> options,
    IOptions<NotificationsOptions> notifications,
    TimeProvider time,
    ILogger<NewEpisodeAnnouncements> logger)
{
    public const int BatchSize = 50;

    /// <summary>Set by a 429 during the pass: the time Discord said to wait until, which the rest of the pass waits too.</summary>
    private DateTime? RateLimitedUntil { get; set; }

    public async Task<NewEpisodePass> RunAsync(CancellationToken ct)
    {
        var instance = await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == InstanceSettings.SingletonId, ct);
        var token = instance?.DiscordBotToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            // Nothing is collected without a token; what was collected before it was removed waits for a new one.
            return new NewEpisodePass(0, 0, 0, 0);
        }

        var (ready, waiting) = await CloseAsync(instance!.NewEpisodes, ct);
        var (sent, failed) = await SendAsync(token, ct);
        return new NewEpisodePass(ready, waiting, sent, failed);
    }

    private async Task<(int Ready, int Waiting)> CloseAsync(NewEpisodeSettings message, CancellationToken ct)
    {
        var settings = options.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var closedBefore = now - settings.Debounce;
        var fullBefore = now - settings.MaxBatch;
        var due = await db.NewEpisodeAnnouncements
            .Include(a => a.Episodes)
            .Where(a => a.ReadyAt == null && (a.LastAddedAt <= closedBefore || a.CreatedAt <= fullBefore))
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        var ready = 0;
        foreach (var announcement in due)
        {
            if (await ReadyAsync(announcement, message, now, ct))
            {
                ready++;
            }

            // Ready or still waiting, what was decided is kept before the next show is looked at.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return (ready, due.Count - ready);
    }

    private async Task<bool> ReadyAsync(NewEpisodeAnnouncement announcement, NewEpisodeSettings message, DateTime now, CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking().Where(s => s.Id == announcement.SeriesId).Select(s => new { s.Title, s.Images }).FirstOrDefaultAsync(ct);
        var found = series is null ? ShowFollowers.None : await followers.ForSeriesAsync(announcement.SeriesId, ct);
        if (series is null || found.Recipients.Count == 0)
        {
            announcement.ReadyAt = now;
            announcement.PlexWait = PlexWait.NoRecipient;
            logger.LogDebug("No one to tell about the new episodes of series {SeriesId}", announcement.SeriesId);
            return true;
        }

        var (seen, link) = await PlexAsync(announcement, found.PlexShows, ct);
        announcement.PlexCheckedAt = now;
        var deadline = Closed(announcement) + options.Value.PlexWait;
        if (seen == false && now < deadline)
        {
            return false;
        }

        announcement.PlexWait = seen switch
        {
            true => PlexWait.Seen,
            false => PlexWait.TimedOut,
            null => PlexWait.NotChecked,
        };
        var episodes = announcement.Episodes.Select(e => new AnnouncedEpisode(e.SeasonNumber, e.EpisodeNumber, e.Title, e.Overview)).ToList();
        var poster = series.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl;
        announcement.Body = Discord.DiscordWebhook.Serialize(NewEpisodeMessages.Payload(series.Title, poster, episodes, link, now, message));
        announcement.ReadyAt = now;
        foreach (var user in found.Recipients)
        {
            announcement.Messages.Add(new NewEpisodeMessage { UserId = user.Id, DiscordUserId = user.DiscordUserId!, NextAttemptAt = now, CreatedAt = now });
        }

        logger.LogInformation(
            "{Count} new episode(s) of {Series} to tell {Recipients} user(s) ({Wait})",
            episodes.Count,
            series.Title,
            found.Recipients.Count,
            announcement.PlexWait);
        return true;
    }

    /// <summary>When the batch stopped taking episodes: a debounce after the last one, or when it was full.</summary>
    private DateTime Closed(NewEpisodeAnnouncement announcement)
    {
        var settings = options.Value;
        var quiet = announcement.LastAddedAt + settings.Debounce;
        var full = announcement.CreatedAt + settings.MaxBatch;
        return quiet < full ? quiet : full;
    }

    /// <summary>
    /// Whether Plex lists every episode of the batch, on the first Plex server that knows the show: true, false (not
    /// yet), or null when no server can tell (none knows the show, or it was re-matched). The link goes with any answer.
    /// </summary>
    private async Task<(bool? Seen, PlexShowLink? Link)> PlexAsync(NewEpisodeAnnouncement announcement, IReadOnlyList<(int ServerId, string ShowRatingKey)> shows, CancellationToken ct)
    {
        if (shows.Count == 0)
        {
            return (null, null);
        }

        var ids = shows.Select(s => s.ServerId).ToList();
        var servers = await db.MediaServers.AsNoTracking().Where(s => ids.Contains(s.Id) && s.Enabled && s.Kind == MediaServerKind.Plex).ToListAsync(ct);
        foreach (var (serverId, showKey) in shows)
        {
            if (servers.FirstOrDefault(s => s.Id == serverId) is not { } server)
            {
                continue;
            }

            try
            {
                var listed = await plex.EpisodesAsync(server, showKey, ct);
                if (listed is null)
                {
                    continue;
                }

                var machine = await plex.MachineIdentifierAsync(server, ct);
                var link = string.IsNullOrEmpty(machine) ? null : new PlexShowLink(machine, showKey);
                return (announcement.Episodes.All(e => Lists(listed, server, e)), link);
            }
            catch (MediaServerException ex)
            {
                // Down for now: the wait goes on, and runs out like any other.
                logger.LogDebug("Plex {Server} could not list the episodes of show {ShowKey}: {Error}", server.Name, showKey, ex.Message);
                return (false, null);
            }
        }

        return (null, null);
    }

    private static bool Lists(List<PlexEpisode> listed, MediaServer server, NewEpisodeAnnouncementEpisode episode)
    {
        var file = server.Translate(episode.FilePath);
        return listed.Any(p => p.Files.Contains(file, StringComparer.Ordinal))
            || listed.Any(p => p.SeasonNumber == episode.SeasonNumber && p.EpisodeNumber == episode.EpisodeNumber);
    }

    private async Task<(int Sent, int Failed)> SendAsync(string token, CancellationToken ct)
    {
        var settings = notifications.Value;
        var now = time.GetUtcNow().UtcDateTime;
        RateLimitedUntil = null;
        var due = await db.NewEpisodeMessages
            .Include(m => m.Announcement)
            .Where(m => m.DeliveredAt == null && m.NextAttemptAt != null && m.NextAttemptAt <= now)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        int sent = 0, failed = 0;
        foreach (var message in due)
        {
            ct.ThrowIfCancellationRequested();
            if (RateLimitedUntil is { } until)
            {
                // The bot was told to wait: the rest would only be refused again.
                message.NextAttemptAt = until;
            }
            else if (await AttemptAsync(message))
            {
                sent++;
            }
            else
            {
                failed++;
            }

            // Written as soon as known: a message Discord took is never sent twice because a later one was interrupted.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return (sent, failed);

        async Task<bool> AttemptAsync(NewEpisodeMessage message)
        {
            var attemptAt = time.GetUtcNow().UtcDateTime;
            message.Attempts++;
            message.LastAttemptAt = attemptAt;
            if (message.Announcement.Body is not { } body)
            {
                message.NextAttemptAt = null;
                message.LastError = "The announcement has no message.";
                return false;
            }

            var result = await discord.SendAsync(token, message.DiscordUserId, body, ct);
            message.LastStatusCode = result.StatusCode;
            if (result.Success)
            {
                message.DeliveredAt = attemptAt;
                message.NextAttemptAt = null;
                message.LastError = null;
                logger.LogDebug("New episodes told to user {UserId} in {Elapsed} ms", message.UserId, (int)result.Elapsed.TotalMilliseconds);
                return true;
            }

            message.LastError = result.Error;
            if (result.RetryAfter is { } wait)
            {
                var longest = settings.RetryDelay(int.MaxValue);
                message.Attempts--;
                message.NextAttemptAt = attemptAt + (wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > longest ? longest : wait);
                RateLimitedUntil = message.NextAttemptAt;
                logger.LogInformation("The Discord bot is rate limited: {Error}. Next try at {Next:u}", result.Error, message.NextAttemptAt);
                return false;
            }

            if (result.Refused)
            {
                // This user only: the others still get theirs.
                message.NextAttemptAt = null;
                logger.LogWarning("Discord refuses direct messages to user {UserId} ({Error}): they must share a server with the bot and accept its messages", message.UserId, result.Error);
                return false;
            }

            if (message.Attempts >= Math.Max(1, settings.MaxAttempts))
            {
                message.NextAttemptAt = null;
                logger.LogWarning("Gave up telling user {UserId} about new episodes after {Attempts} attempt(s): {Error}", message.UserId, message.Attempts, result.Error);
            }
            else
            {
                message.NextAttemptAt = attemptAt + settings.RetryDelay(message.Attempts);
                logger.LogWarning("Could not tell user {UserId} about new episodes (attempt {Attempts}): {Error}. Next try at {Next:u}", message.UserId, message.Attempts, result.Error, message.NextAttemptAt);
            }

            return false;
        }
    }
}

/// <summary>
/// Runs <see cref="NewEpisodeAnnouncements"/> every <see cref="NewEpisodeOptions.SweepSeconds"/>. A single hosted
/// instance, so a pass never overlaps another; everything it decides is in the database, so a restart resumes it.
/// </summary>
public sealed class NewEpisodeAnnouncer(IServiceScopeFactory scopeFactory, IOptions<NewEpisodeOptions> options, IOptions<NotificationsOptions> notifications, TimeProvider time, ILogger<NewEpisodeAnnouncer> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!notifications.Value.Enabled)
        {
            logger.LogInformation("New episode messages disabled (Bigarr:Notifications:Enabled = false)");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = options.Value.Sweep;
                await Task.Delay(wait, time, stoppingToken);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var announcements = scope.ServiceProvider.GetRequiredService<NewEpisodeAnnouncements>();
                    await announcements.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "New episode announcements crashed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
