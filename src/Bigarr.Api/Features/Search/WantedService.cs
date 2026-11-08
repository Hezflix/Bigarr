using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// What the library still wants (blueprint §5, job cutoff unmet): managed monitored versions, available, without an
/// active download, that have no file or a file below their profile's cutoff. Computed, not stored (§8).
/// SQL keeps the versions that can be wanted, as light rows; availability and the cutoff are judged in memory. A file's
/// custom formats are matched only when its score decides the verdict or is shown, and remembered between requests
/// (<see cref="FileFormatMatches"/>): judging every file again for each page made the list slow on a large library.
/// </summary>
public sealed class WantedService(BigarrDbContext db, CustomFormatScorer scorer, FileFormatMatches matches, IOptions<SearchOptions> options, TimeProvider time)
{
    public async Task<WantedPageResponse> PageAsync(WantedFilter? filter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var list = await ListAsync(filter, ct);
        var items = await ItemsAsync(list, list.Versions.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ct);
        return new WantedPageResponse(items, list.Versions.Count, page, pageSize);
    }

    /// <summary>Every wanted version, in the list's order.</summary>
    public async Task<List<int>> VersionIdsAsync(WantedFilter? filter, CancellationToken ct) =>
        (await ListAsync(filter, ct)).Versions.Select(w => w.Row.VersionId).ToList();

    /// <summary>The versions the cutoff-unmet job searches this pass: due ones, the longest-unsearched first, bounded.</summary>
    public async Task<List<int>> DueAsync(int batchSize, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var settings = options.Value;
        var list = await ListAsync(null, ct);
        return LeastRecentlySearchedFirst(list.Versions.Where(w => SearchBackoff.IsDue(w.Row.LastSearchAt, w.Row.SearchAttempts, settings, now)))
            .Take(Math.Max(1, batchSize))
            .Select(w => w.Row.VersionId)
            .ToList();
    }

    /// <summary>Every wanted version, the least recently searched first: the order of Radarr's missing and cutoff-unmet searches.</summary>
    public async Task<List<int>> LeastRecentlySearchedAsync(WantedFilter? filter, CancellationToken ct) =>
        LeastRecentlySearchedFirst((await ListAsync(filter, ct)).Versions).Select(w => w.Row.VersionId).ToList();

    /// <summary>Never searched first, then the oldest search; the film id breaks ties, the list order the rest (default version first).</summary>
    private static IEnumerable<WantedVersion> LeastRecentlySearchedFirst(IEnumerable<WantedVersion> versions) =>
        versions.OrderBy(w => w.Row.LastSearchAt ?? DateTime.MinValue).ThenBy(w => w.Row.MovieId);

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

    /// <summary>The whole list in order: films by sort title then year, the default version first.</summary>
    private async Task<WantedList> ListAsync(WantedFilter? filter, CancellationToken ct)
    {
        var versions = db.MovieVersions.AsNoTracking()
            .Where(v => v.Mode == VersionMode.Managed && v.Monitored)
            .Where(v => !db.GrabTargets.Any(t => t.Active && t.TargetId == v.Id))
            .Where(v => v.File == null || v.QualityProfile.UpgradeAllowed);
        versions = filter switch
        {
            WantedFilter.Missing => versions.Where(v => v.File == null),
            WantedFilter.CutoffUnmet => versions.Where(v => v.File != null),
            _ => versions,
        };

        var rows = await versions
            .Select(v => new VersionRow(
                v.Id,
                v.MovieId,
                v.Name,
                v.IsDefault,
                v.MinimumAvailability,
                v.QualityProfileId,
                v.LastSearchAt,
                v.SearchAttempts,
                v.Movie.SortTitle,
                v.Movie.Year,
                v.Movie.TmdbStatus,
                v.Movie.InCinemasDate,
                v.Movie.DigitalReleaseDate,
                v.Movie.PhysicalReleaseDate,
                v.File == null
                    ? null
                    : new FileRow(
                        v.File.Id,
                        new FileFormatInput(
                            v.File.OriginalReleaseTitle,
                            v.File.RelativePath,
                            v.File.Size,
                            v.File.IndexerFlags,
                            v.File.QualityId,
                            v.File.Languages,
                            v.File.Edition,
                            v.File.ReleaseGroup,
                            v.Movie.Year,
                            v.Movie.OriginalLanguage))))
            .ToListAsync(ct);

        var profiles = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).ToDictionaryAsync(p => p.Id, ct);
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var formats = rows.Any(r => r.File is not null) ? new Formats(await scorer.CalculatorAsync(ct), await scorer.SignatureAsync(ct)) : null;
        var list = new WantedList([], profiles, definitions, formats, matches);
        var ranges = profiles.Values.ToDictionary(p => p.Id, ScoreRange);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        // In memory, with the comparer the list always sorted with (SQLite's would order accents and spaces differently);
        // films sharing a sort title and a year keep the id order the full load gave them.
        foreach (var row in rows.OrderBy(r => r.SortTitle).ThenBy(r => r.Year).ThenBy(r => r.MovieId).ThenByDescending(r => r.IsDefault).ThenBy(r => r.VersionId))
        {
            // The availability rules only read the status and the release dates.
            var movie = new Movie
            {
                Title = string.Empty,
                SortTitle = row.SortTitle,
                TmdbStatus = row.TmdbStatus,
                InCinemasDate = row.InCinemasDate,
                DigitalReleaseDate = row.DigitalReleaseDate,
                PhysicalReleaseDate = row.PhysicalReleaseDate,
            };
            if (!Availability.IsAvailable(movie, row.MinimumAvailability, today))
            {
                continue;
            }

            if (row.File is null)
            {
                list.Versions.Add(new WantedVersion(row, WantedFilter.Missing, null));
                continue;
            }

            var profile = profiles[row.QualityProfileId];
            int? score = null;
            if (ProfileRanking.CutoffMet(profile, row.File.Input.QualityId))
            {
                // At the cutoff quality the score decides, unless no score this profile gives can fall on the other side.
                var (lowest, highest) = ranges[profile.Id];
                if (profile.CutoffFormatScore <= lowest)
                {
                    continue;
                }

                if (profile.CutoffFormatScore <= highest)
                {
                    score = list.Score(row.File, profile);
                    if (score >= profile.CutoffFormatScore)
                    {
                        continue;
                    }
                }
            }

            list.Versions.Add(new WantedVersion(row, WantedFilter.CutoffUnmet, score));
        }

