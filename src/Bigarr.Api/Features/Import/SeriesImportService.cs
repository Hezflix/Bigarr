using System.Globalization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Decision.Specifications;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;
using Bigarr.Parsing.Episodes.Model;
using Bigarr.Parsing.Qualities;
using Microsoft.EntityFrameworkCore;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Import;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>One video file to bring into a series version's folder for the given episodes. Shared by the automatic path and the manual import.</summary>
public sealed record SeriesImportPlan(
    string SourcePath,
    int VersionId,
    IReadOnlyList<int> EpisodeIds,
    string ReleaseTitle,
    QualityModel Quality,
    string? ReleaseGroup,
    IReadOnlyList<string> Languages,
    IndexerFlags Flags,
    Grab? Grab,
    /// <summary>The user decided: no upgrade check.</summary>
    bool Force,
    TransferMode Mode,
    /// <summary>What the download client reported (folder or file), for the extra files.</summary>
    string? ReleasePath = null,
    /// <summary>The extra files picked in the manual import, exactly; null finds them per the media management settings.</summary>
    IReadOnlyList<string>? ExtraPaths = null);

public enum SeriesFileStatus
{
    Imported = 1,

    /// <summary>Left alone on purpose: not an upgrade of the episodes in place, a duplicate of another file of the pack.</summary>
    Skipped = 2,

    /// <summary>Needs a human: not mapped, ambiguous, unreadable, no room.</summary>
    Failed = 3,
}

/// <summary>What happened to one file of a series download.</summary>
public sealed record SeriesFileOutcome(
    string SourcePath,
    SeriesFileStatus Status,
    string? Reason,
    IReadOnlyList<string> Episodes,
    int? EpisodeFileId = null,
    string? DestinationPath = null,
    bool Upgraded = false,
    TransferOutcome? Transfer = null)
{
    public string Describe() =>
        $"{Path.GetFileName(SourcePath)}{(Episodes.Count > 0 ? $" ({string.Join(", ", Episodes)})" : string.Empty)}: {Reason}";
}

