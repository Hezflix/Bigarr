using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Common;
using Bigarr.Domain.Events;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>
/// Turns imported episode files into announcements, in the outbox dispatcher's own save: an import is collected
/// exactly once, with the event, or not at all. Upgrades and episodes already announced (another version, a second
/// download) are left out; an episode joins its show's announcement while that one is still collecting.
/// </summary>
public static class NewEpisodeCollector
{
    /// <summary>Adds the events' new episodes to the tracked announcements; the caller saves. Nothing without a bot token.</summary>
    public static async Task<int> CollectAsync(BigarrDbContext db, IEnumerable<DomainEvent?> events, DateTime now, NewEpisodeOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        var imports = events.OfType<EpisodeFileImported>().Where(e => !e.IsUpgrade && e.Episodes.Count > 0).ToList();
        if (imports.Count == 0)
        {
            return 0;
        }

        var configured = await db.InstanceSettings.AsNoTracking()
            .AnyAsync(s => s.Id == InstanceSettings.SingletonId && s.DiscordBotToken != null && s.DiscordBotToken != string.Empty, ct);
        if (!configured)
        {
            return 0;
        }

        var episodeIds = imports.SelectMany(e => e.Episodes).Select(e => e.Id).Distinct().ToList();
        var announced = (await db.NewEpisodeAnnouncementEpisodes.AsNoTracking().Where(e => episodeIds.Contains(e.EpisodeId)).Select(e => e.EpisodeId).ToListAsync(ct)).ToHashSet();

        var seriesIds = imports.Select(e => e.Series.Id).Distinct().ToList();
        var openSince = now - options.MaxBatch;
        var open = await db.NewEpisodeAnnouncements
            .Where(a => a.ReadyAt == null && seriesIds.Contains(a.SeriesId) && a.CreatedAt > openSince)
            .OrderByDescending(a => a.Id)
            .ToListAsync(ct);

        var added = 0;
        foreach (var import in imports)
        {
            foreach (var episode in import.Episodes.Where(e => announced.Add(e.Id)))
            {
                var announcement = open.FirstOrDefault(a => a.SeriesId == import.Series.Id);
                if (announcement is null)
                {
                    announcement = new NewEpisodeAnnouncement { SeriesId = import.Series.Id, CreatedAt = now };
                    db.NewEpisodeAnnouncements.Add(announcement);
                    open.Insert(0, announcement);
                }

                announcement.LastAddedAt = now;
                announcement.Episodes.Add(new NewEpisodeAnnouncementEpisode
                {
                    EpisodeId = episode.Id,
                    SeasonNumber = episode.SeasonNumber,
                    EpisodeNumber = episode.EpisodeNumber,
                    Title = string.IsNullOrWhiteSpace(episode.Title) ? null : episode.Title,
                    Overview = string.IsNullOrWhiteSpace(episode.Overview) ? null : episode.Overview,
                    FilePath = import.File.Path,
                });
                added++;
            }
        }

        return added;
    }
}
