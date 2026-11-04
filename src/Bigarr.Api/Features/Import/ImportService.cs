using System.Globalization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.Specifications;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Naming;
using Bigarr.Parsing;
using Bigarr.Parsing.Model;
using Bigarr.Parsing.Qualities;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;
using ParserRevision = Bigarr.Parsing.Qualities.Revision;

namespace Bigarr.Api.Features.Import;

/// <summary>What one import attempt did. <see cref="Deferred"/> means "not ready yet", not a failure.</summary>
public sealed record ImportOutcome(bool Success, string? Reason, MediaFile? File, bool Upgraded, TransferOutcome? Transfer, string? DestinationPath, bool Deferred = false)
{
    public static ImportOutcome Failed(string reason) => new(false, reason, null, false, null, null);

    public static ImportOutcome Defer(string reason) => new(false, reason, null, false, null, null, Deferred: true);
}

/// <summary>A video file to bring into a version's folder. Shared by the automatic path and the manual import.</summary>
public sealed record ImportPlan(
    string SourcePath,
    /// <summary>Tracked, with Movie, QualityProfile (+ scores), RootFolder and File (+ Quality) loaded.</summary>
    MovieVersion Version,
    string ReleaseTitle,
    ParsedMovieInfo? Parsed,
    QualityModel Quality,
    IndexerFlags Flags,
    Grab? Grab,
    /// <summary>The user decided: no upgrade check, a one-shot with a file is replaced too.</summary>
    bool Force,
    TransferMode Mode);

