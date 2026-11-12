using System.Globalization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Import;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>
/// Renames a series' episode files to the current templates (Sonarr's "Preview Rename"): per series, version or season,
/// preview first, then the moves, journaled per episode as <see cref="HistoryEventType.Renamed"/>. Extras follow their file.
/// The version folder itself never moves.
/// </summary>
public sealed class SeriesRenameService(BigarrDbContext db, MediaManagementService settings, CustomFormatScorer scorer, Infrastructure.Events.OutboxWriter outbox, TimeProvider time, ILogger<SeriesRenameService> logger)
{
    /// <summary>Files moved since the last <see cref="PublishRenamed"/>, for Sonarr's Rename event (one per series version).</summary>
    private readonly List<(Series Series, SeriesVersion Version, EpisodeFile File, string PreviousRelativePath)> _renamed = [];

    /// <summary>Notes a file moved outside <see cref="Move"/> (the order undo), for the next <see cref="PublishRenamed"/>.</summary>
    public void Record(Series series, SeriesVersion version, EpisodeFile file, string previousRelativePath) => _renamed.Add((series, version, file, previousRelativePath));

    /// <summary>
    /// One <see cref="Bigarr.Domain.Events.EpisodeFileRenamed"/> per series version for the files moved since the last
    /// call, added to the unit of work: the caller's save commits it with the moves.
    /// </summary>
    public void PublishRenamed()
    {
        foreach (var group in _renamed.GroupBy(r => r.Version.Id))
        {
            var (series, version, _, _) = group.First();
            var folder = Folder(version);
            outbox.Publish(new Bigarr.Domain.Events.EpisodeFileRenamed(
                Bigarr.Domain.Events.SeriesRef.From(series),
                Bigarr.Domain.Events.SeriesVersionRef.From(version),
                group.Select(r => new Bigarr.Domain.Events.RenamedFileRef(
                    Infrastructure.Events.EventSnapshots.EpisodeFile(r.File, Path.Combine(folder, r.File.RelativePath)),
                    Path.Combine(folder, r.PreviousRelativePath),
                    r.PreviousRelativePath)).ToList())
            { OccurredAt = time.GetUtcNow().UtcDateTime });
        }

        _renamed.Clear();
    }

