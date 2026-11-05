using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Parsing.Model;
using Bigarr.Parsing.Qualities;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Import;

/// <summary>
/// Manual import (blueprint §5): list the video files of a path or of a queued download with what Bigarr makes of
/// them (film, quality, version that would take it, reasons), then import what the user picked, forced.
/// </summary>
public sealed class ManualImportService(BigarrDbContext db, ImportService importer, MediaManagementService settings, DownloadClientResolver resolver, TargetContextFactory contexts, CustomFormatScorer scorer, QueueState queue, ILogger<ManualImportService> logger)
{
    public async Task<Result<ManualImportResponse>> ListAsync(string? path, int? grabId, int? movieId, CancellationToken ct)
    {
        Grab? grab = null;
        string? releaseTitle = null;
        MovieVersion? grabbedFor = null;
        if (grabId is not null)
        {
            grab = await db.Grabs.AsNoTracking().Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct);
            if (grab is null)
            {
                return Failure.NotFound("Queue item");
            }

            releaseTitle = grab.ReleaseTitle;
            var targetId = grab.Targets.Select(t => (int?)t.TargetId).FirstOrDefault();
            grabbedFor = targetId is null ? null : await db.MovieVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == targetId, ct);

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
        var movies = await contexts.MoviesWithVersions().ToListAsync(ct);
        var library = LibraryIndex.Build(movies);
        var forcedMovie = movieId is null ? null : movies.FirstOrDefault(m => m.Id == movieId);
        var grabbedMovie = grabbedFor is null ? null : movies.FirstOrDefault(m => m.Id == grabbedFor.MovieId);
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var calculator = await scorer.CalculatorAsync(ct);
        var contextCache = new Dictionary<int, List<TargetContext>>();
        var extraExtensions = config.ImportExtraFiles ? ExtraFiles.ParseExtensions(config.ExtraFileExtensions) : null;
        var listingRoot = File.Exists(path) ? Path.GetDirectoryName(path)! : path;

