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
            var target = await TargetNameAsync(movie, version, config.FileNameTemplate, ct);
            rows.Add(new RenamePreviewResponse(version.Id, version.Name, version.File!.Id, version.File.RelativePath, target, !string.Equals(target, version.File.RelativePath, StringComparison.Ordinal)));
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
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var version in movie.Versions.Where(v => v.File is not null && (wanted is null || wanted.Contains(v.File.Id))))
        {
            var file = version.File!;
            var target = await TargetNameAsync(movie, version, config.FileNameTemplate, ct);
            if (string.Equals(target, file.RelativePath, StringComparison.Ordinal))
            {
                results.Add(new RenameResultResponse(file.Id, file.RelativePath, false, null));
                continue;
            }

            var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
            var from = Path.Combine(folder, file.RelativePath);
            var to = Path.Combine(folder, target);
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
                Data = new Dictionary<string, string> { ["from"] = from, ["to"] = to },
                CreatedAt = now,
            });
            results.Add(new RenameResultResponse(file.Id, target, true, null));
            outbox.Publish(new FileRenamed(MovieRef.From(movie), VersionRef.From(version), FileRef.From(file, to), from));
            logger.LogInformation("Renamed {From} → {To}", previous, target);
        }

        await db.SaveChangesAsync(ct);
        return Result<List<RenameResultResponse>>.Ok(results);
    }

    /// <summary>The relative path the template gives this file today, extension kept.</summary>
    public async Task<string> TargetNameAsync(Movie movie, MovieVersion version, string template, CancellationToken ct)
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
        return NamingTemplate.Render(template, context) + Path.GetExtension(file.RelativePath).ToLowerInvariant();
    }

    private Task<Movie?> LoadAsync(int movieId, bool tracking, CancellationToken ct)
    {
        var query = db.Movies
            .Include(m => m.Versions).ThenInclude(v => v.RootFolder)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .Include(m => m.Versions).ThenInclude(v => v.Tags)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(m => m.Id == movieId, ct);
    }
}
