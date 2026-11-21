using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Notifications.Discord;

/// <param name="EventType">The event the message is for.</param>
/// <param name="Payload">The body a delivery would post to Discord, cut to Discord's limits.</param>
public sealed record DiscordPreviewResponse(string EventType, DiscordPayload Payload);

/// <param name="Discord">The form's settings, saved or not; Radarr's defaults for what is left out.</param>
public sealed record DiscordPreviewRequest(DiscordSettings? Discord = null);

/// <summary>
/// The Discord messages the form's settings would post, built around the film and the episode most recently put on
/// disk, so the preview shows the library's own posters, qualities and release names. Read only; nothing is posted.
/// </summary>
public sealed class DiscordPreviewService(BigarrDbContext db, IOptions<NotificationsOptions> options, TimeProvider time)
{
    public async Task<List<DiscordPreviewResponse>> PreviewAsync(DiscordPreviewRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var film = await FilmAsync(ct);
        var series = await SeriesAsync(ct);
        return DiscordPreview.All(film, series, request.Discord, now, options.Value.ApplicationUrl)
            .Select(p => new DiscordPreviewResponse(p.EventType, p.Payload))
            .ToList();
    }

    private async Task<PreviewFilm?> FilmAsync(CancellationToken ct)
    {
        var file = await db.MediaFiles.AsNoTracking()
            .Include(f => f.Quality)
            .Include(f => f.Version).ThenInclude(v => v.Movie)
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Include(f => f.Version).ThenInclude(v => v.Tags)
            .Include(f => f.Version).ThenInclude(v => v.QualityProfile)
            .OrderByDescending(f => f.DateAdded).ThenByDescending(f => f.Id)
            .FirstOrDefaultAsync(ct);
        if (file is null)
        {
            return null;
        }

        var version = file.Version;
        version.File = file;
        var versionRef = VersionRef.From(version);
        var fileRef = FileRef.From(file, versionRef.FilePath ?? file.RelativePath);
        var indexer = await db.History.AsNoTracking()
            .Where(h => h.MovieId == version.MovieId && h.IndexerName != null)
            .OrderByDescending(h => h.Id)
            .Select(h => h.IndexerName)
            .FirstOrDefaultAsync(ct);
        var other = await db.MovieVersions.AsNoTracking()
            .Include(v => v.QualityProfile)
            .Where(v => v.MovieId == version.MovieId && v.Id != version.Id)
            .OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id)
            .FirstOrDefaultAsync(ct);
        return new PreviewFilm(MovieRef.From(version.Movie), versionRef, fileRef, DiscordPreview.ReleaseOf(fileRef, indexer), other is null ? null : VersionRef.From(other));
    }

    private async Task<PreviewSeries?> SeriesAsync(CancellationToken ct)
    {
        var file = await db.EpisodeFiles.AsNoTracking()
            .Include(f => f.Quality)
            .Include(f => f.Episodes).ThenInclude(t => t.Episode)
            .Include(f => f.Version).ThenInclude(v => v.Series)
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Include(f => f.Version).ThenInclude(v => v.Tags)
            .Include(f => f.Version).ThenInclude(v => v.QualityProfile)
            .Where(f => f.Episodes.Any())
            .OrderByDescending(f => f.DateAdded).ThenByDescending(f => f.Id)
            .FirstOrDefaultAsync(ct);
        if (file is null)
        {
            return null;
        }

        var versionRef = SeriesVersionRef.From(file.Version);
        var path = versionRef.Folder is null ? file.RelativePath : Path.Combine(versionRef.Folder, file.RelativePath);
        var fileRef = EventSnapshots.EpisodeFile(file, path);
        var indexer = await db.History.AsNoTracking()
            .Where(h => h.SeriesId == file.Version.SeriesId && h.IndexerName != null)
            .OrderByDescending(h => h.Id)
            .Select(h => h.IndexerName)
            .FirstOrDefaultAsync(ct);
        return new PreviewSeries(SeriesRef.From(file.Version.Series), versionRef, EpisodeRef.From(file.Episodes.Select(t => t.Episode)), fileRef, DiscordPreview.ReleaseOf(fileRef, indexer));
    }
}
