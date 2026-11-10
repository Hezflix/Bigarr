using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>
/// Read side of playback (now playing, plays, users, plays per version, locations), the play rules, and stopping a
/// stream on request.
/// </summary>
public sealed class PlaybackService(BigarrDbContext db, PlaybackMonitor monitor, LiveTranscodes transcodes, GeoLocator geo, PlexClient plex, TimeProvider time, ILogger<PlaybackService> logger)
{
    public const int MaxPageSize = 200;

    /// <summary>The message shown on the viewer's player: 200 characters, Plex's own limit for it.</summary>
    public const int MaxStopMessageLength = 200;

    public async Task<NowPlayingResponse> NowPlayingAsync(CancellationToken ct)
    {
        var servers = await db.MediaServers.AsNoTracking()
            .Where(s => s.Enabled && s.Kind == MediaServerKind.Plex)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);
        var statuses = servers.Select(s => monitor.Status.TryGetValue(s.Id, out var status)
                ? new PlaybackServerStatus(s.Id, s.Name, status.Connected, status.Error, status.Since)
                : new PlaybackServerStatus(s.Id, s.Name, false, null, null))
            .ToList();

        var sessions = await db.PlaybackSessions.AsNoTracking()
            .OrderBy(s => s.StartedAt)
            .Select(s => new PlaybackSessionResponse(
                s.Id,
                s.MediaServerId,
                s.SessionKey,
                new PlaybackUserRef(s.User.Id, s.User.DisplayName ?? s.User.Name, s.User.ThumbUrl),
                new PlaybackItemRef(s.Item.Id, s.Item.Kind, s.Item.Movie != null ? s.Item.Movie.Title : s.Item.Title, s.Item.Movie != null ? s.Item.Movie.Year : s.Item.Year, s.Item.ShowTitle, s.Item.SeasonNumber, s.Item.EpisodeNumber, s.Item.DurationMs, s.Item.LibrarySectionTitle, s.Item.MovieId),
                s.State,
                s.ViewOffsetMs,
                s.StartedAt,
                s.PausedSeconds,
                s.VersionId,
                s.VersionName,
                s.Details,
                null,
                null,
                null,
                null,
                s.ServerSessionId != null))
            .ToListAsync(ct);
        // Bigarr's own images for the films it manages; episodes and other films have none until shows come.
        var movieIds = sessions.Select(s => s.Item.MovieId).OfType<int>().Distinct().ToList();
        var images = await db.Movies.AsNoTracking().Where(m => movieIds.Contains(m.Id)).Select(m => new { m.Id, m.Images }).ToDictionaryAsync(m => m.Id, m => m.Images, ct);
        var located = new List<PlaybackSessionResponse>(sessions.Count);
        foreach (var s in sessions)
        {
            var movieImages = s.Item.MovieId is { } id && images.TryGetValue(id, out var found) ? found : [];
            located.Add(s with
            {
                Transcode = transcodes.Get(s.ServerId, s.SessionKey),
                PosterUrl = movieImages.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                FanartUrl = movieImages.FirstOrDefault(i => i.Type == ImageType.Fanart)?.RemoteUrl,
                Location = await geo.ResolveAsync(s.Details.IpAddress, s.Details.Local, s.ServerId, ct),
            });
        }

