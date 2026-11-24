using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>What the preview of the new-episode message tells: the show last put on disk, or a sample one.</summary>
public static class NewEpisodePreview
{
    /// <summary>Episodes the batch preview lists.</summary>
    public const int BatchSize = 3;

    public sealed record Subject(string Show, string? Poster, List<AnnouncedEpisode> Episodes);

    public static Subject Sample { get; } = new(
        "Sample Show",
        null,
        [
            new AnnouncedEpisode(1, 2, "The Second One", "What happens in the episode, as the show's metadata tells it."),
            new AnnouncedEpisode(1, 3, "The Third One", null),
            new AnnouncedEpisode(1, 4, "The Fourth One", null),
        ]);

    /// <summary>The episodes from the one last put on disk on, in its season; the sample when there are fewer than two.</summary>
    public static async Task<Subject> SubjectAsync(BigarrDbContext db, CancellationToken ct)
    {
        var last = await db.EpisodeTargets.AsNoTracking()
            .Where(t => t.EpisodeFileId != null)
            .OrderByDescending(t => t.EpisodeFile!.DateAdded).ThenByDescending(t => t.EpisodeFileId)
            .Select(t => new { t.Episode.SeriesId, t.Episode.SeasonNumber, t.Episode.EpisodeNumber })
            .FirstOrDefaultAsync(ct);
        if (last is null)
        {
            return Sample;
        }

        var series = await db.Series.AsNoTracking().Where(s => s.Id == last.SeriesId).Select(s => new { s.Title, s.Images }).FirstOrDefaultAsync(ct);
        var episodes = await db.Episodes.AsNoTracking()
            .Where(e => e.SeriesId == last.SeriesId && e.SeasonNumber == last.SeasonNumber && e.EpisodeNumber >= last.EpisodeNumber)
            .OrderBy(e => e.EpisodeNumber)
            .Take(BatchSize)
            .Select(e => new AnnouncedEpisode(e.SeasonNumber, e.EpisodeNumber, e.Title, e.Overview))
            .ToListAsync(ct);
        if (series is null || episodes.Count < 2)
        {
            return Sample;
        }

        return new Subject(series.Title, series.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl, episodes);
    }
}
