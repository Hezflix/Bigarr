using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>
/// Turns what Plex says about its streams into plays. Two inputs:
/// <list type="bullet">
/// <item>the full list of streams (<c>/status/sessions</c>): new ones open a <see cref="PlaybackSession"/>, known ones
/// are refreshed, and the ones gone are closed where they were last seen;</item>
/// <item>a <c>playing</c> notification: the state and position of a known stream, or its stop.</item>
/// </list>
/// A closed session long enough under the <see cref="PlayRules"/> becomes a <see cref="PlayStream"/>, added to the play
/// it continues or to a new one. Everything lives in the database, so a restart in the middle of a film loses nothing.
/// </summary>
public sealed class PlaybackTracker(BigarrDbContext db, PlexClient plex, PlexLibraries libraries, VersionResolver resolver, LiveTranscodes transcodes, TimeProvider time, ILogger<PlaybackTracker> logger)
{
    /// <summary>An item's metadata is read again after this long, when one of its streams starts.</summary>
    public static readonly TimeSpan ItemRefresh = TimeSpan.FromDays(1);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Brings the open sessions in line with the server's list of streams. Returns how many are playing.</summary>
    public async Task<int> ReconcileAsync(MediaServer server, IReadOnlyList<PlexSession> live, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(live);

        var now = Now;
        var open = await db.PlaybackSessions.Include(s => s.User).Include(s => s.Item)
            .Where(s => s.MediaServerId == server.Id)
            .ToListAsync(ct);

        var seen = new HashSet<PlaybackSession>();
        foreach (var stream in live)
        {
            var session = open.FirstOrDefault(o => !seen.Contains(o) && IsSame(o, stream));
            if (session is null)
            {
                session = await OpenAsync(server, stream, now, ct);
            }
            else
            {
                Refresh(session, stream, now);
            }

            seen.Add(session);
        }

        await db.SaveChangesAsync(ct);

        foreach (var gone in open.Where(o => !seen.Contains(o)))
        {
            await CloseAsync(gone, gone.LastSeenAt, ct);
            transcodes.Set(server.Id, gone.SessionKey, null);
        }

        // After the closes: a stream gone and a new one may share a session key (Plex restarted).
        foreach (var stream in live)
        {
            transcodes.Set(server.Id, stream.SessionKey, stream.Transcode);
        }

        return live.Count;
    }

