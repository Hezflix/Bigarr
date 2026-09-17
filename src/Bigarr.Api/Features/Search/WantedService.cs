using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// What the library still wants (blueprint §5, job cutoff unmet): managed monitored versions, available, without an
/// active download, that have no file or a file below their profile's cutoff. Computed, not stored (§8).
/// </summary>
public sealed class WantedService(BigarrDbContext db, TargetContextFactory contexts, CustomFormatScorer scorer, IOptions<SearchOptions> options, TimeProvider time)
{
    public async Task<List<WantedItemResponse>> AllAsync(WantedFilter? filter, CancellationToken ct)
    {
        var movies = await contexts.MoviesWithVersions().ToListAsync(ct);
        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var calculator = await scorer.CalculatorAsync(ct);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var settings = options.Value;

        var items = new List<WantedItemResponse>();
        foreach (var movie in movies.OrderBy(m => m.SortTitle).ThenBy(m => m.Year))
        {
            foreach (var version in movie.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
            {
                if (version.Mode != VersionMode.Managed || !version.Monitored || active.Contains(version.Id) || !Availability.IsAvailable(movie, version.MinimumAvailability, today))
                {
                    continue;
                }

                var profile = version.QualityProfile;
                WantedFilter kind;
                string? currentQuality = null;
                int? currentScore = null;
                if (version.File is null)
                {
                    kind = WantedFilter.Missing;
                }
                else
                {
                    if (!profile.UpgradeAllowed)
                    {
                        continue;
                    }

                    var file = version.File;
                    var matched = calculator.Match(CustomFormatScorer.FileInput(file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
                    currentScore = ProfileRanking.FormatScore(profile, matched.Select(m => m.Id));
                    currentQuality = file.Quality?.Name ?? definitions.GetValueOrDefault(file.QualityId)?.Name;
                    if (ProfileRanking.CutoffMet(profile, file.QualityId) && currentScore >= profile.CutoffFormatScore)
                    {
                        continue;
                    }

                    kind = WantedFilter.CutoffUnmet;
                }

                if (filter is not null && filter != kind)
                {
                    continue;
                }

                items.Add(new WantedItemResponse(
                    movie.Id,
                    movie.Title,
                    movie.Year,
                    movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                    version.Id,
                    version.Name,
                    profile.Name,
                    kind,
                    currentQuality,
                    currentScore,
                    CutoffName(profile, definitions),
                    version.LastSearchAt,
                    version.SearchAttempts,
                    SearchBackoff.NextAllowed(version.LastSearchAt, version.SearchAttempts, settings)));
            }
        }

        return items;
    }

    public async Task<WantedPageResponse> PageAsync(WantedFilter? filter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var all = await AllAsync(filter, ct);
        return new WantedPageResponse(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count, page, pageSize);
    }

    /// <summary>The versions the cutoff-unmet job searches this pass: due ones, the longest-unsearched first, bounded.</summary>
    public async Task<List<WantedItemResponse>> DueAsync(int batchSize, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var all = await AllAsync(null, ct);
        return all
            .Where(w => w.NextSearchAt is null || w.NextSearchAt <= now)
            .OrderBy(w => w.LastSearchAt ?? DateTime.MinValue)
            .ThenBy(w => w.MovieId)
            .Take(Math.Max(1, batchSize))
            .ToList();
    }

    public static string CutoffName(QualityProfile profile, IReadOnlyDictionary<int, QualityDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var group = profile.Items.FirstOrDefault(i => i.GroupId == profile.Cutoff);
        if (group is not null)
        {
            return group.GroupName ?? string.Join(" / ", group.QualityIds.Select(q => definitions.GetValueOrDefault(q)?.Name ?? $"#{q}"));
        }

        return definitions.GetValueOrDefault(profile.Cutoff)?.Name ?? $"#{profile.Cutoff}";
    }
}

/// <summary>Searches what is wanted and due, a bounded batch per pass (blueprint §5, M6).</summary>
public sealed class CutoffUnmetSearchJob(WantedService wanted, AutomaticSearchService search, IOptions<SearchOptions> options) : IScheduledJob
{
    public const string JobName = "cutoffUnmetSearch";

    public string Name => JobName;

    public string DisplayName => "Search missing and cutoff unmet";

    public TimeSpan Interval => options.Value.CutoffUnmetInterval;

    public bool Enabled => options.Value.CutoffUnmetEnabled;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(Math.Max(0, options.Value.CutoffUnmetInitialDelayMinutes));

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var due = await wanted.DueAsync(options.Value.CutoffUnmetBatchSize, ct);
        if (due.Count == 0)
        {
            return "Nothing due";
        }

        var results = await search.SearchAsync(due.Select(d => d.VersionId).ToList(), SearchTrigger.CutoffUnmet, ct);
        var grabbed = results.Count(r => r.Outcome == AutomaticSearchOutcome.Grabbed);
        var noIndexer = results.Count(r => r.Outcome == AutomaticSearchOutcome.NoIndexer);
        return noIndexer == results.Count
            ? $"{results.Count} version(s) due, no indexer with automatic search"
            : $"{results.Count} version(s) searched, {grabbed} grabbed";
    }
}