        return new NowPlayingResponse(statuses, located);
    }

    /// <summary>
    /// Stops a stream on its player, with an optional message shown to the viewer. Manual only: nothing in Bigarr calls
    /// this on its own. The stream then ends like any other, through Plex's stop notification.
    /// </summary>
    public async Task<Result<bool>> StopAsync(int sessionId, string? message, CancellationToken ct)
    {
        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message?.Length > MaxStopMessageLength)
        {
            return Failure.Invalid("Message too long", $"At most {MaxStopMessageLength} characters.");
        }

        var session = await db.PlaybackSessions.AsNoTracking()
            .Include(s => s.MediaServer).Include(s => s.User).Include(s => s.Item)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return Failure.NotFound("Stream");
        }

        if (session.MediaServer.Kind != MediaServerKind.Plex || session.ServerSessionId is null)
        {
            return Failure.Conflict("This stream cannot be stopped", "The server did not give the stream's session id.");
        }

        try
        {
            await plex.TerminateAsync(session.MediaServer, session.ServerSessionId, message, ct);
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Could not stop the stream of {User} ({Title}): {Error}", session.User.ShownName, session.Item.Title, ex.Message);
            return new Failure(StatusCodes.Status502BadGateway, "Plex did not stop the stream", ex.Message);
        }

        logger.LogInformation("Stopped the stream of {User} ({Title}) on request", session.User.ShownName, session.Item.Title);
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// The addresses a user played from, newest first, each with its place (looked up on demand) and its
    /// streams. Null when the user does not exist.
    /// </summary>
    public async Task<List<PlaybackAddressResponse>?> AddressesAsync(int userId, CancellationToken ct)
    {
        if (!await db.MediaServerUsers.AnyAsync(u => u.Id == userId, ct))
        {
            return null;
        }

        var streams = await db.PlayStreams.AsNoTracking()
            .Where(s => s.Play.UserId == userId && s.Details.IpAddress != null)
            .OrderByDescending(s => s.StoppedAt)
            .Select(s => new { Ip = s.Details.IpAddress!, s.StartedAt, s.StoppedAt, s.Details.Local, Player = s.Details.PlayerTitle ?? s.Details.PlayerProduct, Platform = s.Details.PlayerPlatform, ServerId = s.Play.Item.MediaServerId })
            .ToListAsync(ct);

        var addresses = new List<PlaybackAddressResponse>();
        foreach (var group in streams.GroupBy(s => s.Ip))
        {
            var last = group.First();
            var location = await geo.ResolveAsync(last.Ip, group.Any(s => s.Local == true), last.ServerId, ct);
            addresses.Add(new PlaybackAddressResponse(last.Ip, location, group.Count(), group.Min(s => s.StartedAt), last.StoppedAt, last.Player, last.Platform));
        }

        return addresses;
    }

    public async Task<PlaysPageResponse> PlaysAsync(int page, int pageSize, PlayFilter filter, PlaySort sort, bool ascending, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = Filter(db.Plays.AsNoTracking(), db.PlayStreams.AsNoTracking(), filter);
        var total = await query.CountAsync(ct);
        var records = await Order(query, sort, ascending)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PlayResponse(
                p.Id,
                new PlaybackUserRef(p.User.Id, p.User.DisplayName ?? p.User.Name, p.User.ThumbUrl),
                new PlaybackItemRef(p.Item.Id, p.Item.Kind, p.Movie != null ? p.Movie.Title : p.Item.Movie != null ? p.Item.Movie.Title : p.Item.Title, p.Movie != null ? p.Movie.Year : p.Item.Movie != null ? p.Item.Movie.Year : p.Item.Year, p.Item.ShowTitle, p.Item.SeasonNumber, p.Item.EpisodeNumber, p.Item.DurationMs, p.Item.LibrarySectionTitle, p.Item.MovieId),
                p.StartedAt,
                p.StoppedAt,
                p.PlayedSeconds,
                p.PausedSeconds,
                p.MaxOffsetMs,
                p.DurationMs,
                p.Watched,
                p.Source,
                p.MovieId,
                p.VersionId,
                p.VersionName,
                p.Streams.Count,
                p.Streams.Max(s => s.Details.Decision),
                p.Streams.OrderByDescending(s => s.StoppedAt).Select(s => s.Details.PlayerTitle ?? s.Details.PlayerProduct).FirstOrDefault(),
                p.Streams.OrderByDescending(s => s.StoppedAt).Select(s => s.Details.PlayerPlatform).FirstOrDefault(),
                p.Streams.OrderByDescending(s => s.StoppedAt).Select(s => s.Details.Local).FirstOrDefault()))
            .ToListAsync(ct);
        return new PlaysPageResponse(page, pageSize, total, records);
    }

    public static IQueryable<Play> Filter(IQueryable<Play> query, IQueryable<PlayStream> streams, PlayFilter filter)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.UserId is { } user)
        {
            query = query.Where(p => p.UserId == user);
        }

        if (filter.MovieId is { } movie)
        {
            query = query.Where(p => p.MovieId == movie);
        }

        if (filter.VersionId is { } version)
        {
            query = query.Where(p => p.VersionId == version);
        }

        if (!string.IsNullOrWhiteSpace(filter.Version))
        {
            var name = filter.Version.Trim();
            query = query.Where(p => p.VersionName == name);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(p => p.Item.Kind == kind);
        }

        if (filter.From is { } from)
        {
            query = query.Where(p => p.StartedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(p => p.StartedAt < to);
        }

        if (filter.Decision is { } decision)
        {
            // A stream with that decision and none further. Two IN lists read once each, where a MAX per play reads
            // the streams of every play.
            var with = streams.Where(s => s.Details.Decision == decision).Select(s => s.PlayId);
            var further = streams.Where(s => s.Details.Decision > decision).Select(s => s.PlayId);
            query = query.Where(p => with.Contains(p.Id) && !further.Contains(p.Id));
        }

        // Film plays linked to no Bigarr film: an import that could not match them, or a film Bigarr does not manage.
        if (filter.Unmatched)
        {
            query = query.Where(p => p.Item.Kind == MediaItemKind.Movie && p.MovieId == null && p.Item.MovieId == null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            query = query.Where(p => EF.Functions.Like(p.Item.Title, pattern, "\\") || EF.Functions.Like(p.Item.ShowTitle!, pattern, "\\"));
        }

        return query;
    }

    /// <summary>The version names plays were recorded under, for the Plays page's version filter.</summary>
    public async Task<List<string>> VersionNamesAsync(CancellationToken ct) =>
        await db.Plays.AsNoTracking().Where(p => p.VersionName != null).Select(p => p.VersionName!).Distinct().OrderBy(n => n).ToListAsync(ct);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>The sessions a play was made of, oldest first, with everything the server said about each.</summary>
    public async Task<List<PlayStreamResponse>?> StreamsAsync(int playId, CancellationToken ct)
    {
        if (!await db.Plays.AnyAsync(p => p.Id == playId, ct))
        {
            return null;
        }

        var rows = await db.PlayStreams.AsNoTracking()
            .Where(s => s.PlayId == playId)
            .OrderBy(s => s.StartedAt).ThenBy(s => s.Id)
            .Select(s => new { Stream = new PlayStreamResponse(s.Id, s.StartedAt, s.StoppedAt, s.StartOffsetMs, s.EndOffsetMs, s.PausedSeconds, s.FilePath, s.VersionId, s.Version != null ? s.Version.Name : null, s.Details, null), ServerId = s.Play.Item.MediaServerId })
            .ToListAsync(ct);

        // Locations are not stored: each address is placed now, through the in-memory cache.
        var streams = new List<PlayStreamResponse>(rows.Count);
        foreach (var row in rows)
        {
            streams.Add(row.Stream with { Location = await geo.ResolveAsync(row.Stream.Details.IpAddress, row.Stream.Details.Local, row.ServerId, ct) });
        }

        return streams;
    }

    /// <summary>Newest first by default; ties keep a stable order by id.</summary>
    private static IOrderedQueryable<Play> Order(IQueryable<Play> query, PlaySort sort, bool ascending) => (sort, ascending) switch
    {
        (PlaySort.User, true) => query.OrderBy(p => p.User.DisplayName ?? p.User.Name).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.User, false) => query.OrderByDescending(p => p.User.DisplayName ?? p.User.Name).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Title, true) => query.OrderBy(p => p.Item.ShowTitle ?? (p.Movie != null ? p.Movie.Title : p.Item.Movie != null ? p.Item.Movie.Title : p.Item.Title)).ThenBy(p => p.Item.SeasonNumber).ThenBy(p => p.Item.EpisodeNumber).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Title, false) => query.OrderByDescending(p => p.Item.ShowTitle ?? (p.Movie != null ? p.Movie.Title : p.Item.Movie != null ? p.Item.Movie.Title : p.Item.Title)).ThenByDescending(p => p.Item.SeasonNumber).ThenByDescending(p => p.Item.EpisodeNumber).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Version, true) => query.OrderBy(p => p.VersionName).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Version, false) => query.OrderByDescending(p => p.VersionName).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Played, true) => query.OrderBy(p => p.PlayedSeconds).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Played, false) => query.OrderByDescending(p => p.PlayedSeconds).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (_, true) => query.OrderBy(p => p.StartedAt).ThenBy(p => p.Id),
        _ => query.OrderByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
    };

    public async Task<List<PlaybackUserResponse>> UsersAsync(CancellationToken ct) =>
        await db.MediaServerUsers.AsNoTracking()
            .OrderBy(u => u.DisplayName ?? u.Name)
            .Select(u => new PlaybackUserResponse(u.Id, u.DisplayName ?? u.Name, u.ThumbUrl, u.IsOwner, db.Plays.Count(p => p.UserId == u.Id), u.LastSeenAt, u.Name, u.KeepHistory))
            .ToListAsync(ct);

    /// <summary>The windows of the user page's watch time, in days; null is since the start.</summary>
    public static readonly int?[] WatchTimeWindows = [1, 7, 30, null];

    public async Task<PlaybackUserStatsResponse?> UserStatsAsync(int userId, CancellationToken ct)
    {
        var user = await db.MediaServerUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var plays = await db.Plays.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Id, p.StartedAt, p.StoppedAt, p.PlayedSeconds })
            .ToListAsync(ct);
        var watchTime = WatchTimeWindows
            .Select(days =>
            {
                var window = days is { } d ? plays.Where(p => p.StartedAt >= now.AddDays(-d)).ToList() : plays;
                return new WatchTimeResponse(days, window.Count, window.Sum(p => (long)p.PlayedSeconds));
            })
            .ToList();

        // One read of the streams gives the players and the decisions; the addresses are AddressesAsync's, with their places.
        var streams = await db.PlayStreams.AsNoTracking()
            .Where(s => s.Play.UserId == userId)
            .Select(s => new
            {
                s.PlayId,
                s.StartedAt,
                s.StoppedAt,
                s.PausedSeconds,
                Player = s.Details.PlayerTitle ?? s.Details.PlayerProduct,
                s.Details.PlayerPlatform,
                s.Details.Decision,
            })
            .ToListAsync(ct);

        var players = streams
            .GroupBy(s => (Player: s.Player ?? string.Empty, Platform: s.PlayerPlatform))
            .Select(g => new PlayerStatsResponse(
                g.Key.Player,
                g.Key.Platform,
                g.Select(s => s.PlayId).Distinct().Count(),
                g.Sum(s => Math.Max(0, (long)(s.StoppedAt - s.StartedAt).TotalSeconds - s.PausedSeconds)),
                g.Max(s => s.StoppedAt)))
            .OrderByDescending(p => p.Plays).ThenByDescending(p => p.LastPlayedAt)
            .Take(TopPlayers)
            .ToList();

        var decisions = streams.GroupBy(s => s.PlayId).Select(g => g.Max(s => s.Decision)).ToList();
        var counts = new DecisionCountsResponse(
            decisions.Count(d => d == StreamDecision.DirectPlay),
            decisions.Count(d => d == StreamDecision.DirectStream),
            decisions.Count(d => d == StreamDecision.Transcode),
            plays.Count - decisions.Count(d => d is not null));

        return new PlaybackUserStatsResponse(
            user.Id,
            user.Name,
            user.DisplayName,
            user.ThumbUrl,
            user.IsOwner,
            user.KeepHistory,
            user.FirstSeenAt,
            user.LastSeenAt,
            plays.Count == 0 ? null : plays.Max(p => p.StoppedAt),
            watchTime,
            players,
            counts);
    }

    /// <summary>How many players the user page lists, the most used first.</summary>
    public const int TopPlayers = 10;

    public async Task<Result<PlaybackUserStatsResponse>> UpdateUserAsync(int userId, UpdatePlaybackUserRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await db.MediaServerUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Failure.NotFound($"Media server user {userId}");
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (displayName is { Length: > 256 })
        {
            return Failure.Invalid("Display name too long", "At most 256 characters.");
        }

        user.DisplayName = displayName;
        user.KeepHistory = request.KeepHistory ?? user.KeepHistory;
        await db.SaveChangesAsync(ct);
        return Result<PlaybackUserStatsResponse>.Ok((await UserStatsAsync(userId, ct))!);
    }

    public async Task<List<VersionPlaysResponse>> MoviePlaysAsync(int movieId, CancellationToken ct)
    {
        var rows = await db.Plays.AsNoTracking()
            .Where(p => p.MovieId == movieId)
            .GroupBy(p => new { p.VersionId, p.VersionName })
            .Select(g => new VersionPlaysResponse(
                g.Key.VersionId,
                g.Key.VersionName,
                g.Count(),
                g.Select(p => p.UserId).Distinct().Count(),
                g.Count(p => p.Streams.Any(s => s.Details.Decision == StreamDecision.Transcode)),
                g.Max(p => (DateTime?)p.StoppedAt)))
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return rows;
        }

        // The film's versions nobody played are listed too: a 4K never played is what the table is for.
        var unplayed = await db.MovieVersions.AsNoTracking()
            .Where(v => v.MovieId == movieId)
            .Select(v => new { v.Id, v.Name })
            .ToListAsync(ct);
        rows.AddRange(unplayed.Where(v => rows.All(r => r.VersionId != v.Id)).Select(v => new VersionPlaysResponse(v.Id, v.Name, 0, 0, 0, null)));
        return rows.OrderBy(r => r.VersionId is null).ThenByDescending(r => r.Plays).ThenBy(r => r.VersionName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<PlaybackSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var settings = await db.PlaybackSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlaybackSettings.SingletonId, ct) ?? PlaybackSettings.Default();
        return new PlaybackSettingsDto(settings.MinimumPlay, settings.MinimumPlayUnit, settings.WatchedPercent);
    }

    public async Task<Result<PlaybackSettingsDto>> UpdateSettingsAsync(PlaybackSettingsDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.MinimumPlayUnit))
        {
            return Failure.Invalid("Unknown unit", "Expected seconds or percent.");
        }

        var max = request.MinimumPlayUnit == MinimumPlayUnit.Percent ? 100 : 3600;
        if (request.MinimumPlay < 0 || request.MinimumPlay > max)
        {
            return Failure.Invalid("Invalid minimum play time", $"Between 0 and {max}.");
        }

        if (request.WatchedPercent is < 1 or > 100)
        {
            return Failure.Invalid("Invalid watched threshold", "Between 1 and 100 %.");
        }

        var settings = await db.PlaybackSettings.FirstOrDefaultAsync(s => s.Id == PlaybackSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = PlaybackSettings.Default();
            db.PlaybackSettings.Add(settings);
        }

        settings.MinimumPlay = request.MinimumPlay;
        settings.MinimumPlayUnit = request.MinimumPlayUnit;
        settings.WatchedPercent = request.WatchedPercent;
        await db.SaveChangesAsync(ct);
        return Result<PlaybackSettingsDto>.Ok(request);
    }
}