    public async Task<Result<List<SeriesRenamePreviewResponse>>> PreviewAsync(int seriesId, int? versionId, int? seasonNumber, CancellationToken ct)
    {
        var series = await LoadAsync(seriesId, tracking: false, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        var config = await settings.GetAsync(ct);
        var rows = new List<SeriesRenamePreviewResponse>();
        foreach (var (version, file) in Files(series, versionId, seasonNumber, null))
        {
            var target = await TargetPathAsync(config, series, version, file, ct);
            var changed = !string.Equals(target, file.RelativePath, StringComparison.Ordinal);
            var folder = Folder(version);
            var extras = changed
                ? Extras(file).Zip(ExtraTargets(file, folder, target), (extra, to) => new ExtraRenamePreviewResponse(extra.Id, extra.RelativePath, to)).ToList()
                : Extras(file).Select(extra => new ExtraRenamePreviewResponse(extra.Id, extra.RelativePath, extra.RelativePath)).ToList();
            rows.Add(new SeriesRenamePreviewResponse(version.Id, version.Name, file.Id, file.SeasonNumber, Labels(file), file.RelativePath, target, changed, extras));
        }

        return Result<List<SeriesRenamePreviewResponse>>.Ok(rows);
    }

    public async Task<Result<List<SeriesRenameResultResponse>>> RenameAsync(SeriesRenameRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var turn = await SeriesFileGate.EnterAsync(request.SeriesId, ct);
        var series = await LoadAsync(request.SeriesId, tracking: true, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        // Every target is worked out before the first move: from there on nothing waits on the request any more, so a
        // caller that went away cannot leave files renamed on disk and the old names in the database.
        var config = await settings.GetAsync(ct);
        var planned = new List<(SeriesVersion Version, EpisodeFile File, string Target)>();
        foreach (var (version, file) in Files(series, request.VersionId, request.SeasonNumber, request.EpisodeFileIds?.ToHashSet()))
        {
            planned.Add((version, file, await TargetPathAsync(config, series, version, file, ct)));
        }

        var results = new List<SeriesRenameResultResponse>();
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var (version, file, target) in planned)
        {
            results.Add(Move(series, version, file, target, now));
        }

        PublishRenamed();
        await db.SaveChangesAsync(CancellationToken.None);
        foreach (var version in series.Versions)
        {
            RemoveEmptySeasonFolders(Folder(version));
        }

        return Result<List<SeriesRenameResultResponse>>.Ok(results);
    }

    /// <summary>The path relative to the version folder the templates give this file today (renaming forced on unless <paramref name="rename"/> says otherwise), extension kept.</summary>
    public async Task<string> TargetPathAsync(MediaManagementConfig config, Series series, SeriesVersion version, EpisodeFile file, CancellationToken ct, bool rename = true)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(file);

        var calculator = await scorer.CalculatorAsync(ct);
        var matched = calculator.Match(CustomFormatScorer.EpisodeFileInput(
            file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.ReleaseGroup, series.Year, series.OriginalLanguage));
        var facts = new EpisodeFileFacts(
            file.Quality?.Name ?? $"#{file.QualityId}",
            file.Revision.Version,
            file.Revision.Real,
            file.Revision.IsRepack,
            file.ReleaseGroup,
            file.Languages,
            file.MediaInfo,
            matched.Where(m => m.IncludeWhenRenaming).Select(m => m.Name).ToList(),
            file.OriginalReleaseTitle,
            Path.GetFileNameWithoutExtension(file.RelativePath));
        var episodes = file.Episodes.Select(t => t.Episode).ToList();
        var extension = Path.GetExtension(file.RelativePath).ToLowerInvariant();
        return SeriesNaming.RelativePath(config, series, version, episodes, facts, extension, Path.GetFileName(file.RelativePath), rename);
    }

    /// <summary>Moves one file (and its extras) to <paramref name="target"/>, journaled; the caller saves.</summary>
    public SeriesRenameResultResponse Move(Series series, SeriesVersion version, EpisodeFile file, string target, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(file);

        if (string.Equals(target, file.RelativePath, StringComparison.Ordinal))
        {
            return new SeriesRenameResultResponse(file.Id, file.RelativePath, false, null);
        }

        var folder = Folder(version);
        var from = Path.Combine(folder, file.RelativePath);
        var to = Path.Combine(folder, target);
        var extraTargets = ExtraTargets(file, folder, target);
        try
        {
            if (!File.Exists(from))
            {
                return new SeriesRenameResultResponse(file.Id, file.RelativePath, false, "The file is missing on disk.");
            }

            if (File.Exists(to) && !SeriesImportService.PathsEqual(from, to))
            {
                return new SeriesRenameResultResponse(file.Id, file.RelativePath, false, $"A file already exists at {to}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Rename of {From} failed", from);
            return new SeriesRenameResultResponse(file.Id, file.RelativePath, false, ex.Message);
        }

        var previous = file.RelativePath;
        file.RelativePath = target;
        _renamed.Add((series, version, file, previous));
        var extrasRenamed = RenameExtras(file, folder, extraTargets);
        foreach (var episodeTarget in file.Episodes)
        {
            var line = GrabService.SeriesHistory(version, episodeTarget, HistoryEventType.Renamed, file.OriginalReleaseTitle, null, null, now);
            line.Data["from"] = from;
            line.Data["to"] = to;
            line.Data["episodeFileId"] = file.Id.ToString(CultureInfo.InvariantCulture);
            if (extrasRenamed > 0)
            {
                line.Data["extras"] = extrasRenamed.ToString(CultureInfo.InvariantCulture);
            }

            db.History.Add(line);
        }

        logger.LogInformation("Renamed {From} → {To}", previous, target);
        return new SeriesRenameResultResponse(file.Id, target, true, null);
    }

    private static IEnumerable<(SeriesVersion Version, EpisodeFile File)> Files(Series series, int? versionId, int? seasonNumber, HashSet<int>? fileIds) =>
        series.Versions
            .Where(v => versionId is null || v.Id == versionId)
            .OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id)
            .SelectMany(v => v.Files
                .Where(f => f.Episodes.Count > 0 && (seasonNumber is null || f.SeasonNumber == seasonNumber) && (fileIds is null || fileIds.Contains(f.Id)))
                .OrderBy(f => f.SeasonNumber).ThenBy(f => f.Episodes.Min(t => t.Episode.EpisodeNumber))
                .Select(f => (v, f)));

