using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>Read side of playback (now playing, plays, users, plays per version) and the play rules.</summary>
public sealed class PlaybackService(BigarrDbContext db, PlaybackMonitor monitor)
{
    public const int MaxPageSize = 200;

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
                new PlaybackUserRef(s.User.Id, s.User.DisplayName ?? s.User.Name, s.User.ThumbUrl),
                new PlaybackItemRef(s.Item.Id, s.Item.Kind, s.Item.Title, s.Item.Year, s.Item.ShowTitle, s.Item.SeasonNumber, s.Item.EpisodeNumber, s.Item.DurationMs, s.Item.LibrarySectionTitle, s.Item.MovieId),
                s.State,
                s.ViewOffsetMs,
                s.StartedAt,
                s.PausedSeconds,
                s.VersionId,
                s.VersionName,
                s.Details))
            .ToListAsync(ct);
        return new NowPlayingResponse(statuses, sessions);
    }

    public async Task<PlaysPageResponse> PlaysAsync(int page, int pageSize, int? userId, int? movieId, int? versionId, MediaItemKind? kind, PlaySort sort, bool ascending, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Plays.AsNoTracking();
        if (userId is { } user)
        {
            query = query.Where(p => p.UserId == user);
        }

        if (movieId is { } movie)
        {
            query = query.Where(p => p.MovieId == movie);
        }

        if (versionId is { } version)
        {
            query = query.Where(p => p.VersionId == version);
        }

        if (kind is { } k)
        {
            query = query.Where(p => p.Item.Kind == k);
        }

        var total = await query.CountAsync(ct);
        var records = await Order(query, sort, ascending)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PlayResponse(
                p.Id,
                new PlaybackUserRef(p.User.Id, p.User.DisplayName ?? p.User.Name, p.User.ThumbUrl),
                new PlaybackItemRef(p.Item.Id, p.Item.Kind, p.Item.Title, p.Item.Year, p.Item.ShowTitle, p.Item.SeasonNumber, p.Item.EpisodeNumber, p.Item.DurationMs, p.Item.LibrarySectionTitle, p.Item.MovieId),
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

    /// <summary>The sessions a play was made of, oldest first, with everything the server said about each.</summary>
    public async Task<List<PlayStreamResponse>?> StreamsAsync(int playId, CancellationToken ct)
    {
        if (!await db.Plays.AnyAsync(p => p.Id == playId, ct))
        {
            return null;
        }

        return await db.PlayStreams.AsNoTracking()
            .Where(s => s.PlayId == playId)
            .OrderBy(s => s.StartedAt).ThenBy(s => s.Id)
            .Select(s => new PlayStreamResponse(s.Id, s.StartedAt, s.StoppedAt, s.StartOffsetMs, s.EndOffsetMs, s.PausedSeconds, s.FilePath, s.VersionId, s.Version != null ? s.Version.Name : null, s.Details))
            .ToListAsync(ct);
    }

    /// <summary>Newest first by default; ties keep a stable order by id.</summary>
    private static IOrderedQueryable<Play> Order(IQueryable<Play> query, PlaySort sort, bool ascending) => (sort, ascending) switch
    {
        (PlaySort.User, true) => query.OrderBy(p => p.User.DisplayName ?? p.User.Name).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.User, false) => query.OrderByDescending(p => p.User.DisplayName ?? p.User.Name).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Title, true) => query.OrderBy(p => p.Item.ShowTitle ?? p.Item.Title).ThenBy(p => p.Item.SeasonNumber).ThenBy(p => p.Item.EpisodeNumber).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
        (PlaySort.Title, false) => query.OrderByDescending(p => p.Item.ShowTitle ?? p.Item.Title).ThenByDescending(p => p.Item.SeasonNumber).ThenByDescending(p => p.Item.EpisodeNumber).ThenByDescending(p => p.StartedAt).ThenByDescending(p => p.Id),
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
            .Select(u => new PlaybackUserResponse(u.Id, u.DisplayName ?? u.Name, u.ThumbUrl, u.IsOwner, db.Plays.Count(p => p.UserId == u.Id), u.LastSeenAt))
            .ToListAsync(ct);

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
        return rows.OrderBy(r => r.VersionId is null).ThenByDescending(r => r.Plays).ToList();
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
