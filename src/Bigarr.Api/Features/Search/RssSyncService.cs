using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// The main loop (blueprint §5): one feed request per indexer, every new release parsed, matched to a film of the
/// library and judged by each of its versions with the automatic rules, then attributed to one version. Grabbing the
/// attributed version arrives with the download client (M4); until then the outcome is shown as "would grab".
/// </summary>
public sealed class RssSyncService(
    BigarrDbContext db,
    TargetContextFactory contexts,
    CustomFormatScorer scorer,
    IndexerGateway gateway,
    RssFeedState state,
    IOptions<RssSyncOptions> options,
    TimeProvider time,
    ILogger<RssSyncService> logger)
{
    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Automatic();

    public RssStatusResponse Status() => state.Status(options.Value);

    public async Task<Result<RssSyncResponse>> RunAsync(CancellationToken ct)
    {
        if (!state.TryBeginRun())
        {
            return Failure.Conflict("RSS sync already running", "Wait for the current pass to finish.");
        }

        try
        {
            return Result<RssSyncResponse>.Ok(await RunCoreAsync(ct));
        }
        finally
        {
            state.EndRun(time.GetUtcNow().UtcDateTime);
        }
    }

    private async Task<RssSyncResponse> RunCoreAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        state.Prune(now);

        var indexers = await db.Indexers.AsNoTracking().Where(i => i.EnableRss).OrderBy(i => i.Priority).ToListAsync(ct);
        if (indexers.Count == 0)
        {
            logger.LogDebug("RSS sync: no indexer has RSS enabled");
            return new RssSyncResponse(0, 0, 0, 0, 0, []);
        }

        var results = await IndexerQueries.RunAsync(indexers, (indexer, token) => gateway.FetchRecentAsync(indexer, token), ct);

        var fresh = new List<(Indexer Indexer, ReleaseInfo Release)>();
        foreach (var result in results)
        {
            var newCount = 0;
            foreach (var release in result.Releases)
            {
                if (state.MarkSeen(result.Indexer.Id, release.Guid, now))
                {
                    fresh.Add((result.Indexer, release));
                    newCount++;
                }
            }

            state.RecordIndexer(new RssIndexerStatusResponse(result.Indexer.Id, result.Indexer.Name, now, result.Error?.Message, result.Releases.Count, newCount));
        }

        var matched = 0;
        var wouldGrab = 0;
        if (fresh.Count > 0)
        {
            var movies = await contexts.MoviesWithVersions().ToListAsync(ct);
            var library = LibraryIndex.Build(movies);
            var targets = await contexts.BuildAsync(movies, ct);
            var calculator = await scorer.CalculatorAsync(ct);

            // Oldest first so the newest ends up on top of the list.
            foreach (var (indexer, release) in fresh.OrderBy(f => f.Release.PublishDate))
            {
                var entry = Judge(indexer, release, library, targets, calculator, now);
                if (entry.Movie is not null)
                {
                    matched++;
                }

                if (entry.AttributedVersionId is not null)
                {
                    wouldGrab++;
                }

                state.Add(entry);
            }
        }

        var errors = results.Where(r => r.Error is not null)
            .Select(r => new IndexerErrorResponse(r.Indexer.Id, r.Indexer.Name, r.Error!.Kind, r.Error.Message))
            .ToList();

        logger.LogInformation("RSS sync: {Indexers} indexer(s), {Fetched} release(s), {New} new, {Matched} matched, {WouldGrab} would be grabbed, {Errors} error(s)",
            indexers.Count, results.Sum(r => r.Releases.Count), fresh.Count, matched, wouldGrab, errors.Count);

        return new RssSyncResponse(indexers.Count, results.Sum(r => r.Releases.Count), fresh.Count, matched, wouldGrab, errors);
    }

    private RssReleaseResponse Judge(Indexer indexer, ReleaseInfo release, LibraryIndex library, Dictionary<int, List<TargetContext>> targets, CustomFormatCalculator calculator, DateTime now)
    {
        var parsed = ReleaseMatcher.TryParse(release.Title);
        var (movie, reason) = ReleaseMatcher.FindMovie(parsed, release, library);
        var formats = parsed is null ? [] : calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie?.Year, movie?.OriginalLanguage));

        var versions = new List<VersionDecisionResponse>();
        ReleaseDecision? attributed = null;
        if (movie is not null && parsed is not null)
        {
            var candidate = new ReleaseCandidate(release, parsed, indexer.MinimumSeeders) { CustomFormatIds = formats.Select(f => f.Id).ToList() };
            var decisions = targets[movie.Id].Select(context => _evaluator.Evaluate(candidate, context)).ToList();
            attributed = VersionAttribution.Choose(decisions);
            versions = decisions.Select(d =>
            {
                var version = (MovieVersion)d.Target.Target;
                return new VersionDecisionResponse(version.Id, version.Name, d.Approved, d.Rejections.Select(r => new RejectionResponse(r.Reason, r.Type == RejectionType.Temporary)).ToList(), d.CustomFormatScore);
            }).ToList();
        }

        var quality = parsed?.Quality?.Quality ?? ParserQuality.Unknown;
        var revision = parsed?.Quality?.Revision;
        var attributedVersion = attributed is null ? null : (MovieVersion)attributed.Target.Target;

        return new RssReleaseResponse(
            release.Guid,
            release.Title,
            release.IndexerId,
            release.IndexerName,
            release.Size,
            release.Seeders,
            release.Leechers,
            release.PublishDate,
            now,
            release.InfoUrl,
            release.Flags,
            quality.Id,
            quality.Name,
            revision?.Version ?? 1,
            revision?.IsRepack ?? false,
            string.IsNullOrWhiteSpace(parsed?.Edition) ? null : parsed!.Edition,
            string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup,
            parsed?.Languages?.Where(l => l.Id != 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [],
            formats.Select(f => f.Name).ToList(),
            parsed?.PrimaryMovieTitle,
            parsed is { Year: > 0 } ? parsed.Year : null,
            movie is null ? null : new RssMovieRef(movie.Id, movie.Title, movie.Year, movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl),
            reason,
            versions,
            attributedVersion?.Id,
            attributedVersion?.Name);
    }
}

/// <summary>Runs the RSS pass on the configured interval, after a grace period at startup.</summary>
public sealed class RssSyncRunner(IServiceScopeFactory scopeFactory, RssFeedState state, IOptions<RssSyncOptions> options, TimeProvider time, ILogger<RssSyncRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("RSS sync disabled (Bigarr:Rss:Enabled = false)");
            return;
        }

        var initialDelay = TimeSpan.FromSeconds(Math.Max(0, settings.InitialDelaySeconds));
        state.NextRunAt = time.GetUtcNow().UtcDateTime + initialDelay;
        logger.LogInformation("RSS sync every {Interval} minutes, first pass in {Delay} seconds", settings.Interval.TotalMinutes, initialDelay.TotalSeconds);

        try
        {
            await Task.Delay(initialDelay, time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<RssSyncService>().RunAsync(stoppingToken);
                    if (!result.IsSuccess)
                    {
                        logger.LogDebug("Scheduled RSS pass skipped: {Title}", result.Failure!.Title);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "RSS sync pass crashed");
                }

                state.NextRunAt = time.GetUtcNow().UtcDateTime + settings.Interval;
                await Task.Delay(settings.Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
