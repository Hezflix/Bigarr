using System.Globalization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Series;

/// <summary>
/// Sonarr's "edit file" for an episode file: the release group, the quality and the languages it really has (an episode
/// file has no edition). Custom formats and the cutoff are judged at read time, so they follow at once; nothing moves on
/// disk. A hardlink of a film's file is the film's to describe: it is edited from the film. Journaled per episode as
/// <see cref="HistoryEventType.FileEdited"/>, like a rename.
/// </summary>
public sealed class EpisodeFileEditService(BigarrDbContext db, TimeProvider time)
{
    public async Task<Result<EpisodeFile>> UpdateAsync(int seriesId, int fileId, UpdateEpisodeFileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = FileFactsEdit.Validate(request.ReleaseGroup, null, request.Languages)
            ?? await FileFactsEdit.ValidateQualityAsync(request.QualityId, db, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var file = await db.EpisodeFiles
            .Include(f => f.Quality)
            .Include(f => f.Version).ThenInclude(v => v.Series)
            .Include(f => f.Episodes).ThenInclude(t => t.Episode)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.Version.SeriesId == seriesId, ct);
        if (file is null)
        {
            return Failure.NotFound("Episode file");
        }

        if (file.IsFilmLink)
        {
            return Failure.Conflict("A film's file", "This file is a hardlink of a film's file: edit the film's file and it follows.");
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
            return Result<EpisodeFile>.Ok(file);
        }

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var target in file.Episodes)
        {
            var line = GrabService.SeriesHistory(file.Version, target, HistoryEventType.FileEdited, file.OriginalReleaseTitle, null, null, now);
            line.Data["episodeFileId"] = file.Id.ToString(CultureInfo.InvariantCulture);
            foreach (var (key, value) in changes)
            {
                line.Data[key] = value;
            }

            db.History.Add(line);
        }

        // Only rows change, nothing on disk: the request's token may still cancel the save.
        await db.SaveChangesAsync(ct);
        return Result<EpisodeFile>.Ok(file);
    }
}