        var files = File.Exists(path)
            ? [new FileInfo(path)]
            : new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => VideoFiles.IsVideo(f.Name) && !f.Name.EndsWith(".bigarr-partial", StringComparison.Ordinal))
                .OrderByDescending(f => f.Length)
                .ToList();

        var rows = new List<ManualImportFileResponse>();
        foreach (var file in files)
        {
            var parsedRelease = releaseTitle is null ? null : ReleaseMatcher.TryParse(releaseTitle);
            var parsedFile = ImportService.TryParsePath(file.FullName);
            var parsed = parsedRelease ?? parsedFile;
            var quality = ImportService.QualityOf(parsedRelease, parsedFile);
            var qualityName = definitions.GetValueOrDefault(quality.Quality.Id)?.Name ?? quality.Quality.Name;

            Movie? movie = grabbedMovie ?? forcedMovie;
            string? matchReason = null;
            if (movie is null)
            {
                var release = new ReleaseInfo { Guid = file.FullName, Title = releaseTitle ?? file.Name, IndexerName = "manual import", Size = file.Length };
                (movie, matchReason) = ReleaseMatcher.FindMovie(parsed, release, library);
                if (movie is null && parsedFile is not null && !ReferenceEquals(parsed, parsedFile))
                {
                    (movie, matchReason) = ReleaseMatcher.FindMovie(parsedFile, release, library);
                }
            }

            var options = new List<ImportVersionOption>();
            int? suggested = grabbedFor?.Id;
            if (movie is not null)
            {
                if (!contextCache.TryGetValue(movie.Id, out var targets))
                {
                    targets = await contexts.BuildAsync(movie, movie.Versions.ToList(), ct);
                    contextCache[movie.Id] = targets;
                }

                var judged = parsed ?? new ParsedMovieInfo();
                judged.Quality ??= quality;
                var release = new ReleaseInfo { Guid = file.FullName, Title = releaseTitle ?? file.Name, IndexerName = "manual import", Size = file.Length };
                var formats = calculator.Match(CustomFormatScorer.ReleaseInput(judged, release, movie.Year, movie.OriginalLanguage));
                var candidate = new ReleaseCandidate(release, judged, 0) { CustomFormatIds = formats.Select(f => f.Id).ToList() };
                var decisions = targets.Select(t => ImportService.Evaluator.Evaluate(candidate, t)).ToList();
                options = decisions.Select(d =>
                {
                    var version = (MovieVersion)d.Target.Target;
                    return new ImportVersionOption(version.Id, version.Name, version.IsDefault, d.Approved, d.Rejections.Select(r => r.Reason).ToList());
                }).ToList();
                suggested ??= VersionAttribution.Choose(decisions)?.Target.Target.Id ?? movie.Versions.FirstOrDefault(v => v.IsDefault)?.Id;
            }

            rows.Add(new ManualImportFileResponse(
                file.FullName,
                File.Exists(path) ? file.Name : Path.GetRelativePath(path, file.FullName),
                file.Name,
                file.Length,
                VideoFiles.LooksLikeSample(file.Name),
                parsed?.PrimaryMovieTitle,
                parsed is { Year: > 0 } ? parsed.Year : null,
                quality.Quality.Id,
                qualityName,
                quality.Revision?.Version ?? 1,
                quality.Revision?.IsRepack ?? false,
                string.IsNullOrWhiteSpace(parsed?.Edition) ? null : parsed!.Edition,
                string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup,
                parsed?.Languages?.Where(l => l.Id > 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [],
                movie?.Id,
                movie?.Title,
                movie?.Year,
                movie is null ? matchReason : null,
                suggested,
                options,
                ListExtras(file.FullName, grab is null ? null : path, extraExtensions, listingRoot)));
        }

        return Result<ManualImportResponse>.Ok(new ManualImportResponse(path, grab?.Id, releaseTitle, config.TransferMode, rows));
    }

    public async Task<Result<List<ManualImportResultResponse>>> ImportAsync(ManualImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Files is null || request.Files.Count == 0)
        {
            return Failure.Invalid("Nothing to import", "Pick at least one file.");
        }

        var config = await settings.GetAsync(ct);
        var mode = request.TransferMode ?? config.TransferMode;
        var results = new List<ManualImportResultResponse>();
        foreach (var item in request.Files)
        {
            results.Add(await ImportOneAsync(item, mode, ct));
            db.ChangeTracker.Clear();
        }

        queue.RequestRefresh();
        return Result<List<ManualImportResultResponse>>.Ok(results);
    }

    private async Task<ManualImportResultResponse> ImportOneAsync(ManualImportItemRequest item, TransferMode mode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Path) || !Path.IsPathRooted(item.Path) || !File.Exists(item.Path))
        {
            return new ManualImportResultResponse(item.Path, item.VersionId, false, "File not found.", null, null, false);
        }

        var version = await importer.LoadVersionAsync(item.VersionId, ct);
        if (version is null)
        {
            return new ManualImportResultResponse(item.Path, item.VersionId, false, "Version not found.", null, null, false);
        }

        Grab? grab = null;
        if (item.GrabId is not null)
        {
            grab = await db.Grabs.Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == item.GrabId, ct);
            if (grab is null)
            {
                return new ManualImportResultResponse(item.Path, item.VersionId, false, "Queue item not found.", null, null, false);
            }

            if (grab.Targets.Count == 0)
            {
                grab.AddTarget(version);
            }
        }

        var parsedRelease = grab is null ? null : ReleaseMatcher.TryParse(grab.ReleaseTitle);
        var parsedFile = ImportService.TryParsePath(item.Path);
        var quality = ImportService.QualityOf(parsedRelease, parsedFile);
        if (item.QualityId is { } qualityId)
        {
            var picked = ParserQuality.All.FirstOrDefault(q => q.Id == qualityId);
            if (picked is null)
            {
                return new ManualImportResultResponse(item.Path, item.VersionId, false, $"Unknown quality {qualityId}.", null, null, false);
            }

            quality = new QualityModel(picked, quality.Revision);
        }

        var releaseTitle = grab?.ReleaseTitle ?? Path.GetFileNameWithoutExtension(item.Path);
        var plan = new ImportPlan(item.Path, version, releaseTitle, parsedRelease ?? parsedFile, quality, grab?.IndexerFlags ?? IndexerFlags.None, grab, Force: true, mode, ExtraPaths: item.ExtraPaths);
        try
        {
            var outcome = await importer.ExecuteAsync(plan, ct);
            if (outcome.Success)
            {
                logger.LogInformation("Manually imported {Path} → {Destination} for {Movie} [{Version}]", item.Path, outcome.DestinationPath, version.Movie.Title, version.Name);
            }

            return new ManualImportResultResponse(item.Path, item.VersionId, outcome.Success, outcome.Reason, outcome.File?.Id, outcome.DestinationPath, outcome.Upgraded);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual import of {Path} crashed", item.Path);
            return new ManualImportResultResponse(item.Path, item.VersionId, false, ex.Message, null, null, false);
        }
    }

    /// <summary>What the import would bring with this video; the user keeps or drops each one. Nothing when the setting is off.</summary>
    private List<ManualImportExtraResponse> ListExtras(string video, string? releasePath, IReadOnlySet<string>? extensions, string listingRoot)
    {
        if (extensions is null)
        {
            return [];
        }

        try
        {
            return ExtraFiles.Find(video, releasePath, extensions)
                .Select(c => new ManualImportExtraResponse(
                    c.Source.FullName, Path.GetRelativePath(listingRoot, c.Source.FullName), c.Name.Extension, c.Name.Kind, c.Name.Language, c.Name.Forced, c.Name.HearingImpaired))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The extra files next to {Video} could not be listed: {Message}", video, ex.Message);
            return [];
        }
    }

    /// <summary>Where the client put the download, asked to the client itself.</summary>
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