        if (filter != WantedFilter.Missing)
        {
            // Only files that no longer exist are forgotten: the library list remembers files this list never judges.
            matches.Retain((await db.MediaFiles.AsNoTracking().Select(f => f.Id).ToListAsync(ct)).ToHashSet());
        }

        return list;
    }

    private async Task<List<WantedItemResponse>> ItemsAsync(WantedList list, List<WantedVersion> versions, CancellationToken ct)
    {
        var movieIds = versions.Select(w => w.Row.MovieId).Distinct().ToList();
        var movies = await db.Movies.AsNoTracking()
            .Where(m => movieIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Title, m.Images })
            .ToDictionaryAsync(m => m.Id, ct);
        var settings = options.Value;

        return versions.Select(w =>
        {
            var row = w.Row;
            var movie = movies[row.MovieId];
            var profile = list.Profiles[row.QualityProfileId];
            return new WantedItemResponse(
                row.MovieId,
                movie.Title,
                row.Year,
                movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                row.VersionId,
                row.VersionName,
                profile.Name,
                w.Kind,
                row.File is null ? null : list.Definitions.GetValueOrDefault(row.File.Input.QualityId)?.Name,
                row.File is null ? null : w.Score ?? list.Score(row.File, profile),
                CutoffName(profile, list.Definitions),
                row.LastSearchAt,
                row.SearchAttempts,
                SearchBackoff.NextAllowed(row.LastSearchAt, row.SearchAttempts, settings));
        }).ToList();
    }

    /// <summary>The lowest and highest score a file can get from the profile: every negative format matched, or every positive one.</summary>
    private static (int Lowest, int Highest) ScoreRange(QualityProfile profile) =>
        (profile.FormatScores.Where(s => s.Score < 0).Sum(s => s.Score), profile.FormatScores.Where(s => s.Score > 0).Sum(s => s.Score));

    private sealed record VersionRow(
        int VersionId,
        int MovieId,
        string VersionName,
        bool IsDefault,
        MinimumAvailability MinimumAvailability,
        int QualityProfileId,
        DateTime? LastSearchAt,
        int SearchAttempts,
        string SortTitle,
        int? Year,
        MovieStatus TmdbStatus,
        DateOnly? InCinemasDate,
        DateOnly? DigitalReleaseDate,
        DateOnly? PhysicalReleaseDate,
        FileRow? File);

    private sealed record FileRow(int Id, FileFormatInput Input);

    /// <summary>A wanted version; the score is there when the verdict needed it.</summary>
    private sealed record WantedVersion(VersionRow Row, WantedFilter Kind, int? Score);

    private sealed record Formats(CustomFormatCalculator Calculator, string Signature);

    private sealed record WantedList(
        List<WantedVersion> Versions,
        Dictionary<int, QualityProfile> Profiles,
        Dictionary<int, QualityDefinition> Definitions,
        Formats? Formats,
        FileFormatMatches Matches)
    {
        public int Score(FileRow file, QualityProfile profile)
        {
            var matched = Matches.Match(file.Id, file.Input, Definitions.GetValueOrDefault(file.Input.QualityId), Formats!.Calculator, Formats.Signature);
            return ProfileRanking.FormatScore(profile, matched);
        }
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

        var results = await search.SearchAsync(due, SearchTrigger.CutoffUnmet, ct);
        var grabbed = results.Count(r => r.Outcome == AutomaticSearchOutcome.Grabbed);
        var picked = results.Count(r => r.Outcome == AutomaticSearchOutcome.Picked);
        var noIndexer = results.Count(r => r.Outcome == AutomaticSearchOutcome.NoIndexer);
        return noIndexer == results.Count
            ? $"{results.Count} version(s) due, no indexer with automatic search"
            : $"{results.Count} version(s) searched, {grabbed} grabbed" + (picked > 0 ? $", {picked} picked in shadow mode" : string.Empty);
    }
}
