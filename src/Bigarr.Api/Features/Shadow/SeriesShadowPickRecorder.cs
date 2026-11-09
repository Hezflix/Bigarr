using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Bigarr.Parsing.Languages;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Where a series grab ends in shadow mode: the release is written down for the series version with every episode it
/// covers and what Bigarr made of it, and nothing else happens. One row per version and release; picking it again only
/// moves <see cref="SeriesShadowPick.LastPickedAt"/>.
/// </summary>
public sealed class SeriesShadowPickRecorder(BigarrDbContext db, CustomFormatScorer scorer, TimeProvider time, ILogger<SeriesShadowPickRecorder> logger)
{
    /// <param name="version">Loaded with its series and its profile's format scores.</param>
    /// <param name="targets">The (version, episode) targets the release covers, with their episodes.</param>
    public async Task<SeriesShadowPick> RecordAsync(SeriesVersion version, IReadOnlyList<EpisodeTarget> targets, GrabRequest request, string trigger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(request);

        var now = time.GetUtcNow().UtcDateTime;
        var guid = string.IsNullOrWhiteSpace(request.Guid) ? request.Title : request.Guid.Trim();
        var existing = await db.SeriesShadowPicks.FirstOrDefaultAsync(p => p.VersionId == version.Id && p.ReleaseGuid == guid, ct);
        if (existing is not null)
        {
            existing.LastPickedAt = now;
            existing.Picks++;
            await db.SaveChangesAsync(ct);
            logger.LogDebug("Shadow mode: {Title} picked again for {Series} [{Version}] ({Trigger})", request.Title, version.Series.Title, version.Name, trigger);
            return existing;
        }

        var indexer = request.IndexerId > 0
            ? await db.Indexers.AsNoTracking().Where(i => i.Id == request.IndexerId).Select(i => new { i.Id, i.Name }).FirstOrDefaultAsync(ct)
            : null;
        var parsed = SeriesReleaseJudge.TryParse(request.Title);
        var quality = parsed?.Quality?.Quality;
        var hasQuality = quality is not null && quality.Id != 0;
        var revision = parsed?.Quality?.Revision;
        var series = version.Series;

        var formats = new List<string>();
        var score = 0;
        if (parsed is not null)
        {
            var calculator = await scorer.CalculatorAsync(ct);
            var release = new Bigarr.Domain.Indexers.ReleaseInfo { Guid = guid, Title = request.Title, IndexerName = indexer?.Name ?? request.Indexer ?? string.Empty, Size = request.Size, Flags = request.Flags };
            var matched = calculator.Match(CustomFormatScorer.EpisodeReleaseInput(parsed, release, series.Year, series.OriginalLanguage));
            formats = matched.Select(m => m.Name).ToList();
            score = ProfileRanking.FormatScore(version.QualityProfile, matched.Select(m => m.Id));
        }

        var languages = (parsed?.Languages ?? []).Where(l => l.Id != Language.Unknown.Id).Select(l => l.Name).Distinct(StringComparer.Ordinal).ToList();

        var pick = new SeriesShadowPick
        {
            VersionId = version.Id,
            SeriesId = version.SeriesId,
            TvdbId = series.TvdbId,
            SeriesTitle = series.Title,
            VersionName = version.Name,
            Trigger = trigger,
            PickedAt = now,
            LastPickedAt = now,
            ReleaseTitle = request.Title,
            ReleaseGuid = guid,
            InfoHash = InfoHashes.Normalize(request.InfoHash),
            IndexerId = indexer?.Id,
            IndexerName = indexer?.Name ?? request.Indexer,
            Size = request.Size,
            Seeders = request.Seeders,
            IndexerFlags = request.Flags,
            PublishDate = request.PublishDate,
            Quality = hasQuality ? quality!.Name : null,
            QualityId = hasQuality ? quality!.Id : null,
            RevisionVersion = hasQuality ? revision?.Version ?? 1 : null,
            RevisionReal = hasQuality ? revision?.Real ?? 0 : null,
            IsRepack = revision?.IsRepack ?? false,
            ReleaseGroup = string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup,
            Languages = languages,
            CustomFormats = formats,
            CustomFormatScore = score,
            FullSeason = parsed?.FullSeason ?? false,
            Episodes = targets
                .OrderBy(t => t.Episode.SeasonNumber).ThenBy(t => t.Episode.EpisodeNumber)
                .Select(t => new SeriesShadowPickEpisode
                {
                    TargetId = t.Id,
                    EpisodeId = t.EpisodeId,
                    SeasonNumber = t.Episode.SeasonNumber,
                    EpisodeNumber = t.Episode.EpisodeNumber,
                    AbsoluteEpisodeNumber = t.Episode.AbsoluteEpisodeNumber,
                })
                .ToList(),
        };
        db.SeriesShadowPicks.Add(pick);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another path picked the same release for the same version in between: that row stands.
            db.Entry(pick).State = EntityState.Detached;
            foreach (var episode in pick.Episodes)
            {
                db.Entry(episode).State = EntityState.Detached;
            }

            var raced = await db.SeriesShadowPicks.FirstOrDefaultAsync(p => p.VersionId == version.Id && p.ReleaseGuid == guid, ct);
            if (raced is null)
            {
                throw;
            }

            return raced;
        }

        logger.LogInformation("Shadow mode: {Title} picked for {Series} [{Version}] {Episodes} ({Trigger}); nothing sent to a download client",
            request.Title, series.Title, version.Name, SeriesReleaseJudge.Label(targets.Select(t => t.Episode).ToList()), trigger);
        return pick;
    }
}
