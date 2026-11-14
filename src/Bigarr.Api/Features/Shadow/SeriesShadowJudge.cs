using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// The series twin of <see cref="ShadowJudge"/>: runs a release Sonarr grabbed through Bigarr's own episode parser, series
/// matching, episode mapping and decision engine (the automatic rules, those of the RSS pass but its "no proper for an old
/// file": Sonarr may have grabbed from a search) for the series version that follows that Sonarr. Called when the grab
/// is read, before the follow brings in Sonarr's files, so each episode is judged with the file it had then, or with
/// Bigarr's own latest pick made before Sonarr grabbed.
/// </summary>
public sealed class SeriesShadowJudge(BigarrDbContext db, SeriesTargetsFactory targetsFactory, CustomFormatScorer scorer)
{
    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Automatic();

    public async Task<BigarrVerdict> JudgeAsync(int tvdbId, string versionName, ReleaseInfo release, DateTime grabbedAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);

        var seriesId = await db.Series.AsNoTracking().Where(s => s.TvdbId == tvdbId).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (seriesId is null)
        {
            return Unmatched("The series was not in Bigarr's library yet.");
        }

        var targets = (await targetsFactory.LoadAsync([seriesId.Value], grabbedAt, ct)).GetValueOrDefault(seriesId.Value);
        var version = targets?.Series.Versions.FirstOrDefault(v => v.Name == versionName);
        if (targets is null || version is null)
        {
            return Unmatched($"The series had no {versionName} version in Bigarr.");
        }

        var series = targets.Series;
        var parsed = SeriesReleaseJudge.TryParse(release.Title);
        var index = new SeriesTitleIndex([targets.Indexed], targets.SceneNames);
        int? sceneSeason = null;
        var special = false;
        if ((parsed is null || parsed.IsPossibleSpecialEpisode) && SeriesReleaseJudge.ParseSpecial(parsed, release.Title, targets) is { } byTitle)
        {
            parsed = byTitle;
            special = true;
        }

        if (parsed is null)
        {
            return Unmatched("Bigarr's parser could not read the title as an episode.");
        }

        if (!special)
        {
            var match = index.Find(parsed, release.TvdbId is { } t && t == series.TvdbId ? t : null);
            if (match.Series is null)
            {
                return Unmatched($"Title '{parsed.SeriesTitle}' does not match {series.Title} in Bigarr.");
            }

            sceneSeason = match.SceneSeasonNumber;
        }

        var calculator = await scorer.CalculatorAsync(ct);
        var minimumSeeders = await db.Indexers.AsNoTracking().Where(i => i.Name == release.IndexerName).Select(i => (int?)i.MinimumSeeders).FirstOrDefaultAsync(ct) ?? 0;
        var judgement = SeriesReleaseJudge.Judge(release, parsed, targets, sceneSeason, calculator, _evaluator, minimumSeeders, sceneSource: !special);
        var quality = parsed.Quality?.Quality is { } q && q.Name != "Unknown" ? q.Name : null;
        if (!judgement.IsMapped)
        {
            return new BigarrVerdict(judgement.UnmatchedReason ?? "No episode of the series.", false, [], 0, judgement.Formats.Select(f => f.Name).ToList(), quality, null, null, null);
        }

        var label = SeriesReleaseJudge.Label(judgement.Episodes);
        var decision = judgement.Decisions.FirstOrDefault(d => ((EpisodeTarget)d.Target.Target).VersionId == version.Id);
        if (decision is null && judgement.Episodes.FirstOrDefault(e => targets.FilmOwned.ContainsKey(e.Id)) is { } owned)
        {
            return new BigarrVerdict($"{label} is the film {targets.FilmOwned[owned.Id]}: the film engine picks for it (paired with the film's picks).", false, [], 0, judgement.Formats.Select(f => f.Name).ToList(), quality, null, null, null, Episodes: label);
        }

        if (decision is null)
        {
            return new BigarrVerdict($"The {versionName} version has no target for {label}.", false, [], 0, judgement.Formats.Select(f => f.Name).ToList(), quality, null, null, null, Episodes: label);
        }

        var attributed = judgement.Attributed is { } a ? series.Versions.FirstOrDefault(v => v.Id == ((EpisodeTarget)a.Target.Target).VersionId)?.Name : null;
        var file = decision.Target.Episodes?.Slots.Select(s => s.File).FirstOrDefault(f => f is not null);
        return new BigarrVerdict(
            null,
            decision.Approved,
            decision.Rejections.Select(r => r.Reason).ToList(),
            decision.CustomFormatScore,
            judgement.Formats.Select(f => f.Name).ToList(),
            quality,
            attributed,
            file is null ? null : Bigarr.Parsing.Qualities.Quality.FindById(file.QualityId)?.Name,
            file is null ? null : ProfileRanking.FormatScore(decision.Target.Profile, file.CustomFormatIds),
            file?.IsPick ?? false,
            label);
    }

    private static BigarrVerdict Unmatched(string reason) => new(reason, false, [], 0, [], null, null, null, null);
}