    private static List<string> Labels(EpisodeFile file) =>
        file.Episodes.Select(t => t.Episode).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(GrabService.EpisodeLabel).ToList();

    public static string Folder(SeriesVersion version) => Path.Combine(version.RootFolder.Path, version.FolderName);

    private static IEnumerable<EpisodeExtraFile> Extras(EpisodeFile file) => file.Extras.OrderBy(e => e.RelativePath, StringComparer.Ordinal);

    private static List<string> ExtraTargets(EpisodeFile file, string folder, string target)
    {
        var extras = Extras(file).ToList();
        if (extras.Count == 0)
        {
            return [];
        }

        var own = extras.Select(e => Path.GetFullPath(Path.Combine(folder, e.RelativePath))).ToHashSet(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var destination = Path.GetFullPath(Path.Combine(folder, target));
        var names = extras.Select(e => new ExtraFileName(Path.ChangeExtension(e.RelativePath, null), e.Extension, e.Kind, e.Language, e.Forced, e.HearingImpaired)).ToList();
        return ExtraFiles.Destinations(names, Path.GetDirectoryName(destination)!, Path.GetFileNameWithoutExtension(destination), p => File.Exists(p) && !own.Contains(Path.GetFullPath(p)))
            .Select(p => Path.GetRelativePath(Path.GetFullPath(folder), p))
            .ToList();
    }

    private int RenameExtras(EpisodeFile file, string folder, List<string> targets)
    {
        var renamed = 0;
        foreach (var (extra, target) in Extras(file).ToList().Zip(targets))
        {
            if (string.Equals(extra.RelativePath, target, StringComparison.Ordinal))
            {
                continue;
            }

            var from = Path.Combine(folder, extra.RelativePath);
            var to = Path.Combine(folder, target);
            try
            {
                if (!File.Exists(from) || File.Exists(to))
                {
                    logger.LogWarning("Extra file {From} not renamed: {Reason}", from, File.Exists(from) ? $"{to} already exists" : "missing on disk");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Rename of extra file {From} failed", from);
                continue;
            }

            extra.RelativePath = target;
            renamed++;
        }

        return renamed;
    }

    /// <summary>A season folder emptied by the renames goes (only empty ones, never the version folder).</summary>
    public static void RemoveEmptySeasonFolders(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
                // still in use: leave it
            }
        }
    }

    private Task<Series?> LoadAsync(int seriesId, bool tracking, CancellationToken ct)
    {
        var query = db.Series
            .Include(s => s.Versions).ThenInclude(v => v.RootFolder)
            .Include(s => s.Versions).ThenInclude(v => v.Files).ThenInclude(f => f.Quality)
            .Include(s => s.Versions).ThenInclude(v => v.Files).ThenInclude(f => f.Extras)
            .Include(s => s.Versions).ThenInclude(v => v.Files).ThenInclude(f => f.Episodes).ThenInclude(t => t.Episode)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
    }
}