/// <summary>
/// Completed download handling for series (blueprint §5 applied to episodes, Sonarr's <c>ImportApprovedEpisodes</c>):
/// every video file of the download is parsed and mapped to episodes (the grab's episodes are the strong hint, the S2
/// mapper decides otherwise, an ambiguity is never guessed), judged against the files in place by the same upgrade
/// rule as the search (per episode), named by the series templates, hardlinked/copied/moved like a film, probed, its
/// subtitles brought along. Replaced files go to the recycle bin; a multi-episode file links every episode it holds.
/// Each file is saved on its own: a pack partly imported keeps what went in and reports the rest per file.
/// </summary>
public sealed class SeriesImportService(
    BigarrDbContext db,
    MediaManagementService settings,
    FileTransfer transfer,
    RecycleBin recycleBin,
    IMediaInfoReader probe,
    CustomFormatScorer scorer,
    SeriesTargetsFactory targetsFactory,
    TimeProvider time,
    ILogger<SeriesImportService> logger)
{
    /// <summary>True when the grab is about episodes, or (added by hand) sits in the client's series category.</summary>
    public async Task<bool> IsSeriesGrabAsync(Grab grab, DownloadClient client, DownloadItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grab);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(item);

        if (grab.Targets.Count == 0)
        {
            return client.IsSeriesCategory(item.Category);
        }

        var ids = grab.Targets.Select(t => t.TargetId).ToList();
        return await db.EpisodeTargets.AnyAsync(t => ids.Contains(t.Id), ct);
    }

    /// <summary>The automatic path: the download's files, mapped and imported one by one.</summary>
    public async Task<ImportOutcome> ImportDownloadAsync(Grab grab, DownloadItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grab);
        ArgumentNullException.ThrowIfNull(item);

        var files = VideoFiles(item.OutputPath!);
        if (files.Count == 0)
        {
            return ImportOutcome.Failed("No video file in the download.");
        }

        var config = await settings.GetAsync(ct);
        var releaseParsed = TryParse(grab.ReleaseTitle);

        // The version: the grab remembers it; a torrent added by hand finds its show by its title (or its files').
        var grabbed = await GrabbedTargetsAsync(grab, ct);
        int versionId;
        if (grabbed.Count > 0)
        {
            versionId = grabbed[0].VersionId;
        }
        else
        {
            var attribution = await AttributeAsync(grab, releaseParsed, files, ct);
            if (attribution.VersionId is null)
            {
                return ImportOutcome.Failed(attribution.Reason ?? "No series version accepts this release.");
            }

            versionId = attribution.VersionId.Value;
            grabbed = await GrabbedTargetsAsync(grab, ct);
        }

        var version = await db.SeriesVersions.AsNoTracking().Include(v => v.Series).FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return ImportOutcome.Failed("The series version this download was grabbed for no longer exists.");
        }

        var judge = await targetsFactory.LoadFilesOnlyAsync(version.SeriesId, ct);
        if (judge is null)
        {
            return ImportOutcome.Failed("The series of this download no longer exists.");
        }

        var grabEpisodeIds = grabbed.Select(t => t.EpisodeId).ToHashSet();
        var sceneSeason = await SceneSeasonAsync(version.Series, releaseParsed, ct);
        var releaseMapping = releaseParsed is null ? null : EpisodeMapper.Map(releaseParsed, judge.Mappable, judge.Episodes.ToList(), sceneSource: true, sceneSeason);
        var releaseQuality = releaseParsed?.Quality;

        var plans = new List<SeriesImportPlan>();
        var outcomes = new List<SeriesFileOutcome>();
        var claimed = new Dictionary<int, string>();
        foreach (var file in files)
        {
            var (episodes, reason) = MapFile(file, files.Count, judge, grabEpisodeIds, releaseMapping, sceneSeason, version.Series.TvdbId is { } tvdb ? await SceneSeasonForFileAsync(tvdb, file, ct) : null);
            if (episodes is null)
            {
                outcomes.Add(new SeriesFileOutcome(file.FullName, SeriesFileStatus.Failed, reason, []));
                continue;
            }

            var labels = episodes.Select(Label).ToList();
            var clash = episodes.FirstOrDefault(e => claimed.ContainsKey(e.Id));
            if (clash is not null)
            {
                outcomes.Add(new SeriesFileOutcome(file.FullName, SeriesFileStatus.Failed, $"{Label(clash)} is also in {claimed[clash.Id]}: two files of the download hold it.", labels));
                continue;
            }

            foreach (var episode in episodes)
            {
                claimed[episode.Id] = file.Name;
            }

            var parsedFile = TryParsePath(file.FullName);
            plans.Add(new SeriesImportPlan(
                file.FullName,
                version.Id,
                episodes.Select(e => e.Id).ToList(),
                files.Count == 1 ? grab.ReleaseTitle : Path.GetFileNameWithoutExtension(file.Name),
                QualityOf(releaseQuality, parsedFile?.Quality),
                Group(parsedFile) ?? Group(releaseParsed),
                LanguagesOf(parsedFile) is { Count: > 0 } fileLanguages ? fileLanguages : LanguagesOf(releaseParsed),
                grab.IndexerFlags,
                grab,
                Force: false,
                config.TransferMode,
                item.OutputPath));
        }

        foreach (var plan in plans)
        {
            outcomes.Add(await ExecuteGuardedAsync(plan, judge, ct));
        }

        return Summarize(grab, outcomes);
    }

    /// <summary>Imports one file, never throwing: a crash is that file's failure.</summary>
    public async Task<SeriesFileOutcome> ExecuteGuardedAsync(SeriesImportPlan plan, SeriesTargets? judge, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        try
        {
            var outcome = await ExecuteAsync(plan, judge, ct);
            db.ChangeTracker.Clear();
            return outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import of {Path} crashed", plan.SourcePath);
            db.ChangeTracker.Clear();
            return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Failed, ex.Message, []);
        }
    }

    /// <summary>The import of one file, once its version and episodes are known. One transaction per file.</summary>
    public async Task<SeriesFileOutcome> ExecuteAsync(SeriesImportPlan plan, SeriesTargets? judge, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var source = new FileInfo(plan.SourcePath);
        if (!source.Exists)
        {
            return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Failed, $"Source file not found: {plan.SourcePath}", []);
        }

        var config = await settings.GetAsync(ct);
        var version = await db.SeriesVersions
            .Include(v => v.Series)
            .Include(v => v.RootFolder)
            .Include(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .FirstOrDefaultAsync(v => v.Id == plan.VersionId, ct);
        if (version is null)
        {
            return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Failed, "Series version not found.", []);
        }

        var series = version.Series;
        var episodes = await db.Episodes.Where(e => e.SeriesId == series.Id && plan.EpisodeIds.Contains(e.Id)).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToListAsync(ct);
        if (episodes.Count == 0 || episodes.Count != plan.EpisodeIds.Distinct().Count())
        {
            return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Failed, "Some of the episodes are not episodes of this series.", []);
        }

        var labels = episodes.Select(Label).ToList();
        var now = time.GetUtcNow().UtcDateTime;
        var targets = await db.EpisodeTargets
            .Include(t => t.EpisodeFile!).ThenInclude(f => f.Extras)
            .Include(t => t.EpisodeFile!).ThenInclude(f => f.Quality)
            .Where(t => t.VersionId == version.Id && plan.EpisodeIds.Contains(t.EpisodeId))
            .ToListAsync(ct);
        foreach (var episode in episodes.Where(e => targets.All(t => t.EpisodeId != e.Id)))
        {
            // An episode the version has no row for yet (added by a refresh after the version).
            var created = new EpisodeTarget { Version = version, VersionId = version.Id, Episode = episode, EpisodeId = episode.Id, Monitored = false, CreatedAt = now };
            db.EpisodeTargets.Add(created);
            targets.Add(created);
        }

        foreach (var target in targets)
        {
            target.Episode = episodes.First(e => e.Id == target.EpisodeId);
            target.Version = version;
        }

        var folder = Path.GetFullPath(Path.Combine(version.RootFolder.Path, version.FolderName));
        var existing = targets.Where(t => t.EpisodeFile is not null).Select(t => t.EpisodeFile!).DistinctBy(f => f.Id).ToList();
        var existingPaths = existing.ToDictionary(f => f.Id, f => Path.GetFullPath(Path.Combine(folder, f.RelativePath)));
        var inPlace = existing.FirstOrDefault(f => PathsEqual(existingPaths[f.Id], source.FullName));
        if (inPlace is not null && !plan.Force)
        {
            return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Skipped, "This file is already the episode's file.", labels);
        }

        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var qualityDefinition = definitions.GetValueOrDefault(plan.Quality.Quality.Id);
        var revision = plan.Quality.Revision ?? new Bigarr.Parsing.Qualities.Revision();
        var languages = plan.Languages.ToList();
        var calculator = await scorer.CalculatorAsync(ct);
        var matched = calculator.Match(CustomFormatScorer.EpisodeFileInput(
            plan.ReleaseTitle, source.Name, source.Length, plan.Flags, qualityDefinition, languages, plan.ReleaseGroup, series.Year, series.OriginalLanguage));

        if (!plan.Force && existing.Count > 0)
        {
            judge ??= await targetsFactory.LoadFilesOnlyAsync(series.Id, ct);
            var shared = judge?.Episodes.Where(e => plan.EpisodeIds.Contains(e.Id)).ToList() ?? [];
            var context = judge?.ContextsFor(shared, version.Id).FirstOrDefault();
            if (context is not null)
            {
                var candidate = new ReleaseCandidate(
                    new ReleaseInfo { Guid = plan.Grab?.ReleaseGuid ?? source.FullName, Title = plan.ReleaseTitle, IndexerName = plan.Grab?.IndexerName ?? "import", Size = source.Length },
                    new ParsedEpisodeInfo { ReleaseTitle = plan.ReleaseTitle, Quality = plan.Quality, ReleaseGroup = plan.ReleaseGroup ?? string.Empty },
                    0)
                {
                    CustomFormatIds = matched.Select(m => m.Id).ToList(),
                };
                if (new UpgradableSpecification().Evaluate(candidate, context) is { } rejection)
                {
                    return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Skipped, rejection.Reason, labels);
                }
            }
        }

        var mediaInfo = await probe.ReadAsync(source.FullName, plan.ReleaseTitle, ct);
        if (languages.Count == 0 && mediaInfo is { AudioLanguages.Count: > 0 })
        {
            languages = [.. mediaInfo.AudioLanguages];
        }

        if (languages.Count == 0 && ImportService.OriginalLanguageName(series.OriginalLanguage) is { } original)
        {
            languages = [original];
        }

        var extension = source.Extension.ToLowerInvariant();
        var facts = new EpisodeFileFacts(
            qualityDefinition?.Name ?? plan.Quality.Quality.Name,
            revision.Version,
            revision.Real,
            revision.IsRepack,
            plan.ReleaseGroup,
            languages,
            mediaInfo,
            matched.Where(m => m.IncludeWhenRenaming).Select(m => m.Name).ToList(),
            plan.ReleaseTitle,
            Path.GetFileNameWithoutExtension(source.Name));
        var relative = SeriesNaming.RelativePath(config, series, version, episodes, facts, extension, source.Name);
        var destination = Path.GetFullPath(Path.Combine(folder, relative));
        var replacesInPlace = existing.FirstOrDefault(f => PathsEqual(existingPaths[f.Id], destination));
        var alreadyThere = PathsEqual(destination, source.FullName);
        if (File.Exists(destination) && replacesInPlace is null && !alreadyThere)
        {
            return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Failed, $"A file already exists at {destination}. Add {{Version}} or {{Quality Full}} to the episode format if two versions share this folder, or remove that file.", labels);
        }

        var extraCandidates = FindExtras(plan, config, source.FullName, folder);

        // Every file in place for these episodes leaves through the recycle bin, its extras with it; the other
        // episodes a multi-episode file held become missing (Sonarr does the same).
        var recycled = new Dictionary<int, string?>();
        var others = new List<EpisodeTarget>();
        if (existing.Count > 0)
        {
            var ids = existing.Select(f => f.Id).ToList();
            others = await db.EpisodeTargets.Include(t => t.Episode).Where(t => t.EpisodeFileId != null && ids.Contains(t.EpisodeFileId.Value)).ToListAsync(ct);
        }

        // A file at the destination goes first so the new one can take its name; the source itself (a library file
        // assigned again by hand) is moved, never recycled.
        bool IsSource(EpisodeFile f) => inPlace is not null && f.Id == inPlace.Id;
        if (replacesInPlace is not null && !alreadyThere && !IsSource(replacesInPlace))
        {
            recycled[replacesInPlace.Id] = RecycleQuietly(existingPaths[replacesInPlace.Id], config, version.RootFolder.Path);
        }

        TransferOutcome? outcome = null;
        if (!alreadyThere)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            outcome = await transfer.TransferAsync(source.FullName, destination, inPlace is not null ? TransferMode.Move : plan.Mode, ct);
        }

        foreach (var old in existing.Where(f => !IsSource(f)))
        {
            if (!recycled.ContainsKey(old.Id))
            {
                recycled[old.Id] = RecycleQuietly(existingPaths[old.Id], config, version.RootFolder.Path);
            }

            RecycleExtras(old, folder, config, version.RootFolder.Path);
        }

        var extras = new List<EpisodeExtraFile>();
        if (inPlace is not null)
        {
            // A library file imported again (assigned by hand): its extras stay attached, renamed with it below.
            extras.AddRange(inPlace.Extras.Select(e => new EpisodeExtraFile
            {
                RelativePath = e.RelativePath,
                Extension = e.Extension,
                Kind = e.Kind,
                Language = e.Language,
                Forced = e.Forced,
                HearingImpaired = e.HearingImpaired,
                DateAdded = e.DateAdded,
            }));
        }

        extras.AddRange(await TransferExtrasAsync(extraCandidates, destination, folder, plan.Mode, now, ct));

        var previousQuality = targets.ToDictionary(t => t.Id, t => t.EpisodeFile is { } f ? f.Quality?.Name ?? definitions.GetValueOrDefault(f.QualityId)?.Name : null);
        foreach (var target in others)
        {
            target.EpisodeFileId = null;
            target.EpisodeFile = null;
        }

        db.EpisodeFiles.RemoveRange(existing);

        var file = new EpisodeFile
        {
            VersionId = version.Id,
            Version = version,
            RelativePath = Path.GetRelativePath(folder, destination),
            SeasonNumber = episodes[0].SeasonNumber,
            Size = source.Length,
            DateAdded = now,
            QualityId = definitions.ContainsKey(plan.Quality.Quality.Id) ? plan.Quality.Quality.Id : ParserQuality.Unknown.Id,
            Revision = new Bigarr.Domain.Movies.Revision { Version = revision.Version, Real = revision.Real, IsRepack = revision.IsRepack },
            ReleaseGroup = plan.ReleaseGroup,
            Languages = languages,
            IndexerFlags = plan.Flags,
            OriginalReleaseTitle = plan.ReleaseTitle,
            MediaInfo = mediaInfo,
            Extras = extras,
        };
        db.EpisodeFiles.Add(file);
        foreach (var target in targets)
        {
            target.EpisodeFile = file;
            target.SearchAttempts = 0;
        }

        await db.UnmatchedEpisodeFiles.Where(u => u.VersionId == version.Id && u.RelativePath == file.RelativePath).ExecuteDeleteAsync(ct);

        if (plan.Grab is not null)
        {
            plan.Grab.StatusMessages.RemoveAll(m => m.Title == ImportService.FailedMessageTitle);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);

        foreach (var target in targets)
        {
            var upgraded = previousQuality[target.Id] is not null;
            var line = GrabService.SeriesHistory(version, target, upgraded ? HistoryEventType.Upgraded : HistoryEventType.Imported, plan.ReleaseTitle, plan.Grab?.IndexerId, plan.Grab?.IndexerName, now);
            line.Data["path"] = destination;
            line.Data["size"] = source.Length.ToString(CultureInfo.InvariantCulture);
            line.Data["quality"] = facts.QualityName ?? string.Empty;
            line.Data["transfer"] = outcome?.ToString() ?? "inPlace";
            line.Data["source"] = source.FullName;
            line.Data["episodeFileId"] = file.Id.ToString(CultureInfo.InvariantCulture);
            if (labels.Count > 1)
            {
                line.Data["episodes"] = string.Join(", ", labels);
            }

            if (plan.Force)
            {
                line.Data["manual"] = "true";
            }

            if (plan.Grab is not null)
            {
                line.Data["downloadClient"] = plan.Grab.DownloadClientName ?? string.Empty;
                line.Data["downloadId"] = plan.Grab.DownloadId ?? string.Empty;
            }

            if (upgraded)
            {
                line.Data["previousQuality"] = previousQuality[target.Id]!;
                var old = existing.FirstOrDefault(f => f.Id == target.EpisodeFile?.Id) ?? existing.FirstOrDefault();
                if (old is not null)
                {
                    line.Data["previousPath"] = existingPaths[old.Id];
                    if (recycled.GetValueOrDefault(old.Id) is { } bin)
                    {
                        line.Data["recycledTo"] = bin;
                    }
                }
            }

            if (matched.Count > 0)
            {
                line.Data["customFormats"] = string.Join(", ", matched.Select(m => m.Name));
            }

            if (extras.Count > 0)
            {
                line.Data["extras"] = extras.Count.ToString(CultureInfo.InvariantCulture);
            }

            db.History.Add(line);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var upgradedAny = previousQuality.Values.Any(v => v is not null);
        logger.LogInformation("Imported {Source} → {Destination} for {Series} [{Version}] {Episodes} ({Transfer}{Upgrade})", source.Name, destination, series.Title, version.Name, string.Join(", ", labels), outcome?.ToString() ?? "in place", upgradedAny ? ", upgrade" : string.Empty);
        return new SeriesFileOutcome(plan.SourcePath, SeriesFileStatus.Imported, null, labels, file.Id, destination, upgradedAny, outcome);
    }

    /// <summary>
    /// The grab ends imported when no file failed (files skipped on purpose do not count), import failed otherwise, with
    /// one line per file that needs a human.
    /// </summary>
    private static ImportOutcome Summarize(Grab grab, List<SeriesFileOutcome> outcomes)
    {
        var imported = outcomes.Where(o => o.Status == SeriesFileStatus.Imported).ToList();
        var failed = outcomes.Where(o => o.Status == SeriesFileStatus.Failed).ToList();
        var skipped = outcomes.Where(o => o.Status == SeriesFileStatus.Skipped).ToList();

        if (failed.Count == 0 && imported.Count > 0)
        {
            var notes = skipped.Select(s => s.Describe()).ToList();
            return new ImportOutcome(true, null, null, imported.Any(i => i.Upgraded), imported[0].Transfer, imported[0].DestinationPath) { Messages = notes, ImportedFiles = imported.Count };
        }

        if (failed.Count == 0)
        {
            // Nothing upgraded anything: refused like a film that is not an upgrade, never blocklisted.
            var reason = skipped.Count == 1 ? skipped[0].Reason ?? "Not an upgrade." : $"None of the {skipped.Count} files upgrades its episodes.";
            return ImportOutcome.Failed(reason) with { Messages = skipped.Select(s => s.Describe()).ToList() };
        }

        var summary = imported.Count > 0
            ? $"{imported.Count} of {outcomes.Count} files imported; {failed.Count} need a manual import."
            : failed.Count == 1 ? failed[0].Reason ?? "The file could not be imported." : $"None of the {outcomes.Count} files could be imported.";
        return ImportOutcome.Failed(summary) with
        {
            Messages = failed.Concat(skipped).Select(f => f.Describe()).ToList(),
            ImportedFiles = imported.Count,
        };
    }

    /// <summary>
    /// The episodes one file of the download holds. Its own name decides when it maps; a download of a single file whose
    /// name says nothing usable takes the grab's episodes (or the release title's). Never guessed beyond that.
    /// </summary>
    private static (List<Episode>? Episodes, string? Reason) MapFile(
        FileInfo file,
        int fileCount,
        SeriesTargets judge,
        HashSet<int> grabEpisodeIds,
        EpisodeMapping? releaseMapping,
        int? releaseSceneSeason,
        int? fileSceneSeason)
    {
        var parsed = TryParsePath(file.FullName);
        if (parsed is { FullSeason: true } && fileCount > 1)
        {
            // Sonarr's parser falls back to the folder: a file whose own name says nothing reads as the pack itself.
            return (null, "Not recognised: its name says no episode (only the folder names the season). Import it by hand to choose its episodes.");
        }

        var mapping = parsed is { FullSeason: true }
            ? EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, "Named like a season pack.")
            : EpisodeMapper.Map(parsed, judge.Mappable, judge.Episodes.ToList(), sceneSource: true, fileSceneSeason ?? releaseSceneSeason);

        if (mapping.IsMapped)
        {
            return (mapping.Episodes.ToList(), null);
        }

        if (fileCount == 1)
        {
            if (grabEpisodeIds.Count > 0)
            {
                return (judge.Episodes.Where(e => grabEpisodeIds.Contains(e.Id)).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList(), null);
            }

            if (releaseMapping is { IsMapped: true } release)
            {
                return (release.Episodes.ToList(), null);
            }
        }

        var what = mapping.Status switch
        {
            EpisodeMappingStatus.Ambiguous => "Ambiguous",
            EpisodeMappingStatus.EpisodeNotFound => "No such episode",
            EpisodeMappingStatus.WrongSeriesType => "Wrong series type",
            _ => "Not recognised",
        };
        return (null, $"{what}: {mapping.Detail} Import it by hand to choose its episodes.");
    }

    /// <summary>
    /// Title → series in the library → the version whose profile and constraints accept the release (blueprint §5,
    /// fallback), then one grab target per episode the title covers. Nothing found → manual import.
    /// </summary>
    private async Task<(int? VersionId, string? Reason)> AttributeAsync(Grab grab, ParsedEpisodeInfo? parsed, List<FileInfo> files, CancellationToken ct)
    {
        var index = await targetsFactory.IndexAsync(ct);
        var probe = parsed ?? files.Select(f => TryParsePath(f.FullName)).FirstOrDefault(p => p is not null);
        if (probe is null)
        {
            return (null, "Neither the release title nor its files could be parsed as episodes. Import it by hand.");
        }

        var match = index.Find(probe);
        if (match.Series is null)
        {
            return (null, match.IsAmbiguous
                ? $"Several series in the library match '{probe.SeriesTitle}': {string.Join(", ", match.Ambiguous.Select(s => s.Title))}. Import it by hand."
                : $"No series in the library matches '{probe.SeriesTitle}'. Import it by hand.");
        }

        var judge = await targetsFactory.LoadFilesOnlyAsync(match.Series.Id, ct);
        if (judge is null)
        {
            return (null, "The series is gone.");
        }

        var release = new ReleaseInfo { Guid = grab.DownloadId ?? grab.ReleaseTitle, Title = grab.ReleaseTitle, IndexerName = grab.IndexerName ?? "download client", Size = grab.Size };
        var calculator = await scorer.CalculatorAsync(ct);
        var judgement = SeriesReleaseJudge.Judge(release, probe, judge, match.SceneSeasonNumber, calculator, ImportService.SeriesEvaluator, 0);
        if (!judgement.IsMapped)
        {
            return (null, $"{judgement.UnmatchedReason}. Import it by hand.");
        }

        if (judgement.Attributed is null)
        {
            var reasons = judgement.Decisions.Select(d =>
            {
                var version = judge.Series.Versions.First(v => v.Id == ((EpisodeTarget)d.Target.Target).VersionId);
                return $"{version.Name}: {string.Join(", ", d.Rejections.Select(r => r.Reason))}";
            });
            return (null, $"No version of {judge.Series.Title} accepts this release ({string.Join(" · ", reasons)}). Import it by hand to pick one.");
        }

        var versionId = ((EpisodeTarget)judgement.Attributed.Target.Target).VersionId;
        var targetIds = judgement.Attributed.Target.Episodes!.Slots.Select(s => s.Target.Id).ToList();
        var tracked = await db.Grabs.Include(g => g.Targets).FirstAsync(g => g.Id == grab.Id, ct);
        foreach (var id in targetIds.Where(id => tracked.Targets.All(t => t.TargetId != id)))
        {
            tracked.Targets.Add(new GrabTarget { GrabId = tracked.Id, TargetId = id, Active = tracked.IsActive });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return (null, $"An episode of this release already has an active download for {judge.Series.Title}.");
        }

        logger.LogInformation("{Title} (added by hand) attributed to {Series} [{Version}]", grab.ReleaseTitle, judge.Series.Title, judge.Series.Versions.First(v => v.Id == versionId).Name);
        return (versionId, null);
    }

    private async Task<List<EpisodeTarget>> GrabbedTargetsAsync(Grab grab, CancellationToken ct)
    {
        var ids = await db.GrabTargets.AsNoTracking().Where(t => t.GrabId == grab.Id).Select(t => t.TargetId).ToListAsync(ct);
        return await db.EpisodeTargets.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
    }

    /// <summary>The scene season a release title stands for, when it is one of the show's XEM names ("Show S2").</summary>
    private async Task<int?> SceneSeasonAsync(Series series, ParsedEpisodeInfo? parsed, CancellationToken ct)
    {
        if (series.TvdbId is not { } tvdb || parsed?.SeriesTitle is not { Length: > 0 } title)
        {
            return null;
        }

        var clean = SeriesTitleIndex.Clean(title);
        var names = await db.SceneNames.AsNoTracking().Where(n => n.TvdbId == tvdb && n.SceneSeasonNumber != null).ToListAsync(ct);
        return names.FirstOrDefault(n => n.CleanTitle == clean)?.SceneSeasonNumber;
    }

    private async Task<int?> SceneSeasonForFileAsync(int tvdbId, FileInfo file, CancellationToken ct)
    {
        var parsed = TryParsePath(file.FullName);
        if (parsed?.SeriesTitle is not { Length: > 0 } title)
        {
            return null;
        }

        var clean = SeriesTitleIndex.Clean(title);
        var names = await db.SceneNames.AsNoTracking().Where(n => n.TvdbId == tvdbId && n.SceneSeasonNumber != null).ToListAsync(ct);
        return names.FirstOrDefault(n => n.CleanTitle == clean)?.SceneSeasonNumber;
    }

    /// <summary>The release's side files (the user's pick, else what the settings find), minus what already sits in the version folder.</summary>
    private List<ExtraCandidate> FindExtras(SeriesImportPlan plan, MediaManagementConfig config, string video, string folder)
    {
        try
        {
            var candidates = plan.ExtraPaths is not null
                ? ExtraFiles.Chosen(video, plan.ExtraPaths)
                : config.ImportExtraFiles ? ExtraFiles.Find(video, plan.ReleasePath, ExtraFiles.ParseExtensions(config.ExtraFileExtensions)) : [];
            return candidates.Where(c => !IsUnder(c.Source.FullName, folder)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The extra files next to {Video} could not be listed: {Message}", video, ex.Message);
            return [];
        }
    }

    private async Task<List<EpisodeExtraFile>> TransferExtrasAsync(IReadOnlyList<ExtraCandidate> candidates, string destination, string folder, TransferMode mode, DateTime now, CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var extras = new List<EpisodeExtraFile>();
        try
        {
            var targets = ExtraFiles.Destinations(candidates.Select(c => c.Name).ToList(), Path.GetDirectoryName(destination)!, Path.GetFileNameWithoutExtension(destination), File.Exists);
            foreach (var (candidate, target) in candidates.Zip(targets))
            {
                try
                {
                    await transfer.TransferAsync(candidate.Source.FullName, target, mode, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Extra file {Source} was left behind: {Message}", candidate.Source.FullName, ex.Message);
                    continue;
                }

                extras.Add(new EpisodeExtraFile
                {
                    RelativePath = Path.GetRelativePath(folder, target),
                    Extension = candidate.Name.Extension,
                    Kind = candidate.Name.Kind,
                    Language = candidate.Name.Language,
                    Forced = candidate.Name.Forced,
                    HearingImpaired = candidate.Name.HearingImpaired,
                    DateAdded = now,
                });
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning("The extra files of {Destination} were left behind: {Message}", destination, ex.Message);
        }

        return extras;
    }

    private void RecycleExtras(EpisodeFile replaced, string folder, MediaManagementConfig config, string root)
    {
        foreach (var extra in replaced.Extras)
        {
            RecycleQuietly(Path.Combine(folder, extra.RelativePath), config, root);
        }
    }

    private string? RecycleQuietly(string path, MediaManagementConfig config, string root)
    {
        try
        {
            return recycleBin.RemoveFile(path, config.RecycleBinPath, root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The replaced file {Path} could not be removed: {Message}", path, ex.Message);
            return null;
        }
    }

    /// <summary>Sonarr's import listing: every video of the download that is not a sample or an extra (a single file is taken as is).</summary>
    public static List<FileInfo> VideoFiles(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (File.Exists(outputPath))
        {
            return Bigarr.Domain.Library.VideoFiles.IsVideo(outputPath) ? [new FileInfo(outputPath)] : [];
        }

        return Directory.Exists(outputPath)
            ? Bigarr.Api.Features.Series.SeriesLibraryScanner.EnumerateVideoFiles(new DirectoryInfo(outputPath)).Where(f => !f.Name.EndsWith(".bigarr-partial", StringComparison.Ordinal)).ToList()
            : [];
    }

    /// <summary>The release title knows the quality better than an episode file named <c>01.mkv</c>; the file name fills in.</summary>
    public static QualityModel QualityOf(QualityModel? release, QualityModel? file)
    {
        if (release is { Quality.Id: not 0 })
        {
            return release;
        }

        return file ?? release ?? new QualityModel(ParserQuality.Unknown);
    }

    public static ParsedEpisodeInfo? TryParsePath(string path)
    {
        try
        {
            return EpisodeParser.ParsePath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static ParsedEpisodeInfo? TryParse(string title)
    {
        try
        {
            return EpisodeParser.ParseTitle(title);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? Group(ParsedEpisodeInfo? parsed) => string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup.Trim();

    public static List<string> LanguagesOf(ParsedEpisodeInfo? parsed) =>
        parsed?.Languages?.Where(l => l.Id > 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().Distinct().ToList() ?? [];

    public static string Label(Episode episode) => GrabService.EpisodeLabel(episode);

    private static bool IsUnder(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
