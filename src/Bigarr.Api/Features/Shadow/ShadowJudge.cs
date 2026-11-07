using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Runs a release Radarr grabbed through Bigarr's own parser, film matching and decision engine (the automatic rules,
/// those of the RSS pass) for the version that follows that Radarr, against the library as it is now. Called when the
/// grab is read, before the follow brings in Radarr's file, so the version is judged with the file it had then.
/// </summary>
public sealed class ShadowJudge(BigarrDbContext db, TargetContextFactory contexts, CustomFormatScorer scorer)
{
    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Automatic();
    private CustomFormatCalculator? _calculator;

    public async Task<BigarrVerdict> JudgeAsync(int tmdbId, string versionName, ReleaseInfo release, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);

        var movie = await contexts.MoviesWithVersions().FirstOrDefaultAsync(m => m.TmdbId == tmdbId, ct);
        if (movie is null)
        {
            return Unmatched("The film was not in Bigarr's library yet.");
        }

        if (movie.Versions.FirstOrDefault(v => v.Name == versionName) is null)
        {
            return Unmatched($"The film had no {versionName} version in Bigarr.");
        }

        var parsed = ReleaseMatcher.TryParse(release.Title);
        var mismatch = ReleaseMatcher.Match(parsed, release, movie);
        _calculator ??= await scorer.CalculatorAsync(ct);
        var formats = parsed is null ? [] : _calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie.Year, movie.OriginalLanguage));
        var quality = parsed?.Quality?.Quality is { } q && q.Name != "Unknown" ? q.Name : null;
        if (parsed is null || mismatch is not null)
        {
            return new BigarrVerdict(mismatch ?? "Bigarr's parser could not read the title.", false, [], 0, formats.Select(f => f.Name).ToList(), quality, null, null, null);
        }

        var minimumSeeders = await db.Indexers.AsNoTracking().Where(i => i.Name == release.IndexerName).Select(i => (int?)i.MinimumSeeders).FirstOrDefaultAsync(ct) ?? 0;
        var candidate = new ReleaseCandidate(release, parsed, minimumSeeders) { CustomFormatIds = formats.Select(f => f.Id).ToList() };
        var decisions = (await contexts.BuildAsync([movie], ct))[movie.Id].Select(context => _evaluator.Evaluate(candidate, context)).ToList();
        var decision = decisions.First(d => ((MovieVersion)d.Target.Target).Name == versionName);
        var attributed = VersionAttribution.Choose(decisions);

        var file = decision.Target.File;
        return new BigarrVerdict(
            null,
            decision.Approved,
            decision.Rejections.Select(r => r.Reason).ToList(),
            decision.CustomFormatScore,
            formats.Select(f => f.Name).ToList(),
            quality,
            attributed is null ? null : ((MovieVersion)attributed.Target.Target).Name,
            file is null ? null : Bigarr.Parsing.Qualities.Quality.FindById(file.QualityId)?.Name,
            file is null ? null : ProfileRanking.FormatScore(decision.Target.Profile, file.CustomFormatIds));
    }

    private static BigarrVerdict Unmatched(string reason) => new(reason, false, [], 0, [], null, null, null, null);
}
