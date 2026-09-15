using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Matching;

/// <summary>
/// Turns movie versions into what the decision engine may know (blueprint §3.10): profile, definitions, current file,
/// runtime, blocklist (a one-shot borrows the default version's, §3.5), active grab, availability, default flag.
/// Movies must come with Versions → QualityProfile and File loaded.
/// </summary>
public sealed class TargetContextFactory(BigarrDbContext db, CustomFormatScorer scorer, TimeProvider time)
{
    public async Task<List<TargetContext>> BuildAsync(Movie movie, IReadOnlyList<MovieVersion> versions, CancellationToken ct)
    {
        var all = await BuildAsync([movie], ct);
        var wanted = versions.Select(v => v.Id).ToHashSet();
        return all[movie.Id].Where(c => wanted.Contains(c.Target.Id)).ToList();
    }

    /// <summary>One list per movie id, every version included. Blocklist and active grabs are loaded once: both tables stay small.</summary>
    public async Task<Dictionary<int, List<TargetContext>>> BuildAsync(IReadOnlyCollection<Movie> movies, CancellationToken ct)
    {
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var blocklist = (await db.Blocklist.AsNoTracking().ToListAsync(ct)).ToLookup(b => b.TargetId);
        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var calculator = await scorer.CalculatorAsync(ct);
        var formatNames = await scorer.NamesAsync(ct);

        var result = new Dictionary<int, List<TargetContext>>();
        foreach (var movie in movies)
        {
            var defaultVersionId = movie.Versions.FirstOrDefault(v => v.IsDefault)?.Id;
            var contexts = new List<TargetContext>();
            foreach (var version in movie.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
            {
                var blocklistOwner = version.Mode == VersionMode.OneShot && defaultVersionId is not null ? defaultVersionId.Value : version.Id;
                ExistingFile? existing = null;
                if (version.File is { } file)
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

    /// <summary>The graph both search paths need.</summary>
    public IQueryable<Movie> MoviesWithVersions() =>
        db.Movies.AsNoTracking()
            .Include(m => m.Versions).ThenInclude(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .AsSplitQuery();
}
