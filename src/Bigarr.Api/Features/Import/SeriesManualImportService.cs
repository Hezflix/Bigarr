using System.Globalization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Decision.Specifications;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;
using Bigarr.Parsing.Episodes.Model;
using Bigarr.Parsing.Qualities;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Import;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>
/// Manual import for series (Sonarr's Interactive Import): list the video files of a folder or of a queued download with
/// the mapper's proposal (series, version, episodes, quality, why not), let the user assign series, version and episodes
/// per file, then import forced. Also: attach a file the scan could not place to episodes where it lies, and the episode
/// file actions (info, delete through the recycle bin).
/// </summary>
public sealed class SeriesManualImportService(
    BigarrDbContext db,
    SeriesImportService importer,
    SeriesRenameService renamer,
    MediaManagementService settings,
    DownloadClientResolver resolver,
    SeriesTargetsFactory targetsFactory,
    CustomFormatScorer scorer,
    RecycleBin recycleBin,
    QueueState queue,
    OutboxWriter outbox,
    TimeProvider time,
    ILogger<SeriesManualImportService> logger)
{
    public async Task<Result<SeriesManualImportResponse>> ListAsync(string? path, int? grabId, int? seriesId, CancellationToken ct)
    {
        Grab? grab = null;
        List<int> grabbedEpisodeIds = [];
        int? grabbedVersionId = null;
        if (grabId is not null)
        {
            grab = await db.Grabs.AsNoTracking().Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct);
            if (grab is null)
            {
                return Failure.NotFound("Queue item");
            }

            var targetIds = grab.Targets.Select(t => t.TargetId).ToList();
            var grabbed = await db.EpisodeTargets.AsNoTracking().Where(t => targetIds.Contains(t.Id)).ToListAsync(ct);
            grabbedEpisodeIds = grabbed.Select(t => t.EpisodeId).ToList();
            grabbedVersionId = grabbed.FirstOrDefault()?.VersionId;

            if (string.IsNullOrWhiteSpace(path))
            {
                var located = await LocateAsync(grab, ct);
                if (!located.IsSuccess)
                {
                    return located.Failure!;
                }

                path = located.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return Failure.Invalid("A path is required", "Give an absolute path, or the id of a queued download.");
        }

        path = Path.GetFullPath(path.Trim());
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return Failure.Invalid("Path not found", $"'{path}' does not exist or is not accessible from Bigarr.");
        }

        var config = await settings.GetAsync(ct);
        var index = await targetsFactory.IndexAsync(ct);
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var calculator = await scorer.CalculatorAsync(ct);
        var extraExtensions = config.ImportExtraFiles ? ExtraFiles.ParseExtensions(config.ExtraFileExtensions) : null;
        var listingRoot = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        var releaseTitle = grab?.ReleaseTitle;
        var releaseParsed = releaseTitle is null ? null : SeriesImportService.TryParse(releaseTitle);
        var forcedSeriesId = grabbedVersionId is { } gv
            ? await db.SeriesVersions.Where(v => v.Id == gv).Select(v => (int?)v.SeriesId).FirstOrDefaultAsync(ct)
            : seriesId;

        // Every video, samples included (flagged): the user may need one.
        var files = File.Exists(path)
            ? [new FileInfo(path)]
            : new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => VideoFiles.IsVideo(f.Name) && !f.Name.EndsWith(".bigarr-partial", StringComparison.Ordinal))
                .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        var realFiles = files.Count(f => !VideoFiles.LooksLikeSample(f.Name));

        var cache = new Dictionary<int, SeriesTargets?>();
        var rows = new List<SeriesManualImportFileResponse>();
        foreach (var file in files)
        {
            var parsedFile = SeriesImportService.TryParsePath(file.FullName);
            var quality = SeriesImportService.QualityOf(releaseParsed?.Quality, parsedFile?.Quality);
            var qualityName = definitions.GetValueOrDefault(quality.Quality.Id)?.Name ?? quality.Quality.Name;
            var group = SeriesImportService.Group(parsedFile) ?? SeriesImportService.Group(releaseParsed);
            var languages = SeriesImportService.LanguagesOf(parsedFile) is { Count: > 0 } l ? l : SeriesImportService.LanguagesOf(releaseParsed);

            int? showId = forcedSeriesId;
            string? reason = null;
            int? sceneSeason = null;
            if (showId is null)
            {
                var probe = parsedFile ?? releaseParsed;
                var match = probe is null ? SeriesMatch.NotFound : index.Find(probe);
                if (match.Series is null && releaseParsed is not null && !ReferenceEquals(probe, releaseParsed))
                {
                    match = index.Find(releaseParsed);
                }

                showId = match.Series?.Id;
                sceneSeason = match.SceneSeasonNumber;
                reason = match switch
                {
                    { Series: not null } => null,
                    { IsAmbiguous: true } => $"Several series match: {string.Join(", ", match.Ambiguous.Select(s => s.Title))}.",
                    _ when probe is null => "The name says no series or episode.",
                    _ => $"No series in the library matches '{probe.SeriesTitle}'.",
                };
            }

            SeriesTargets? show = null;
            if (showId is { } sid)
            {
                if (!cache.TryGetValue(sid, out show))
                {
                    show = await targetsFactory.LoadFilesOnlyAsync(sid, ct);
                    cache[sid] = show;
                }
            }

            var versions = show?.Versions.Select(v => new SeriesManualImportVersion(v.Id, v.Name, v.IsDefault)).ToList() ?? [];
            int? versionId = grabbedVersionId ?? show?.Versions.FirstOrDefault()?.Id;
            List<Episode> proposed = [];
            var rejections = new List<string>();
            if (show is not null)
            {
                var mapping = EpisodeMapper.Map(parsedFile, show.Mappable, show.Episodes.ToList(), sceneSource: true, sceneSeason);
                if (mapping.IsMapped && parsedFile is not { FullSeason: true })
                {
                    proposed = mapping.Episodes.ToList();
                }
                else if (realFiles == 1 && grabbedEpisodeIds.Count > 0 && !VideoFiles.LooksLikeSample(file.Name))
                {
                    proposed = show.Episodes.Where(e => grabbedEpisodeIds.Contains(e.Id)).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
                }
                else
                {
                    reason = mapping.Detail ?? "No episode in the name.";
                }

                if (proposed.Count > 0 && versionId is { } vid && show.ContextsFor(proposed, vid).FirstOrDefault() is { } context)
                {
                    var formats = calculator.Match(CustomFormatScorer.EpisodeFileInput(releaseTitle ?? file.Name, file.Name, file.Length, grab?.IndexerFlags ?? IndexerFlags.None, definitions.GetValueOrDefault(quality.Quality.Id), languages, group, show.Series.Year, show.Series.OriginalLanguage));
                    var candidate = new ReleaseCandidate(
                        new ReleaseInfo { Guid = file.FullName, Title = releaseTitle ?? file.Name, IndexerName = "manual import", Size = file.Length },
                        new ParsedEpisodeInfo { ReleaseTitle = file.Name, Quality = quality, ReleaseGroup = group ?? string.Empty },
                        0)
                    {
                        CustomFormatIds = formats.Select(f => f.Id).ToList(),
                    };
                    var decision = ImportService.SeriesEvaluator.Evaluate(candidate, context);
                    rejections.AddRange(decision.Rejections.Select(r => r.Reason));
                }
            }

            var hasFile = show is null || versionId is null
                ? new HashSet<int>()
                : proposed.Where(e => show.Slot(versionId.Value, e.Id)?.File is not null).Select(e => e.Id).ToHashSet();
            rows.Add(new SeriesManualImportFileResponse(
                file.FullName,
                File.Exists(path) ? file.Name : Path.GetRelativePath(path, file.FullName),
                file.Name,
                file.Length,
                VideoFiles.LooksLikeSample(file.Name),
                quality.Quality.Id,
                qualityName,
                quality.Revision?.Version ?? 1,
                group,
                languages,
                show?.Series.Id,
                show?.Series.Title,
                versionId,
                versions,
                proposed.FirstOrDefault()?.SeasonNumber ?? parsedFile?.SeasonNumbers.FirstOrDefault(),
                proposed.Select(e => new SeriesManualImportEpisode(e.Id, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteEpisodeNumber, e.Title, hasFile.Contains(e.Id))).ToList(),
                proposed.Count > 0 ? null : reason,
                rejections,
                ListExtras(file.FullName, grab is null ? null : path, extraExtensions, listingRoot)));
        }

        return Result<SeriesManualImportResponse>.Ok(new SeriesManualImportResponse(path, grab?.Id, releaseTitle, config.TransferMode, rows));
    }

    public async Task<Result<List<SeriesManualImportResultResponse>>> ImportAsync(SeriesManualImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Files is null || request.Files.Count == 0)
        {
            return Failure.Invalid("Nothing to import", "Pick at least one file.");
        }

        var config = await settings.GetAsync(ct);
        var mode = request.TransferMode ?? config.TransferMode;
        var results = new List<SeriesManualImportResultResponse>();
        var touchedGrabs = new HashSet<int>();
        foreach (var item in request.Files)
        {
            var result = await ImportOneAsync(item, mode, ct);
            results.Add(result);
            if (result.Imported && item.GrabId is { } grabId)
            {
                touchedGrabs.Add(grabId);
            }

            db.ChangeTracker.Clear();
        }

        // The queue item the files came from is done once something of it went in, like Sonarr's manual import.
        foreach (var grabId in touchedGrabs)
        {
            var grab = await db.Grabs.Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct);
            if (grab is not null && !grab.State.IsTerminal())
            {
                grab.StatusMessages.RemoveAll(m => m.Title == ImportService.FailedMessageTitle);
                grab.TransitionTo(GrabState.Imported);
                await db.SaveChangesAsync(ct);
            }
        }

        queue.RequestRefresh();
        return Result<List<SeriesManualImportResultResponse>>.Ok(results);
    }

    private async Task<SeriesManualImportResultResponse> ImportOneAsync(SeriesManualImportItemRequest item, TransferMode mode, CancellationToken ct)
    {
        SeriesManualImportResultResponse Fail(string reason) => new(item.Path, item.VersionId, false, reason, null, null, false, []);

        if (string.IsNullOrWhiteSpace(item.Path) || !Path.IsPathRooted(item.Path) || !File.Exists(item.Path))
        {
            return Fail("File not found.");
        }

        if (item.EpisodeIds is not { Count: > 0 })
        {
            return Fail("Choose the episodes this file holds.");
        }

        var version = await db.SeriesVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == item.VersionId, ct);
        if (version is null)
        {
            return Fail("Series version not found.");
        }

        var count = await db.Episodes.CountAsync(e => e.SeriesId == version.SeriesId && item.EpisodeIds.Contains(e.Id), ct);
        if (count != item.EpisodeIds.Distinct().Count())
        {
            return Fail("Some episodes are not episodes of this series.");
        }

        if (await FilmOwnedAsync(item.EpisodeIds, ct) is { } owned)
        {
            return Fail(owned);
        }

        Grab? grab = null;
        if (item.GrabId is not null)
        {
            grab = await db.Grabs.Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == item.GrabId, ct);
            if (grab is null)
            {
                return Fail("Queue item not found.");
            }
        }

        var releaseParsed = grab is null ? null : SeriesImportService.TryParse(grab.ReleaseTitle);
        var parsedFile = SeriesImportService.TryParsePath(item.Path);
        var quality = SeriesImportService.QualityOf(releaseParsed?.Quality, parsedFile?.Quality);
        if (item.QualityId is { } qualityId)
        {
            var picked = ParserQuality.All.FirstOrDefault(q => q.Id == qualityId);
            if (picked is null)
            {
                return Fail($"Unknown quality {qualityId}.");
            }

            quality = new QualityModel(picked, quality.Revision);
        }

        var plan = new SeriesImportPlan(
            item.Path,
            version.Id,
            item.EpisodeIds.Distinct().ToList(),
            grab?.ReleaseTitle ?? Path.GetFileNameWithoutExtension(item.Path),
            quality,
            SeriesImportService.Group(parsedFile) ?? SeriesImportService.Group(releaseParsed),
            SeriesImportService.LanguagesOf(parsedFile) is { Count: > 0 } l ? l : SeriesImportService.LanguagesOf(releaseParsed),
            grab?.IndexerFlags ?? IndexerFlags.None,
            grab,
            Force: true,
            mode,
            ExtraPaths: item.ExtraPaths);
        var outcome = await importer.ExecuteGuardedAsync(plan, null, ct);
        if (outcome.Status == SeriesFileStatus.Imported)
        {
            logger.LogInformation("Manually imported {Path} → {Destination} ({Episodes})", item.Path, outcome.DestinationPath, string.Join(", ", outcome.Episodes));
        }

        return new SeriesManualImportResultResponse(item.Path, item.VersionId, outcome.Status == SeriesFileStatus.Imported, outcome.Reason, outcome.EpisodeFileId, outcome.DestinationPath, outcome.Upgraded, [.. outcome.Episodes]);
    }

    /// <summary>
    /// A file the scan listed as unmatched becomes the episodes' file where it lies (Sonarr's manual import on a series
    /// folder). Nothing moves unless <see cref="AssignUnmatchedRequest.Rename"/>; an episode that already has a file is
    /// refused unless <see cref="AssignUnmatchedRequest.Replace"/> (the old file goes to the recycle bin).
    /// </summary>
    public async Task<Result<EpisodeFileDetailResponse>> AssignUnmatchedAsync(int seriesId, int unmatchedId, AssignUnmatchedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var unmatched = await db.UnmatchedEpisodeFiles.FirstOrDefaultAsync(u => u.Id == unmatchedId && u.SeriesId == seriesId, ct);
        if (unmatched is null)
        {
            return Failure.NotFound("Unmatched file");
        }

        if (request.EpisodeIds is not { Count: > 0 })
        {
            return Failure.Invalid("No episode", "Choose the episodes this file holds.");
        }

        var version = await db.SeriesVersions.Include(v => v.RootFolder).Include(v => v.Series).FirstAsync(v => v.Id == unmatched.VersionId, ct);
        var folder = SeriesRenameService.Folder(version);
        var path = Path.Combine(folder, unmatched.RelativePath);
        if (!File.Exists(path))
        {
            return Failure.Invalid("File not found", $"{path} is gone; scan the series again.");
        }

        var ids = request.EpisodeIds.Distinct().ToList();
        var episodes = await db.Episodes.Where(e => e.SeriesId == seriesId && ids.Contains(e.Id)).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToListAsync(ct);
        if (episodes.Count != ids.Count)
        {
            return Failure.Invalid("Unknown episodes", "Some episodes are not episodes of this series.");
        }

        if (await FilmOwnedAsync(ids, ct) is { } ownedByFilm)
        {
            return Failure.Conflict("Owned by a film", ownedByFilm);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var targets = await db.EpisodeTargets.Include(t => t.EpisodeFile!).ThenInclude(f => f.Extras)
            .Where(t => t.VersionId == version.Id && ids.Contains(t.EpisodeId)).ToListAsync(ct);
        foreach (var episode in episodes.Where(e => targets.All(t => t.EpisodeId != e.Id)))
        {
            var created = new EpisodeTarget { VersionId = version.Id, EpisodeId = episode.Id, Monitored = false, CreatedAt = now };
            db.EpisodeTargets.Add(created);
            targets.Add(created);
        }

        foreach (var target in targets)
        {
            target.Episode = episodes.First(e => e.Id == target.EpisodeId);
            target.Version = version;
        }

        var held = targets.Where(t => t.EpisodeFile is not null).Select(t => t.EpisodeFile!).DistinctBy(f => f.Id).ToList();
        if (held.Count > 0 && !request.Replace)
        {
            var taken = targets.Where(t => t.EpisodeFile is not null).Select(t => $"{GrabService.EpisodeLabel(t.Episode)} has {t.EpisodeFile!.RelativePath}");
            return Failure.Conflict("Episode already has a file", string.Join("; ", taken) + ". Replace it, or delete that file first.");
        }

        var config = await settings.GetAsync(ct);
        var parsed = SeriesImportService.TryParsePath(path);
        var quality = parsed?.Quality ?? new QualityModel(ParserQuality.Unknown);
        if (request.QualityId is { } qualityId)
        {
            var picked = ParserQuality.All.FirstOrDefault(q => q.Id == qualityId);
            if (picked is null)
            {
                return Failure.Invalid("Unknown quality", $"No quality {qualityId}.");
            }

            quality = new QualityModel(picked, quality.Revision);
        }

        var definitions = await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct);
        foreach (var old in held)
        {
            var others = await db.EpisodeTargets.Where(t => t.EpisodeFileId == old.Id).ToListAsync(ct);
            foreach (var other in others)
            {
                other.EpisodeFileId = null;
                other.EpisodeFile = null;
            }

            try
            {
                recycleBin.RemoveFile(Path.Combine(folder, old.RelativePath), config.RecycleBinPath, version.RootFolder.Path);
                foreach (var extra in old.Extras)
                {
                    recycleBin.RemoveFile(Path.Combine(folder, extra.RelativePath), config.RecycleBinPath, version.RootFolder.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new Failure(StatusCodes.Status500InternalServerError, "Could not remove the replaced file", ex.Message);
            }

            db.EpisodeFiles.Remove(old);
        }

        var file = new EpisodeFile
        {
            VersionId = version.Id,
            RelativePath = unmatched.RelativePath,
            SeasonNumber = episodes[0].SeasonNumber,
            Size = new FileInfo(path).Length,
            DateAdded = now,
            QualityId = definitions.Contains(quality.Quality.Id) ? quality.Quality.Id : ParserQuality.Unknown.Id,
            Revision = new Bigarr.Domain.Movies.Revision { Version = quality.Revision?.Version ?? 1, Real = quality.Revision?.Real ?? 0, IsRepack = quality.Revision?.IsRepack ?? false },
            ReleaseGroup = SeriesImportService.Group(parsed),
            Languages = SeriesImportService.LanguagesOf(parsed),
            OriginalReleaseTitle = Path.GetFileNameWithoutExtension(path),
        };
        db.EpisodeFiles.Add(file);
        foreach (var target in targets)
        {
            target.EpisodeFile = file;
        }

        db.UnmatchedEpisodeFiles.Remove(unmatched);
        await db.SaveChangesAsync(ct);
        foreach (var target in targets)
        {
            var line = GrabService.SeriesHistory(version, target, held.Count > 0 ? HistoryEventType.Upgraded : HistoryEventType.Imported, file.OriginalReleaseTitle, null, null, now);
            line.Data["path"] = path;
            line.Data["transfer"] = "inPlace";
            line.Data["manual"] = "true";
            line.Data["episodeFileId"] = file.Id.ToString(CultureInfo.InvariantCulture);
            db.History.Add(line);
        }

        outbox.Publish(new EpisodeFileImported(
            SeriesRef.From(version.Series),
            SeriesVersionRef.From(version),
            EpisodeRef.From(episodes),
            EventSnapshots.EpisodeFile(file, path) with { Quality = quality.Quality.Name },
            held.Count == 0 ? null : held.Select(f => EventSnapshots.EpisodeFile(f, Path.Combine(folder, f.RelativePath))).ToList(),
            null,
            null,
            Manual: true,
            "inPlace")
        { OccurredAt = now });
        await db.SaveChangesAsync(ct);

        if (request.Rename)
        {
            var loaded = await db.EpisodeFiles.Include(f => f.Quality).Include(f => f.Extras).Include(f => f.Episodes).ThenInclude(t => t.Episode).FirstAsync(f => f.Id == file.Id, ct);
            var target = await renamer.TargetPathAsync(config, version.Series, version, loaded, ct);
            var moved = renamer.Move(version.Series, version, loaded, target, now);
            renamer.PublishRenamed();
            await db.SaveChangesAsync(ct);
            if (!moved.Renamed && moved.Error is not null)
            {
                logger.LogWarning("Assigned {Path} but could not rename it: {Error}", path, moved.Error);
            }
        }

        logger.LogInformation("Assigned {Path} to {Episodes} of {Series} [{Version}]", unmatched.RelativePath, string.Join(", ", episodes.Select(GrabService.EpisodeLabel)), version.Series.Title, version.Name);
        db.ChangeTracker.Clear();
        return await EpisodeFileAsync(seriesId, file.Id, ct);
    }

    public async Task<Result<EpisodeFileDetailResponse>> EpisodeFileAsync(int seriesId, int fileId, CancellationToken ct)
    {
        var file = await db.EpisodeFiles.AsNoTracking()
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Include(f => f.Version).ThenInclude(v => v.Series)
            .Include(f => f.Version).ThenInclude(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(f => f.Quality)
            .Include(f => f.Extras)
            .Include(f => f.Episodes).ThenInclude(t => t.Episode)
            .AsSplitQuery()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.Version.SeriesId == seriesId, ct);
        if (file is null)
        {
            return Failure.NotFound("Episode file");
        }

        var series = file.Version.Series;
        var calculator = await scorer.CalculatorAsync(ct);
        var matched = calculator.Match(CustomFormatScorer.EpisodeFileInput(
            file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.ReleaseGroup, series.Year, series.OriginalLanguage));
        return Result<EpisodeFileDetailResponse>.Ok(new EpisodeFileDetailResponse(
            file.Id,
            file.VersionId,
            file.Version.Name,
            file.RelativePath,
            Path.Combine(SeriesRenameService.Folder(file.Version), file.RelativePath),
            file.Size,
            file.DateAdded,
            file.Quality?.Name ?? $"#{file.QualityId}",
            file.Revision.Version,
            file.ReleaseGroup,
            file.Languages,
            file.OriginalReleaseTitle,
            file.MediaInfo,
            matched.Select(m => m.Name).ToList(),
            ProfileRanking.FormatScore(file.Version.QualityProfile, matched.Select(m => m.Id)),
            file.Episodes.Select(t => t.Episode).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)
                .Select(e => new SeriesManualImportEpisode(e.Id, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteEpisodeNumber, e.Title, true)).ToList(),
            file.Extras.OrderBy(e => e.RelativePath, StringComparer.Ordinal)
                .Select(e => new EpisodeExtraResponse(e.Id, e.RelativePath, e.Extension, e.Kind, e.Language, e.Forced, e.HearingImpaired)).ToList()));
    }

    /// <summary>The file (and its extras) to the recycle bin; its episodes are missing again. Journaled per episode.</summary>
    /// <summary>Specials linked to a film (S8) take no file from the series side: the film engine imports them.</summary>
    private async Task<string?> FilmOwnedAsync(IEnumerable<int> episodeIds, CancellationToken ct)
    {
        var ids = episodeIds.Distinct().ToList();
        var owned = await db.SeriesMovieLinks.AsNoTracking().Where(l => ids.Contains(l.EpisodeId))
            .Select(l => new { l.Episode.SeasonNumber, l.Episode.EpisodeNumber, l.Movie.Title }).FirstOrDefaultAsync(ct);
        return owned is null ? null : $"S{owned.SeasonNumber:00}E{owned.EpisodeNumber:00} is the film {owned.Title}: import it as the film; the special follows as a hardlink.";
    }

    public async Task<Result<bool>> DeleteEpisodeFileAsync(int seriesId, int fileId, CancellationToken ct)
    {
        var file = await db.EpisodeFiles
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Include(f => f.Version).ThenInclude(v => v.Series)
            .Include(f => f.Quality)
            .Include(f => f.Extras)
            .Include(f => f.Episodes).ThenInclude(t => t.Episode)
            .AsSplitQuery()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.Version.SeriesId == seriesId, ct);
        if (file is null)
        {
            return Failure.NotFound("Episode file");
        }

        if (file.IsFilmLink)
        {
            return Failure.Conflict("Owned by a film", "This special is a hardlink of a film's file: delete the film's file, or unlink the film, instead.");
        }

        var config = await settings.GetAsync(ct);
        var folder = SeriesRenameService.Folder(file.Version);
        var path = Path.Combine(folder, file.RelativePath);
        string? recycled;
        try
        {
            recycled = recycleBin.RemoveFile(path, config.RecycleBinPath, file.Version.RootFolder.Path);
            foreach (var extra in file.Extras)
            {
                recycleBin.RemoveFile(Path.Combine(folder, extra.RelativePath), config.RecycleBinPath, file.Version.RootFolder.Path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete {Path}", path);
            return new Failure(StatusCodes.Status500InternalServerError, "Could not delete the file", $"{path}: {ex.Message}");
        }

        var now = time.GetUtcNow().UtcDateTime;
        outbox.Publish(new EpisodeFileDeleted(
            SeriesRef.From(file.Version.Series),
            SeriesVersionRef.From(file.Version),
            EpisodeRef.From(file.Episodes.Select(t => t.Episode)),
            EventSnapshots.EpisodeFile(file, path, recycleBinPath: recycled),
            "manual")
        { OccurredAt = now });
        foreach (var target in file.Episodes.ToList())
        {
            var line = GrabService.SeriesHistory(file.Version, target, HistoryEventType.FileDeleted, file.OriginalReleaseTitle, null, null, now);
            line.Data["path"] = path;
            line.Data["quality"] = file.Quality?.Name ?? string.Empty;
            line.Data["reason"] = "manual";
            if (recycled is not null)
            {
                line.Data["recycledTo"] = recycled;
            }

            db.History.Add(line);
            target.EpisodeFileId = null;
            target.EpisodeFile = null;
        }

        db.EpisodeFiles.Remove(file);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deleted episode file {Path}{Bin}", path, recycled is null ? string.Empty : $" (recycle bin: {recycled})");
        return Result<bool>.Ok(true);
    }

    private List<ManualImportExtraResponse> ListExtras(string video, string? releasePath, IReadOnlySet<string>? extensions, string listingRoot)
    {
        if (extensions is null)
        {
            return [];
        }

        try
        {
            return ExtraFiles.Find(video, releasePath, extensions)
                .Select(c => new ManualImportExtraResponse(c.Source.FullName, Path.GetRelativePath(listingRoot, c.Source.FullName), c.Name.Extension, c.Name.Kind, c.Name.Language, c.Name.Forced, c.Name.HearingImpaired))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The extra files next to {Video} could not be listed: {Message}", video, ex.Message);
            return [];
        }
    }

    private async Task<Result<string>> LocateAsync(Grab grab, CancellationToken ct)
    {
        if (grab.DownloadClientId is null || grab.DownloadId is null)
        {
            return Failure.Invalid("No download client", "This grab is not attached to a download client.");
        }

        var client = await db.DownloadClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == grab.DownloadClientId, ct);
        if (client is null)
        {
            return Failure.Invalid("Download client deleted", $"{grab.DownloadClientName} no longer exists; give the path by hand.");
        }

        try
        {
            var items = await resolver.For(client).GetItemsAsync(client, ct);
            var item = items.FirstOrDefault(i => string.Equals(i.DownloadId, grab.DownloadId, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                return Failure.Invalid("Torrent gone", $"{client.Name} no longer lists this torrent; give the path by hand.");
            }

            return string.IsNullOrWhiteSpace(item.OutputPath)
                ? Failure.Invalid("No path", $"{client.Name} did not report where the files are; give the path by hand.")
                : Result<string>.Ok(item.OutputPath);
        }
        catch (DownloadClientException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Download client unreachable", ex.Message);
        }
    }
}
