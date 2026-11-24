using Bigarr.Api.Features.Library;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Movies;

/// <summary>
/// Radarr's "edit file" for a film: the release group, the edition, the quality and the languages the file really has.
/// Custom formats and the cutoff are judged at read time, so they follow at once. Nothing moves on disk; the episode files
/// that are hardlinks of this one (specials as films) take the same facts, as the link keeper gave them at link time.
/// Journaled as <see cref="HistoryEventType.FileEdited"/>, <c>before → after</c> for each fact that changed.
/// </summary>
public sealed class MovieFileEditService(BigarrDbContext db, TimeProvider time)
{
    public async Task<Result<MovieVersion>> UpdateAsync(int movieId, int versionId, UpdateMediaFileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = FileFactsEdit.Validate(request.ReleaseGroup, request.Edition, request.Languages)
            ?? await FileFactsEdit.ValidateQualityAsync(request.QualityId, db, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var version = await db.MovieVersions
            .Include(v => v.Movie)
            .Include(v => v.File).ThenInclude(f => f!.Quality)
            .FirstOrDefaultAsync(v => v.Id == versionId && v.MovieId == movieId, ct);
        if (version is null)
        {
            return Failure.NotFound("Version");
        }

        if (version.File is not { } file)
        {
            return Failure.NotFound("File");
        }

        var changes = new Dictionary<string, string>();
        if (request.ReleaseGroup is not null)
        {
            var group = FileFactsEdit.Text(request.ReleaseGroup);
            if (FileFactsEdit.Record(changes, "releaseGroup", file.ReleaseGroup, group))
            {
                file.ReleaseGroup = group;
            }
        }

        if (request.Edition is not null)
        {
            var edition = FileFactsEdit.Text(request.Edition);
            if (FileFactsEdit.Record(changes, "edition", file.Edition, edition))
            {
                file.Edition = edition;
            }
        }

        if (request.QualityId is { } qualityId && qualityId != file.QualityId)
        {
            var quality = await db.QualityDefinitions.FirstAsync(q => q.Id == qualityId, ct);
            FileFactsEdit.Record(changes, "quality", file.Quality.Name, quality.Name);
            file.QualityId = qualityId;
            file.Quality = quality;
        }

        if (request.Languages is not null)
        {
            var languages = FileFactsEdit.Languages(request.Languages);
            if (FileFactsEdit.Record(changes, "languages", FileFactsEdit.Line(file.Languages), FileFactsEdit.Line(languages)))
            {
                file.Languages = languages;
            }
        }

        if (changes.Count == 0)
        {
            return Result<MovieVersion>.Ok(version);
        }

        var linked = await db.EpisodeFiles.Where(f => f.LinkedMediaFileId == file.Id).ToListAsync(ct);
        foreach (var copy in linked)
        {
            copy.ReleaseGroup = file.ReleaseGroup;
            copy.QualityId = file.QualityId;
            copy.Languages = [.. file.Languages];
        }

        var movie = version.Movie;
        db.History.Add(new HistoryEvent
        {
            TargetId = version.Id,
            MovieId = movie.Id,
            TmdbId = movie.TmdbId,
            MovieTitle = movie.Title,
            VersionName = version.Name,
            EventType = HistoryEventType.FileEdited,
            SourceTitle = file.OriginalReleaseTitle,
            MediaFile = file,
            Data = changes,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });

        // Only rows change, nothing on disk: the request's token may still cancel the save.
        await db.SaveChangesAsync(ct);
        return Result<MovieVersion>.Ok(version);
    }
}
