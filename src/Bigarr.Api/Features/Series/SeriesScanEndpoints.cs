namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

/// <summary>Series side of the library scan: rescan one series or every series root, list files, map unmapped folders.</summary>
public static class SeriesScanEndpoints
{
    public static IEndpointRouteBuilder MapSeriesScan(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/series").WithTags("Series").RequireAuthorization(AuthPolicies.Api);

        group.MapPost("/rescan", RescanAllAsync);
        group.MapPost("/{id:int}/rescan", RescanAsync);
        group.MapGet("/{id:int}/rescan", RescanStatusAsync);
        group.MapGet("/{id:int}/files", FilesAsync);
        group.MapPost("/unmapped/{unmappedId:int}/map", MapFolderAsync);

        return app;
    }

    /// <summary>Queues a scan of one series' version folders, run by the library scan runner.</summary>
    public static ScanStatusResponse EnqueueRescan(LibraryScanRunner scans, int seriesId) =>
        scans.EnqueueSeries(seriesId, (services, progress, ct) => services.GetRequiredService<SeriesLibraryScanner>().ScanSeriesAsync(seriesId, progress, ct));

    private static async Task<Ok<List<ScanStatusResponse>>> RescanAllAsync(BigarrDbContext db, LibraryScanRunner scans, CancellationToken ct)
    {
        var roots = await db.RootFolders.AsNoTracking().Where(r => r.Kind == MediaKind.Series).OrderBy(r => r.Path).Select(r => r.Id).ToListAsync(ct);
        return TypedResults.Ok(roots.Select(scans.Enqueue).ToList());
    }

    private static async Task<Results<Accepted<ScanStatusResponse>, NotFound>> RescanAsync(int id, BigarrDbContext db, LibraryScanRunner scans, CancellationToken ct)
    {
        if (!await db.Series.AnyAsync(s => s.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Accepted($"/api/v1/series/{id}/rescan", EnqueueRescan(scans, id));
    }

    private static async Task<Results<Ok<ScanStatusResponse>, NotFound>> RescanStatusAsync(int id, BigarrDbContext db, LibraryScanRunner scans, CancellationToken ct) =>
        await db.Series.AnyAsync(s => s.Id == id, ct) ? TypedResults.Ok(scans.SeriesStatus(id)) : TypedResults.NotFound();

    private static async Task<Results<Ok<SeriesFilesResponse>, NotFound>> FilesAsync(
        int id, BigarrDbContext db, CustomFormatScorer scorer, EpisodeFileFormatMatches matches, CancellationToken ct, int? versionId = null)
    {
        if (!await db.Series.AnyAsync(s => s.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var versions = await db.SeriesVersions.AsNoTracking().Include(v => v.RootFolder).Include(v => v.Series)
            .Include(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Where(v => v.SeriesId == id && (versionId == null || v.Id == versionId))
            .ToDictionaryAsync(v => v.Id, ct);
        var ids = versions.Keys.ToList();

        var files = await db.EpisodeFiles.AsNoTracking().Include(f => f.Quality)
            .Where(f => ids.Contains(f.VersionId))
            .ToListAsync(ct);
        var links = await db.EpisodeTargets.AsNoTracking()
            .Where(t => ids.Contains(t.VersionId) && t.EpisodeFileId != null)
            .Select(t => new { FileId = t.EpisodeFileId!.Value, t.EpisodeId, t.Episode.SeasonNumber, t.Episode.EpisodeNumber })
            .ToListAsync(ct);
        var byFile = links.ToLookup(l => l.FileId);

        var unmatched = await db.UnmatchedEpisodeFiles.AsNoTracking()
            .Where(u => ids.Contains(u.VersionId))
            .OrderBy(u => u.RelativePath)
            .ToListAsync(ct);

        string FolderOf(int vid) => Path.Combine(versions[vid].RootFolder.Path, versions[vid].FolderName);

        // Custom formats as the cutoff and the Wanted list see them: matched on the file, scored by its version's profile.
        var (calculator, signature, names) = files.Count == 0
            ? default
            : (await scorer.CalculatorAsync(ct), await scorer.SignatureAsync(ct), await scorer.NamesAsync(ct));
        (List<string> Names, int Score) FormatsOf(EpisodeFile f)
        {
            var version = versions[f.VersionId];
            var row = new EpisodeFileRow(f.Id, f.QualityId, f.OriginalReleaseTitle, f.RelativePath, f.Size, f.IndexerFlags, f.Languages, f.ReleaseGroup, version.Series.Year, version.Series.OriginalLanguage);
            var ids = matches.Match(row, f.Quality, calculator!, signature!);
            return (ids.Select(i => names!.GetValueOrDefault(i)).OfType<string>().ToList(), ProfileRanking.FormatScore(version.QualityProfile, ids));
        }

        var fileResponses = files
            .Select(f =>
            {
                var episodes = byFile[f.Id].OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
                var (formats, score) = FormatsOf(f);
                return new EpisodeFileResponse(
                    f.Id,
                    f.VersionId,
                    f.RelativePath,
                    Path.Combine(FolderOf(f.VersionId), f.RelativePath),
                    f.SeasonNumber,
                    f.Size,
                    f.DateAdded,
                    f.QualityId,
                    f.Quality.Name,
                    f.Revision.Version,
                    f.Revision.IsRepack,
                    f.ReleaseGroup,
                    f.Languages,
                    f.OriginalReleaseTitle,
                    f.MediaInfo,
                    episodes.Select(e => e.EpisodeId).ToList(),
                    Label(episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).ToList()),
                    f.LinkedFromPath,
                    f.Quality.Resolution,
                    f.IndexerFlags,
                    formats,
                    score);
            })
            .OrderBy(f => f.SeasonNumber).ThenBy(f => f.Episodes, StringComparer.Ordinal).ThenBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        var unmatchedResponses = unmatched
            .Select(u => new UnmatchedEpisodeFileResponse(u.Id, u.VersionId, u.RelativePath, Path.Combine(FolderOf(u.VersionId), u.RelativePath), u.Size, u.Reason, u.Detail, u.DiscoveredAt, u.LastSeenAt))
            .ToList();

        return TypedResults.Ok(new SeriesFilesResponse(fileResponses, unmatchedResponses));
    }

    private static async Task<Results<Ok<MapSeriesFolderResponse>, ProblemHttpResult>> MapFolderAsync(
        int unmappedId, MapSeriesFolderRequest request, SeriesLibraryScanner scanner, LibraryScanRunner scans, CancellationToken ct)
    {
        var result = await scanner.MapFolderAsync(unmappedId, request, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var (seriesId, versionId) = result.Value;
        return TypedResults.Ok(new MapSeriesFolderResponse(seriesId, versionId, EnqueueRescan(scans, seriesId)));
    }

    /// <summary>"S01E01", "S01E02-E03" (first and last, Sonarr-style), "S21E194-S22E01" across seasons.</summary>
    public static string Label(IReadOnlyList<(int Season, int Episode)> episodes)
    {
        if (episodes.Count == 0)
        {
            return string.Empty;
        }

        var first = episodes[0];
        var label = $"S{first.Season:00}E{first.Episode:00}";
        var last = episodes[^1];
        return episodes.Count == 1 ? label
            : last.Season == first.Season ? $"{label}-E{last.Episode:00}"
            : $"{label}-S{last.Season:00}E{last.Episode:00}";
    }
}
