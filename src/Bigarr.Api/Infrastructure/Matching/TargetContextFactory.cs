using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Matching;

/// <summary>
/// Turns movie versions into what the decision engine may know: profile, definitions, current file,
/// runtime, blocklist (a one-shot borrows the default version's), active grab, availability, default flag.
/// Movies must come with Versions → QualityProfile and File loaded.
/// </summary>
/// <remarks>
/// Two views of the current file. <see cref="BuildAsync(IReadOnlyCollection{Movie}, CancellationToken)"/> is the file on
/// disk, for imports. <see cref="BuildForGrabAsync(IReadOnlyCollection{Movie}, DateTime, CancellationToken)"/> is what a
/// grab decision compares with: the same file, except in shadow mode, where a version's latest pick stands in for it.
/// A pick is what Bigarr would have grabbed, so it is the file the version would have had: later releases are judged
/// against it, as they would be once that download was imported, and only a real upgrade over it is picked again.
/// </remarks>
public sealed class TargetContextFactory(BigarrDbContext db, CustomFormatScorer scorer, IOptions<ShadowOptions> shadow, TimeProvider time)
{
    /// <summary>The versions asked, with the file on disk (imports).</summary>
    public async Task<List<TargetContext>> BuildAsync(Movie movie, IReadOnlyList<MovieVersion> versions, CancellationToken ct) =>
        Only(await BuildCoreAsync([movie], null, ct), movie, versions);

    /// <summary>The versions asked, as a grab decision sees them now: in shadow mode, a version's latest pick stands in for its file.</summary>
    public async Task<List<TargetContext>> BuildForGrabAsync(Movie movie, IReadOnlyList<MovieVersion> versions, CancellationToken ct) =>
        Only(await BuildCoreAsync([movie], time.GetUtcNow().UtcDateTime, ct), movie, versions);

    /// <summary>One list per movie id, every version included, with the file on disk (imports).</summary>
    public Task<Dictionary<int, List<TargetContext>>> BuildAsync(IReadOnlyCollection<Movie> movies, CancellationToken ct) =>
        BuildCoreAsync(movies, null, ct);

    /// <summary>
    /// One list per movie id, every version included, as a grab decision sees them at <paramref name="asOf"/>: in shadow
    /// mode, the latest pick made before that time stands in for a version's file.
    /// </summary>
    public Task<Dictionary<int, List<TargetContext>>> BuildForGrabAsync(IReadOnlyCollection<Movie> movies, DateTime asOf, CancellationToken ct) =>
        BuildCoreAsync(movies, asOf, ct);

    private static List<TargetContext> Only(Dictionary<int, List<TargetContext>> all, Movie movie, IReadOnlyList<MovieVersion> versions)
    {
        var wanted = versions.Select(v => v.Id).ToHashSet();
        return all[movie.Id].Where(c => wanted.Contains(c.Target.Id)).ToList();
    }

    /// <summary>Blocklist and active grabs are loaded once: both tables stay small.</summary>
    private async Task<Dictionary<int, List<TargetContext>>> BuildCoreAsync(IReadOnlyCollection<Movie> movies, DateTime? picksBefore, CancellationToken ct)
    {
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var blocklist = (await db.Blocklist.AsNoTracking().ToListAsync(ct)).ToLookup(b => b.TargetId);
        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var calculator = await scorer.CalculatorAsync(ct);
        var formatNames = await scorer.NamesAsync(ct);
        var picks = picksBefore is { } before && shadow.Value.Enabled ? await LatestPicksAsync(movies, before, ct) : [];

        var result = new Dictionary<int, List<TargetContext>>();
        foreach (var movie in movies)
        {
            var defaultVersionId = movie.Versions.FirstOrDefault(v => v.IsDefault)?.Id;
            var contexts = new List<TargetContext>();
            foreach (var version in movie.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
            {
                var blocklistOwner = version.Mode == VersionMode.OneShot && defaultVersionId is not null ? defaultVersionId.Value : version.Id;
                ExistingFile? existing = null;
                if (picks.TryGetValue(version.Id, out var pick) && PickAsFile(pick, movie, definitions, calculator) is { } picked)
                {
                    existing = picked;
                }
                else if (version.File is { } file)
                {
                    var matched = calculator.Match(CustomFormatScorer.FileInput(
                        file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
                    existing = new ExistingFile(file.QualityId, file.Revision.Version, file.Revision.Real, file.Revision.IsRepack) { CustomFormatIds = matched.Select(m => m.Id).ToList() };
                }

                contexts.Add(new TargetContext(
                    version,
                    version.QualityProfile,
                    definitions,
                    existing,
                    movie.Runtime,
                    blocklist[blocklistOwner].ToList(),
                    active.Contains(version.Id),
                    Availability.IsAvailable(movie, version.MinimumAvailability, today),
                    version.IsDefault)
                {
                    CustomFormatNames = formatNames,
                });
            }

            result[movie.Id] = contexts;
        }

        return result;
    }

    /// <summary>Each version's latest pick made before <paramref name="before"/>.</summary>
    private async Task<Dictionary<int, ShadowPick>> LatestPicksAsync(IReadOnlyCollection<Movie> movies, DateTime before, CancellationToken ct)
    {
        var versionIds = movies.SelectMany(m => m.Versions).Select(v => v.Id).ToList();
        var picks = await db.ShadowPicks.AsNoTracking()
            .Where(p => p.VersionId != null && versionIds.Contains(p.VersionId.Value) && p.PickedAt < before)
            .ToListAsync(ct);
        return picks.GroupBy(p => p.VersionId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PickedAt).ThenByDescending(p => p.Id).First());
    }

    /// <summary>The file a pick would have become: its quality and revision, and the custom formats its release would give the file.</summary>
    private static ExistingFile? PickAsFile(ShadowPick pick, Movie movie, IReadOnlyDictionary<int, QualityDefinition> definitions, CustomFormatCalculator calculator)
    {
        var parsed = ReleaseMatcher.TryParse(pick.ReleaseTitle);
        var qualityId = pick.QualityId ?? parsed?.Quality?.Quality?.Id;
        if (qualityId is not { } id || id == 0)
        {
            return null; // nothing to compare with: the version's own file stays
        }

        var revision = parsed?.Quality?.Revision;
        var formats = calculator.Match(CustomFormatScorer.FileInput(
            pick.ReleaseTitle, pick.ReleaseTitle, pick.Size, pick.IndexerFlags, definitions.GetValueOrDefault(id), pick.Languages, null, pick.ReleaseGroup, movie.Year, movie.OriginalLanguage));
        return new ExistingFile(id, pick.RevisionVersion ?? revision?.Version ?? 1, revision?.Real ?? 0, revision?.IsRepack ?? false)
        {
            CustomFormatIds = formats.Select(f => f.Id).ToList(),
            IsPick = true,
        };
    }

    /// <summary>The graph both search paths need.</summary>
    public IQueryable<Movie> MoviesWithVersions() =>
        db.Movies.AsNoTracking()
            .Include(m => m.Versions).ThenInclude(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .AsSplitQuery();
}
