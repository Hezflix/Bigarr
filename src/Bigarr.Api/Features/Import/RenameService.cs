using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Naming;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Import;

/// <summary>Renames a film's files to the current template: preview first, then the move, journaled as <see cref="HistoryEventType.Renamed"/>.</summary>
public sealed class RenameService(BigarrDbContext db, MediaManagementService settings, CustomFormatScorer scorer, OutboxWriter outbox, TimeProvider time, ILogger<RenameService> logger)
{
    public async Task<Result<List<RenamePreviewResponse>>> PreviewAsync(int movieId, CancellationToken ct)
    {
        var movie = await LoadAsync(movieId, tracking: false, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        var config = await settings.GetAsync(ct);
        var rows = new List<RenamePreviewResponse>();
        foreach (var version in movie.Versions.Where(v => v.File is not null).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
        {
            var file = version.File!;
            var target = await TargetNameAsync(movie, version, config.FileNameTemplate, ct, config.FilmNameCleaning);
            var changed = !string.Equals(target, file.RelativePath, StringComparison.Ordinal);
            var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
            var extras = changed
                ? Extras(file).Zip(ExtraTargets(file, folder, target), (extra, to) => new ExtraRenamePreviewResponse(extra.Id, extra.RelativePath, to)).ToList()
                : Extras(file).Select(extra => new ExtraRenamePreviewResponse(extra.Id, extra.RelativePath, extra.RelativePath)).ToList();
            rows.Add(new RenamePreviewResponse(version.Id, version.Name, file.Id, file.RelativePath, target, changed, extras));
        }

        return Result<List<RenamePreviewResponse>>.Ok(rows);
    }

    public async Task<Result<List<RenameResultResponse>>> RenameAsync(RenameRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var movie = await LoadAsync(request.MovieId, tracking: true, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        var config = await settings.GetAsync(ct);
        var wanted = request.MediaFileIds?.ToHashSet();
        var results = new List<RenameResultResponse>();

        // Every target is worked out before the first move: from there on nothing waits on the request any more, so a
        // caller that went away cannot leave files renamed on disk and the old names in the database.
        var planned = new List<(MovieVersion Version, string Target)>();
        foreach (var version in movie.Versions.Where(v => v.File is not null && (wanted is null || wanted.Contains(v.File.Id))))
        {
            planned.Add((version, await TargetNameAsync(movie, version, config.FileNameTemplate, ct, config.FilmNameCleaning)));
        }

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var (version, target) in planned)
        {
            var file = version.File!;
            if (string.Equals(target, file.RelativePath, StringComparison.Ordinal))
            {
                results.Add(new RenameResultResponse(file.Id, file.RelativePath, false, null));
                continue;
            }

            var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
            var from = Path.Combine(folder, file.RelativePath);
            var to = Path.Combine(folder, target);
            var extraTargets = ExtraTargets(file, folder, target);
            try
            {
                if (!File.Exists(from))
                {
                    results.Add(new RenameResultResponse(file.Id, file.RelativePath, false, "The file is missing on disk."));
                    continue;
                }

                if (File.Exists(to))
                {
                    results.Add(new RenameResultResponse(file.Id, file.RelativePath, false, $"A file already exists at {to}."));
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Rename of {From} failed", from);
                results.Add(new RenameResultResponse(file.Id, file.RelativePath, false, ex.Message));
                continue;
            }

            var previous = file.RelativePath;
            file.RelativePath = target;
            var extrasRenamed = RenameExtras(file, folder, extraTargets);
            var data = new Dictionary<string, string> { ["from"] = from, ["to"] = to };
            if (extrasRenamed > 0)
            {
                data["extras"] = extrasRenamed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            db.History.Add(new HistoryEvent
            {
                TargetId = version.Id,
                MovieId = movie.Id,
                TmdbId = movie.TmdbId,
                MovieTitle = movie.Title,
                VersionName = version.Name,
                EventType = HistoryEventType.Renamed,
                SourceTitle = file.OriginalReleaseTitle,
                MediaFile = file,
                Data = data,
                CreatedAt = now,
            });
            results.Add(new RenameResultResponse(file.Id, target, true, null));
            outbox.Publish(new FileRenamed(MovieRef.From(movie), VersionRef.From(version), FileRef.From(file, to), from));
            logger.LogInformation("Renamed {From} → {To}", previous, target);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return Result<List<RenameResultResponse>>.Ok(results);
    }

    /// <summary>The relative path the template gives this file today, extension kept.</summary>
    public async Task<string> TargetNameAsync(Movie movie, MovieVersion version, string template, CancellationToken ct, NameCleaning? cleaning = null)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentNullException.ThrowIfNull(version);
        var file = version.File ?? throw new InvalidOperationException("Version has no file.");

        var calculator = await scorer.CalculatorAsync(ct);
        var matched = calculator.Match(CustomFormatScorer.FileInput(
            file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
        var context = new NamingContext(movie.Title, movie.OriginalTitle, movie.Year, movie.TmdbId, movie.ImdbId, version.Name)
        {
            QualityName = file.Quality?.Name ?? $"#{file.QualityId}",
            RevisionVersion = file.Revision.Version,
            RevisionReal = file.Revision.Real,
            IsRepack = file.Revision.IsRepack,
            Edition = file.Edition,
            ReleaseGroup = file.ReleaseGroup,
            Languages = file.Languages,
            MediaInfo = file.MediaInfo,
            CustomFormats = matched.Where(m => m.IncludeWhenRenaming).Select(m => m.Name).ToList(),
            OriginalReleaseTitle = file.OriginalReleaseTitle,
            OriginalFileName = Path.GetFileNameWithoutExtension(file.RelativePath),
        };
        return NamingTemplate.Render(template, context, cleaning) + Path.GetExtension(file.RelativePath).ToLowerInvariant();
    }

    /// <summary>Extras in a stable order, so their numbering does not depend on the database's mood.</summary>
    private static IEnumerable<ExtraFile> Extras(MediaFile file) => file.Extras.OrderBy(e => e.RelativePath, StringComparer.Ordinal);

    /// <summary>
    /// Where the file's extras go when it becomes <paramref name="target"/>: its new name with their suffixes, numbered
    /// around files already there. Their own current paths do not count as taken.
    /// </summary>
    private static List<string> ExtraTargets(MediaFile file, string folder, string target)
    {
        var extras = Extras(file).ToList();
        if (extras.Count == 0)
        {
            return [];
        }

        var own = extras.Select(e => Path.GetFullPath(Path.Combine(folder, e.RelativePath))).ToHashSet(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var destination = Path.GetFullPath(Path.Combine(folder, target));
        return ExtraFiles.Destinations(extras.Select(ExtraFileName.Of).ToList(), Path.GetDirectoryName(destination)!, Path.GetFileNameWithoutExtension(destination), p => File.Exists(p) && !own.Contains(Path.GetFullPath(p)))
            .Select(p => Path.GetRelativePath(Path.GetFullPath(folder), p))
            .ToList();
    }

    /// <summary>Moves the extras to <paramref name="targets"/> once their movie file moved. One that cannot move keeps its name, with a warning.</summary>
    private int RenameExtras(MediaFile file, string folder, List<string> targets)
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

    private Task<Movie?> LoadAsync(int movieId, bool tracking, CancellationToken ct)
    {
        var query = db.Movies
            .Include(m => m.Versions).ThenInclude(v => v.RootFolder)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Extras)
            .Include(m => m.Versions).ThenInclude(v => v.Tags)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(m => m.Id == movieId, ct);
    }
}