    /// <summary>
    /// Applies a notification to its session. True when the server's list should be read: an unknown stream (a new
    /// one, or the next episode on the same player) or a changed state (the decision may have changed with it).
    /// </summary>
    public async Task<bool> OnEventAsync(MediaServer server, PlexPlayingEvent playing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(playing);

        var session = await db.PlaybackSessions.Include(s => s.User).Include(s => s.Item)
            .FirstOrDefaultAsync(s => s.MediaServerId == server.Id && s.SessionKey == playing.SessionKey && s.RatingKey == playing.RatingKey, ct);
        if (session is null)
        {
            return !playing.IsStopped;
        }

        var now = Now;
        if (playing.IsStopped)
        {
            if (playing.ViewOffsetMs is { } offset)
            {
                Move(session, offset);
            }

            await CloseAsync(session, now, ct);
            transcodes.Set(server.Id, session.SessionKey, null);
            return false;
        }

        var state = PlexPlayback.StateOf(playing.State);
        var changed = state != session.State;
        ApplyState(session, state, playing.ViewOffsetMs ?? session.ViewOffsetMs, now);
        await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>
    /// Ends a session at <paramref name="stoppedAt"/>: a play when it lasted long enough, nothing otherwise. Returns the
    /// play it went into.
    /// </summary>
    public async Task<Play?> CloseAsync(PlaybackSession session, DateTime stoppedAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (stoppedAt < session.StartedAt)
        {
            stoppedAt = session.StartedAt;
        }

        if (session.PausedSince is { } pausedSince)
        {
            session.PausedSeconds += Seconds(stoppedAt - pausedSince);
            session.PausedSince = null;
        }

        db.PlaybackSessions.Remove(session);
        var rules = new PlayRules(await SettingsAsync(ct));
        var item = session.Item;
        var played = Math.Max(0, Seconds(stoppedAt - session.StartedAt) - session.PausedSeconds);
        if (!session.User.KeepHistory || !rules.IsLongEnough(played, item.DurationMs))
        {
            await db.SaveChangesAsync(ct);
            logger.LogDebug("{User} played {Title} for {Seconds} s: not a play", session.User.ShownName, item.Title, played);
            return null;
        }

        var end = PlayRules.SnapEnd(session.ViewOffsetMs, item.DurationMs);
        var reached = Math.Max(session.MaxOffsetMs, end);
        var stream = new PlayStream
        {
            StartedAt = session.StartedAt,
            StoppedAt = stoppedAt,
            StartOffsetMs = session.StartOffsetMs,
            EndOffsetMs = end,
            PausedSeconds = session.PausedSeconds,
            Details = session.Details.Copy(),
            FilePath = session.FilePath,
            VersionId = session.VersionId,
        };

        var previous = await db.Plays.Where(p => p.UserId == session.UserId && p.ItemId == session.ItemId)
            .OrderByDescending(p => p.StoppedAt)
            .FirstOrDefaultAsync(ct);
        var play = previous is not null && PlayRules.Continues(previous, reached)
            ? previous
            : db.Plays.Add(new Play { UserId = session.UserId, ItemId = session.ItemId, StartedAt = session.StartedAt, Source = PlaySource.Live }).Entity;

        play.Streams.Add(stream);
        play.StoppedAt = stoppedAt > play.StoppedAt ? stoppedAt : play.StoppedAt;
        play.PlayedSeconds += played;
        play.PausedSeconds += session.PausedSeconds;
        play.MaxOffsetMs = Math.Max(play.MaxOffsetMs, reached);
        play.DurationMs = item.DurationMs ?? play.DurationMs;
        play.Watched = rules.IsWatched(play.MaxOffsetMs, play.DurationMs, item.CreditsStartMs);
        play.MovieId = session.MovieId ?? play.MovieId;
        if (session.VersionId is not null)
        {
            play.VersionId = session.VersionId;
            play.VersionName = session.VersionName;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("{User} played {Title} for {Minutes} min{Watched}", session.User.ShownName, item.Title, played / 60, play.Watched ? " (watched)" : string.Empty);
        return play;
    }

    public async Task<PlaybackSettings> SettingsAsync(CancellationToken ct) =>
        await db.PlaybackSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlaybackSettings.SingletonId, ct) ?? PlaybackSettings.Default();

    /// <summary>
    /// Same stream: Plex reuses session keys after a restart and moves a player to the next episode under the same key,
    /// so the rating key, the user and Plex's own session id must agree too.
    /// </summary>
    private static bool IsSame(PlaybackSession session, PlexSession stream) =>
        session.SessionKey == stream.SessionKey
        && session.RatingKey == stream.RatingKey
        && session.User.ExternalId == stream.UserId
        && (session.ServerSessionId is null || stream.SessionId is null || session.ServerSessionId == stream.SessionId);

    private async Task<PlaybackSession> OpenAsync(MediaServer server, PlexSession stream, DateTime now, CancellationToken ct)
    {
        var user = await UserAsync(server, stream, now, ct);
        var metadata = await MetadataAsync(server, stream.RatingKey, ct);
        var item = await ItemAsync(server, stream.RatingKey, stream.Kind, stream.DurationMs, metadata, now, ct);
        var part = metadata?.Parts.FirstOrDefault(p => p.Id == stream.PartId) ?? (metadata?.Parts.Count == 1 ? metadata.Parts[0] : null);
        var details = WithSource(stream.Details, part);
        var folders = await libraries.FoldersAsync(server, item.LibrarySectionId, ct);
        var resolved = await resolver.ResolveAsync(server, item, part?.File, part?.VideoResolution, folders, ct);

        var session = new PlaybackSession
        {
            MediaServerId = server.Id,
            SessionKey = stream.SessionKey,
            ServerSessionId = stream.SessionId,
            RatingKey = stream.RatingKey,
            User = user,
            Item = item,
            State = stream.State,
            StartOffsetMs = stream.ViewOffsetMs,
            ViewOffsetMs = stream.ViewOffsetMs,
            MaxOffsetMs = stream.ViewOffsetMs,
            StartedAt = now,
            LastSeenAt = now,
            PausedSince = stream.State == PlaybackState.Paused ? now : null,
            Details = details,
            FilePath = resolved.FilePath,
            MovieId = resolved.MovieId,
            VersionId = resolved.VersionId,
            VersionName = resolved.VersionName,
        };
        db.PlaybackSessions.Add(session);
        logger.LogInformation("{User} started {Title} on {Player}", user.ShownName, item.Title, details.PlayerTitle ?? details.PlayerProduct);
        return session;
    }

    private static void Refresh(PlaybackSession session, PlexSession stream, DateTime now)
    {
        ApplyState(session, stream.State, stream.ViewOffsetMs, now);
        session.ServerSessionId ??= stream.SessionId;

        // The list carries the live part (player, network, decision); the source and file were read once, at the start.
        var source = session.Details;
        var details = stream.Details;
        details.SourceContainer = source.SourceContainer;
        details.SourceVideoResolution = source.SourceVideoResolution;
        details.SourceVideoCodec = source.SourceVideoCodec;
        details.SourceDynamicRange = source.SourceDynamicRange;
        details.SourceAudioCodec = source.SourceAudioCodec;
        details.SourceAudioChannels = source.SourceAudioChannels;
        details.SourceBitrate = source.SourceBitrate;
        details.ServerFilePath = source.ServerFilePath;
        session.Details = details;
    }

    private static void ApplyState(PlaybackSession session, PlaybackState state, long offsetMs, DateTime now)
    {
        if (session.PausedSince is { } since && state != PlaybackState.Paused)
        {
            session.PausedSeconds += Seconds(now - since);
            session.PausedSince = null;
        }
        else if (state == PlaybackState.Paused && session.PausedSince is null)
        {
            session.PausedSince = now;
        }

        session.State = state;
        Move(session, offsetMs);
        session.LastSeenAt = now;
    }

    private static void Move(PlaybackSession session, long offsetMs)
    {
        session.ViewOffsetMs = offsetMs;
        session.MaxOffsetMs = Math.Max(session.MaxOffsetMs, offsetMs);
    }

    private static StreamDetails WithSource(StreamDetails details, PlexPart? part)
    {
        if (part is null)
        {
            return details;
        }

        details.SourceContainer = part.Container;
        details.SourceVideoResolution = part.VideoResolution;
        details.SourceVideoCodec = part.VideoCodec;
        details.SourceDynamicRange = part.DynamicRange;
        details.SourceAudioCodec = part.AudioCodec;
        details.SourceAudioChannels = part.AudioChannels;
        details.SourceBitrate = part.Bitrate;
        details.ServerFilePath = part.File;
        return details;
    }

    private async Task<MediaServerUser> UserAsync(MediaServer server, PlexSession stream, DateTime now, CancellationToken ct)
    {
        // Two new streams in one pass may share a user or an item not saved yet: the tracked ones count first.
        var user = db.MediaServerUsers.Local.FirstOrDefault(u => u.MediaServerId == server.Id && u.ExternalId == stream.UserId)
            ?? await db.MediaServerUsers.FirstOrDefaultAsync(u => u.MediaServerId == server.Id && u.ExternalId == stream.UserId, ct);
        if (user is null)
        {
            user = new MediaServerUser { MediaServerId = server.Id, ExternalId = stream.UserId, Name = stream.UserName, FirstSeenAt = now };
            db.MediaServerUsers.Add(user);
        }

        user.Name = stream.UserName;
        user.ThumbUrl = stream.UserThumb ?? user.ThumbUrl;

        // Plex numbers the server owner's account 1 in its own sessions.
        user.IsOwner = stream.UserId == "1";
        user.LastSeenAt = now;
        return user;
    }

    private async Task<PlexItem?> MetadataAsync(MediaServer server, string ratingKey, CancellationToken ct)
    {
        try
        {
            return await plex.ItemAsync(server, ratingKey, ct);
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Plex metadata of item {RatingKey} unavailable: {Error}", ratingKey, ex.Message);
            return null;
        }
    }

    /// <summary>The item of a rating key Plex's history names, read from Plex when Bigarr has it not or for too long.</summary>
    public async Task<MediaServerItem> HistoryItemAsync(MediaServer server, PlexHistoryEntry entry, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(entry);

        var known = await db.MediaServerItems.FirstOrDefaultAsync(i => i.MediaServerId == server.Id && i.RatingKey == entry.RatingKey, ct);
        if (known is not null && now - known.UpdatedAt < ItemRefresh)
        {
            return known;
        }

        var item = await ItemAsync(server, entry.RatingKey, entry.Kind, null, await MetadataAsync(server, entry.RatingKey, ct), now, ct);
        item.Title = item.Title == entry.RatingKey ? entry.Title : item.Title;
        item.LibrarySectionId ??= entry.LibrarySectionId;
        return item;
    }

    private async Task<MediaServerItem> ItemAsync(MediaServer server, string ratingKey, MediaItemKind kind, long? durationMs, PlexItem? metadata, DateTime now, CancellationToken ct)
    {
        var item = db.MediaServerItems.Local.FirstOrDefault(i => i.MediaServerId == server.Id && i.RatingKey == ratingKey)
            ?? await db.MediaServerItems.FirstOrDefaultAsync(i => i.MediaServerId == server.Id && i.RatingKey == ratingKey, ct);
        if (item is null)
        {
            item = new MediaServerItem { MediaServerId = server.Id, RatingKey = ratingKey, Kind = kind, Title = metadata?.Title ?? ratingKey, FirstSeenAt = now };
            db.MediaServerItems.Add(item);
        }

        item.DurationMs ??= durationMs;
        if (metadata is null || now - item.UpdatedAt < ItemRefresh && item.Id != 0)
        {
            return item;
        }

        item.Kind = metadata.Kind;
        item.Guid = metadata.Guid;
        item.TmdbId = metadata.Ids.TmdbId;
        item.ImdbId = metadata.Ids.ImdbId;
        item.TvdbId = metadata.Ids.TvdbId;
        item.Title = metadata.Title;
        item.Year = metadata.Year;
        item.DurationMs = metadata.DurationMs ?? durationMs;
        item.CreditsStartMs = metadata.CreditsStartMs;
        item.LibrarySectionId = metadata.LibrarySectionId;
        item.LibrarySectionTitle = metadata.LibrarySectionTitle;
        item.UpdatedAt = now;

        if (metadata.Kind == MediaItemKind.Movie)
        {
            // A link chosen by hand outlives the item's ids, which are what got it wrong or left it out.
            if (!item.LinkedByHand)
            {
                item.MovieId = await MovieIdAsync(metadata.Ids, ct);
            }
        }
        else
        {
            item.ShowRatingKey = metadata.ShowRatingKey;
            item.ShowTitle = metadata.ShowTitle;
            item.SeasonRatingKey = metadata.SeasonRatingKey;
            item.SeasonNumber = metadata.SeasonNumber;
            item.EpisodeNumber = metadata.EpisodeNumber;
            if (metadata.ShowRatingKey is { } showKey && await ShowIdsAsync(server, showKey, ct) is { } show)
            {
                item.ShowGuid = show.Guid;
                item.ShowTmdbId = show.Ids.TmdbId;
                item.ShowTvdbId = show.Ids.TvdbId;
                item.ShowImdbId = show.Ids.ImdbId;
            }
        }

        return item;
    }

    private async Task<(string? Guid, PlexIds Ids, string? Title)?> ShowIdsAsync(MediaServer server, string ratingKey, CancellationToken ct)
    {
        try
        {
            return await plex.IdsAsync(server, ratingKey, ct);
        }
        catch (MediaServerException ex)
        {
            logger.LogDebug("Plex show {RatingKey} unavailable: {Error}", ratingKey, ex.Message);
            return null;
        }
    }

    private async Task<int?> MovieIdAsync(PlexIds ids, CancellationToken ct)
    {
        if (ids.TmdbId is { } tmdbId && await db.Movies.Where(m => m.TmdbId == tmdbId).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct) is { } byTmdb)
        {
            return byTmdb;
        }

        return ids.ImdbId is { } imdbId
            ? await db.Movies.Where(m => m.ImdbId == imdbId).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct)
            : null;
    }

    private static int Seconds(TimeSpan span) => Math.Max(0, (int)Math.Round(span.TotalSeconds));
}