/// <summary>
/// Completed download handling (blueprint §5): find the video in what the client wrote, attach it to its version (the grab
/// remembers it; a torrent added by hand is matched by its title), refuse what is not an upgrade, hardlink/copy/move it
/// into the version's folder under the naming template, probe it, replace the old file through the recycle bin, journal.
/// A failed import is never blocklisted: the release is not at fault (§3.5).
/// </summary>
public sealed class ImportService(
    BigarrDbContext db,
    MediaManagementService settings,
    DownloadClientResolver resolver,
    FileTransfer transfer,
    RecycleBin recycleBin,
    IMediaInfoReader probe,
    CustomFormatScorer scorer,
    TargetContextFactory contexts,
    ImportQueue imports,
    QueueState queue,
    OutboxWriter outbox,
    TimeProvider time,
    ILogger<ImportService> logger)
{
    public const string FailedMessageTitle = "Import failed";

    /// <summary>Specifications an import applies when it has to pick or judge a version itself (no user override).</summary>
    public static readonly ReleaseEvaluator Evaluator = new([new QualityAllowedSpecification(), new ConstraintsSpecification(), new UpgradableSpecification()]);

    /// <summary>The automatic path, run by <see cref="ImportRunner"/>: from <see cref="GrabState.ImportPending"/> to imported or import failed.</summary>
    public async Task ImportGrabAsync(int grabId, CancellationToken ct)
    {
        var grab = await db.Grabs.Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct);
        if (grab is null || grab.State != GrabState.ImportPending)
        {
            return;
        }

        grab.TransitionTo(GrabState.Importing);
        await db.SaveChangesAsync(ct);
        queue.RequestRefresh();

        ImportOutcome outcome;
        try
        {
            outcome = await ImportGrabCoreAsync(grab, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import of {Title} crashed", grab.ReleaseTitle);
            outcome = ImportOutcome.Failed(ex.Message);
        }

        if (outcome.Success)
        {
            logger.LogInformation("Imported {Title} → {Path} ({Transfer}{Upgrade})", grab.ReleaseTitle, outcome.DestinationPath, outcome.Transfer, outcome.Upgraded ? ", upgrade" : string.Empty);
        }
        else if (outcome.Deferred)
        {
            logger.LogInformation("Import of {Title} postponed: {Reason}", grab.ReleaseTitle, outcome.Reason);
            db.ChangeTracker.Clear();
            grab = await db.Grabs.Include(g => g.Targets).FirstAsync(g => g.Id == grabId, ct);
            if (grab.State == GrabState.Importing)
            {
                grab.TransitionTo(GrabState.ImportPending);
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            db.ChangeTracker.Clear();
            grab = await db.Grabs.Include(g => g.Targets).FirstAsync(g => g.Id == grabId, ct);
            await FailAsync(grab, outcome.Reason ?? "Unknown error", ct);
        }

        queue.RequestRefresh();
    }

    /// <summary>An import failed row goes back to pending and is picked up again: for after the user fixed the cause.</summary>
    public async Task<Result<bool>> RetryAsync(int grabId, CancellationToken ct)
    {
        var grab = await db.Grabs.Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct);
        if (grab is null)
        {
            return Failure.NotFound("Queue item");
        }

        if (grab.State != GrabState.ImportFailed)
        {
            return Failure.Conflict("Nothing to retry", $"The download is in state {grab.State}; only a failed import can be retried.");
        }

        grab.StatusMessages.RemoveAll(m => m.Title == FailedMessageTitle);
        grab.TransitionTo(GrabState.ImportPending);
        await db.SaveChangesAsync(ct);
        imports.TryEnqueue(grab.Id);
        queue.RequestRefresh();
        return Result<bool>.Ok(true);
    }

    private async Task<ImportOutcome> ImportGrabCoreAsync(Grab grab, CancellationToken ct)
    {
        if (grab.DownloadClientId is null)
        {
            return ImportOutcome.Failed("The download client of this grab is unknown.");
        }

        var client = await db.DownloadClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == grab.DownloadClientId, ct);
        if (client is null)
        {
            return ImportOutcome.Failed($"Download client {grab.DownloadClientName ?? "?"} was deleted.");
        }

        IReadOnlyList<DownloadItem> items;
        try
        {
            items = await resolver.For(client).GetItemsAsync(client, ct);
        }
        catch (DownloadClientException ex)
        {
            return ImportOutcome.Defer($"{client.Name} did not answer: {ex.Message}");
        }

        var item = items.FirstOrDefault(i => string.Equals(i.DownloadId, grab.DownloadId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return ImportOutcome.Failed($"The torrent is no longer in {client.Name}.");
        }

        if (item.Status != DownloadItemStatus.Completed)
        {
            return ImportOutcome.Defer($"{client.Name} reports the torrent as {item.Status}.");
        }

        if (string.IsNullOrWhiteSpace(item.OutputPath))
        {
            return ImportOutcome.Failed($"{client.Name} did not report where the files are.");
        }

        if (!File.Exists(item.OutputPath) && !Directory.Exists(item.OutputPath))
        {
            return ImportOutcome.Failed($"'{item.OutputPath}' is not accessible from Bigarr. Downloads and the library must share one mount, at the same path for {client.Name} and for Bigarr.");
        }

        var video = PickVideoFile(item.OutputPath);
        if (video is null)
        {
            return ImportOutcome.Failed("No video file in the download.");
        }

        var config = await settings.GetAsync(ct);
        var parsed = ReleaseMatcher.TryParse(grab.ReleaseTitle);
        var parsedFile = TryParsePath(video.FullName);
        var quality = QualityOf(parsed, parsedFile);

        MovieVersion? version = null;
        var targetId = grab.Targets.Select(t => (int?)t.TargetId).FirstOrDefault();
        if (targetId is not null)
        {
            version = await LoadVersionAsync(targetId.Value, ct);
            if (version is null)
            {
                return ImportOutcome.Failed("The version this download was grabbed for no longer exists.");
            }
        }
        else
        {
            // Added by hand (or grabbed before a database wipe): title → film → version whose profile and constraints accept it.
            var attribution = await AttributeAsync(grab, parsed ?? parsedFile, quality, video.Length, ct);
            if (attribution.Version is null)
            {
                return ImportOutcome.Failed(attribution.Reason ?? "No version accepts this release.");
            }

            version = attribution.Version;
            grab.AddTarget(version);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return ImportOutcome.Failed($"Version {version.Name} of {version.Movie.Title} already has an active download.");
            }

            logger.LogInformation("{Title} (added by hand) attributed to {Movie} [{Version}]", grab.ReleaseTitle, version.Movie.Title, version.Name);
        }

        var plan = new ImportPlan(video.FullName, version, grab.ReleaseTitle, parsed ?? parsedFile, quality, grab.IndexerFlags, grab, Force: false, config.TransferMode);
        return await ExecuteAsync(plan, ct);
    }

    /// <summary>The import itself, once the file and the version are known. Saves everything in one transaction at the end.</summary>
    public async Task<ImportOutcome> ExecuteAsync(ImportPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var config = await settings.GetAsync(ct);
        var version = plan.Version;
        var movie = version.Movie;
        var source = new FileInfo(plan.SourcePath);
        if (!source.Exists)
        {
            return ImportOutcome.Failed($"Source file not found: {plan.SourcePath}");
        }

        var folder = Path.GetFullPath(Path.Combine(version.RootFolder.Path, version.FolderName));
        var existing = version.File;
        var existingPath = existing is null ? null : Path.GetFullPath(Path.Combine(folder, existing.RelativePath));
        var inPlace = existingPath is not null && PathsEqual(existingPath, source.FullName);
        if (inPlace && !plan.Force)
        {
            return ImportOutcome.Failed("This file is already the version's file.");
        }

        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var qualityDefinition = definitions.GetValueOrDefault(plan.Quality.Quality.Id);
        var revision = plan.Quality.Revision ?? new ParserRevision();
        var edition = string.IsNullOrWhiteSpace(plan.Parsed?.Edition) ? null : plan.Parsed!.Edition.Trim();
        var releaseGroup = string.IsNullOrWhiteSpace(plan.Parsed?.ReleaseGroup) ? null : plan.Parsed!.ReleaseGroup.Trim();
        var languages = plan.Parsed?.Languages?.Where(l => l.Id > 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().Distinct().ToList() ?? [];

        var calculator = await scorer.CalculatorAsync(ct);
        var matched = calculator.Match(CustomFormatScorer.FileInput(
            plan.ReleaseTitle, source.Name, source.Length, plan.Flags, qualityDefinition, languages, edition, releaseGroup, movie.Year, movie.OriginalLanguage));

        if (!plan.Force && existing is not null)
        {
            var context = (await contexts.BuildAsync(movie, [version], ct)).Single();
            var release = new ReleaseInfo
            {
                Guid = plan.Grab?.ReleaseGuid ?? source.FullName,
                Title = plan.ReleaseTitle,
                IndexerName = plan.Grab?.IndexerName ?? "import",
                Size = source.Length,
            };
            var parsed = plan.Parsed ?? new ParsedMovieInfo();
            parsed.Quality ??= plan.Quality;
            var candidate = new ReleaseCandidate(release, parsed, 0) { CustomFormatIds = matched.Select(m => m.Id).ToList() };
            var rejection = new UpgradableSpecification().Evaluate(candidate, context);
            if (rejection is not null)
            {
                return ImportOutcome.Failed(rejection.Reason);
            }
        }

        var mediaInfo = await probe.ReadAsync(source.FullName, plan.ReleaseTitle, ct);
        if (languages.Count == 0 && mediaInfo is { AudioLanguages.Count: > 0 })
        {
            languages = [.. mediaInfo.AudioLanguages];
        }

        if (languages.Count == 0 && OriginalLanguageName(movie.OriginalLanguage) is { } original)
        {
            languages = [original];
        }

        var extension = source.Extension.ToLowerInvariant();
        var fileName = source.Name;
        if (config.RenameFiles)
        {
            var naming = new NamingContext(movie.Title, movie.OriginalTitle, movie.Year, movie.TmdbId, movie.ImdbId, version.Name)
            {
                QualityName = qualityDefinition?.Name ?? plan.Quality.Quality.Name,
                RevisionVersion = revision.Version,
                RevisionReal = revision.Real,
                IsRepack = revision.IsRepack,
                Edition = edition,
                ReleaseGroup = releaseGroup,
                Languages = languages,
                MediaInfo = mediaInfo,
                CustomFormats = matched.Where(m => m.IncludeWhenRenaming).Select(m => m.Name).ToList(),
                OriginalReleaseTitle = plan.ReleaseTitle,
                OriginalFileName = Path.GetFileNameWithoutExtension(source.Name),
            };
            fileName = NamingTemplate.Render(config.FileNameTemplate, naming) + extension;
        }

        var destination = Path.GetFullPath(Path.Combine(folder, fileName));
        var replacesInPlace = existingPath is not null && PathsEqual(existingPath, destination);
        var alreadyThere = PathsEqual(destination, source.FullName);
        if (File.Exists(destination) && !replacesInPlace && !alreadyThere)
        {
            return ImportOutcome.Failed($"A file already exists at {destination}. Add {{Version}} to the file name template if two versions share this folder, or remove that file.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        string? recycled = null;
        if (existingPath is not null && replacesInPlace && !alreadyThere)
        {
            recycled = recycleBin.RemoveFile(existingPath, config.RecycleBinPath, version.RootFolder.Path);
        }

        TransferOutcome? outcome = null;
        if (!alreadyThere)
        {
            outcome = await transfer.TransferAsync(source.FullName, destination, plan.Mode, ct);
        }

        if (existing is not null && existingPath is not null && !replacesInPlace && !PathsEqual(existingPath, destination))
        {
            try
            {
                recycled = recycleBin.RemoveFile(existingPath, config.RecycleBinPath, version.RootFolder.Path);
            }
            catch (IOException ex)
            {
                logger.LogWarning("The replaced file {Path} could not be removed: {Message}", existingPath, ex.Message);
            }
        }

        if (existing is not null)
        {
            db.MediaFiles.Remove(existing);
        }

        var file = new MediaFile
        {
            Version = version,
            RelativePath = Path.GetRelativePath(folder, destination),
            Size = source.Length,
            DateAdded = now,
            QualityId = plan.Quality.Quality.Id,
            Revision = new Bigarr.Domain.Movies.Revision { Version = revision.Version, Real = revision.Real, IsRepack = revision.IsRepack },
            Edition = edition,
            ReleaseGroup = releaseGroup,
            Languages = languages,
            IndexerFlags = plan.Flags,
            OriginalReleaseTitle = plan.ReleaseTitle,
            MediaInfo = mediaInfo,
        };
        version.File = file;

        // One shot: grabbed once, never again (§3.3).
        if (version.Mode == VersionMode.OneShot)
        {
            version.Monitored = false;
        }

        if (plan.Grab is not null)
        {
            plan.Grab.StatusMessages.RemoveAll(m => m.Title == FailedMessageTitle);
            if (!plan.Grab.State.IsTerminal())
            {
                plan.Grab.TransitionTo(GrabState.Imported);
            }
        }

        var data = new Dictionary<string, string>
        {
            ["path"] = destination,
            ["size"] = source.Length.ToString(CultureInfo.InvariantCulture),
            ["quality"] = qualityDefinition?.Name ?? plan.Quality.Quality.Name,
            ["transfer"] = outcome?.ToString() ?? "inPlace",
            ["source"] = source.FullName,
        };
        if (plan.Force)
        {
            data["manual"] = "true";
        }

        if (plan.Grab is not null)
        {
            data["downloadClient"] = plan.Grab.DownloadClientName ?? string.Empty;
            data["downloadId"] = plan.Grab.DownloadId ?? string.Empty;
        }

        if (existing is not null)
        {
            data["previousPath"] = existingPath ?? string.Empty;
            data["previousQuality"] = existing.Quality?.Name ?? definitions.GetValueOrDefault(existing.QualityId)?.Name ?? $"#{existing.QualityId}";
            if (recycled is not null)
            {
                data["recycledTo"] = recycled;
            }
        }

        if (matched.Count > 0)
        {
            data["customFormats"] = string.Join(", ", matched.Select(m => m.Name));
        }

        db.History.Add(new HistoryEvent
        {
            TargetId = version.Id,
            MovieId = movie.Id,
            TmdbId = movie.TmdbId,
            MovieTitle = movie.Title,
            VersionName = version.Name,
            EventType = existing is null ? HistoryEventType.Imported : HistoryEventType.Upgraded,
            SourceTitle = plan.ReleaseTitle,
            IndexerId = plan.Grab?.IndexerId,
            IndexerName = plan.Grab?.IndexerName,
            MediaFile = file,
            Data = data,
            CreatedAt = now,
        });

        // A file in place means the version is no longer waiting: the search backoff starts over.
        version.SearchAttempts = 0;

        // Two saves in one transaction: the event carries the new file's id.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        var releaseRef = plan.Grab is null ? null : new ReleaseRef(plan.ReleaseTitle, plan.Quality.Quality.Name, plan.Grab.IndexerName, plan.Grab.Size, releaseGroup, null, plan.Grab.InfoHash, revision.Version, languages, plan.Flags);
        var fileRef = FileRef.From(file, destination) with
        {
            Quality = data["quality"],
            CustomFormats = matched.Select(m => m.Name).ToList(),
            CustomFormatIds = matched.Select(m => m.Id).ToList(),
            CustomFormatScore = ProfileRanking.FormatScore(version.QualityProfile, matched.Select(m => m.Id)),
            SourcePath = source.FullName,
        };
        var transferName = data["transfer"];
        var clientType = await EventSnapshots.DownloadClientTypeAsync(db, plan.Grab?.DownloadClientId, ct);
        if (existing is null)
        {
            outbox.Publish(new FileImported(MovieRef.From(movie), VersionRef.From(version), fileRef, releaseRef, plan.Grab?.Id, plan.Force, transferName, plan.Grab?.DownloadClientName, clientType, plan.Grab?.DownloadId) { OccurredAt = now });
        }
        else
        {
            var previous = FileRef.From(existing, existingPath ?? string.Empty) with { Quality = data["previousQuality"], RecycleBinPath = recycled };
            outbox.Publish(new FileUpgraded(MovieRef.From(movie), VersionRef.From(version), fileRef, previous, releaseRef, plan.Grab?.Id, plan.Force, transferName, plan.Grab?.DownloadClientName, clientType, plan.Grab?.DownloadId) { OccurredAt = now });
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ImportOutcome(true, null, file, existing is not null, outcome, destination);
    }

    private async Task FailAsync(Grab grab, string reason, CancellationToken ct)
    {
        var version = grab.Targets.Count == 0 ? null : await db.MovieVersions.AsNoTracking()
            .Include(v => v.Movie)
            .Include(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(v => v.RootFolder)
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == grab.Targets.First().TargetId, ct);
        grab.StatusMessages.RemoveAll(m => m.Title == FailedMessageTitle);
        grab.StatusMessages.Add(new StatusMessage { Title = FailedMessageTitle, Messages = [reason], Severity = StatusSeverity.Error });
        if (!grab.State.IsTerminal())
        {
            grab.TransitionTo(GrabState.ImportFailed);
        }

        db.History.Add(new HistoryEvent
        {
            TargetId = version?.Id,
            MovieId = version?.MovieId,
            TmdbId = version?.Movie.TmdbId,
            MovieTitle = version?.Movie.Title,
            VersionName = version?.Name,
            EventType = HistoryEventType.ImportFailed,
            SourceTitle = grab.ReleaseTitle,
            IndexerId = grab.IndexerId,
            IndexerName = grab.IndexerName,
            Data = new Dictionary<string, string> { ["reason"] = reason, ["downloadClient"] = grab.DownloadClientName ?? string.Empty, ["downloadId"] = grab.DownloadId ?? string.Empty },
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });
        outbox.Publish(new ImportFailed(
            version is null ? null : MovieRef.From(version.Movie),
            version is null ? null : VersionRef.From(version),
            EventSnapshots.Release(grab.ReleaseTitle, grab.IndexerName, grab.Size, grab.IndexerFlags, version?.Movie, version?.QualityProfile, await scorer.CalculatorAsync(ct), null, grab.InfoHash),
            grab.Id,
            reason,
            grab.DownloadClientName,
            await EventSnapshots.DownloadClientTypeAsync(db, grab.DownloadClientId, ct),
            grab.DownloadId));
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Import of {Title} failed: {Reason}", grab.ReleaseTitle, reason);
    }

    /// <summary>Title → film in the library → the version whose profile and constraints accept the release (blueprint §5, fallback).</summary>
    private async Task<(MovieVersion? Version, string? Reason)> AttributeAsync(Grab grab, ParsedMovieInfo? parsed, QualityModel quality, long size, CancellationToken ct)
    {
        var movies = await contexts.MoviesWithVersions().ToListAsync(ct);
        var release = new ReleaseInfo { Guid = grab.DownloadId ?? grab.ReleaseTitle, Title = grab.ReleaseTitle, IndexerName = grab.IndexerName ?? "download client", Size = size };
        var (movie, reason) = ReleaseMatcher.FindMovie(parsed, release, LibraryIndex.Build(movies));
        if (movie is null || parsed is null)
        {
            return (null, reason ?? "Release title could not be parsed");
        }

        parsed.Quality ??= quality;
        var calculator = await scorer.CalculatorAsync(ct);
        var formats = calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie.Year, movie.OriginalLanguage));
        var candidate = new ReleaseCandidate(release, parsed, 0) { CustomFormatIds = formats.Select(f => f.Id).ToList() };
        var targets = await contexts.BuildAsync(movie, movie.Versions.ToList(), ct);
        var decisions = targets.Select(t => Evaluator.Evaluate(candidate, t)).ToList();
        var chosen = VersionAttribution.Choose(decisions);
        if (chosen is null)
        {
            var reasons = decisions.Select(d => $"{((MovieVersion)d.Target.Target).Name}: {string.Join(", ", d.Rejections.Select(r => r.Reason))}");
            return (null, $"No version of {movie.Title} accepts this release ({string.Join(" · ", reasons)}). Import it by hand to pick one.");
        }

        return (await LoadVersionAsync(chosen.Target.Target.Id, ct), null);
    }

    /// <summary>The tracked graph an import writes to.</summary>
    public Task<MovieVersion?> LoadVersionAsync(int versionId, CancellationToken ct) =>
        db.MovieVersions
            .Include(v => v.Movie)
            .Include(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(v => v.RootFolder)
            .Include(v => v.File!).ThenInclude(f => f.Quality)
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);

    /// <summary>The largest video file that is not a sample; a single file is taken as is.</summary>
    public static FileInfo? PickVideoFile(string outputPath)
    {
        if (File.Exists(outputPath))
        {
            return VideoFiles.IsVideo(outputPath) ? new FileInfo(outputPath) : null;
        }

        var candidates = new DirectoryInfo(outputPath)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => VideoFiles.IsVideo(f.Name) && !f.Name.EndsWith(".bigarr-partial", StringComparison.Ordinal))
            .ToList();
        var main = candidates.Where(f => !VideoFiles.LooksLikeSample(f.Name)).ToList();
        return (main.Count > 0 ? main : candidates).OrderByDescending(f => f.Length).FirstOrDefault();
    }

    /// <summary>The release title knows the quality better than a file called <c>movie.mkv</c>; the file name fills in.</summary>
    public static QualityModel QualityOf(ParsedMovieInfo? release, ParsedMovieInfo? file)
    {
        if (release?.Quality is { Quality.Id: not 0 })
        {
            return release.Quality;
        }

        return file?.Quality ?? release?.Quality ?? new QualityModel(ParserQuality.Unknown);
    }

    public static ParsedMovieInfo? TryParsePath(string path)
    {
        try
        {
            return Parser.ParseMoviePath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"fr" → "French": what a release naming no language is assumed to speak.</summary>
    public static string? OriginalLanguageName(string? isoCode)
    {
        if (string.IsNullOrWhiteSpace(isoCode))
        {
            return null;
        }

        try
        {
            return IsoLanguages.Find(isoCode)?.Language?.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
