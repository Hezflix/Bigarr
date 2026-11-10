using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Movies;

/// <summary>
/// The library list: one light row per film with what the library page sorts and filters on. Projected column by
/// column rather than loading the movie graph, so a library of a few thousand films stays a quick read. Each file's
/// custom formats come from <see cref="FileFormatMatches"/>: matching every file again on every load was most of the list's time.
/// </summary>
public sealed class MovieSummaryQuery(BigarrDbContext db, CustomFormatScorer scorer, FileFormatMatches matches, TimeProvider time)
{
    public async Task<List<MovieSummary>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Movies.AsNoTracking()
            .OrderBy(m => m.SortTitle).ThenBy(m => m.Year)
            .Select(m => new MovieRow(
                m.Id,
                m.TmdbId,
                m.Title,
                m.SortTitle,
                m.Year,
                m.Images,
                m.Ratings,
                m.AddedAt,
                m.TmdbStatus,
                m.InCinemasDate,
                m.DigitalReleaseDate,
                m.PhysicalReleaseDate,
                m.OriginalLanguage,
                m.Versions.Select(v => new VersionRow(
                    v.Id,
                    v.Name,
                    v.Mode,
                    v.IsDefault,
                    v.Monitored,
                    v.QualityProfileId,
                    v.MinimumAvailability,
                    v.RootFolderId,
                    v.Tags.Select(t => t.Id).ToList(),
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
                    .ToList()))
            .AsSplitQuery()
            .ToListAsync(ct);

        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
        var profiles = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).ToDictionaryAsync(p => p.Id, ct);
        var qualities = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var formats = rows.Any(r => r.Versions.Any(v => v.File is not null)) ? new Formats(await scorer.CalculatorAsync(ct), await scorer.SignatureAsync(ct)) : null;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        // Plays per film and version, read off the (MovieId, VersionId, StoppedAt) index rather than the rows.
        var plays = await db.Plays.AsNoTracking()
            .Where(p => p.MovieId != null)
            .GroupBy(p => new { p.MovieId, p.VersionId })
            .Select(g => new { MovieId = g.Key.MovieId!.Value, g.Key.VersionId, Count = g.Count(), Last = g.Max(p => p.StoppedAt) })
            .ToListAsync(ct);
        var playsByMovie = plays.ToLookup(p => p.MovieId);

        var summaries = rows.Select(row =>
        {
            // The availability rules only read the status and the release dates.
            var movie = new Movie
            {
                Title = row.Title,
                SortTitle = row.SortTitle,
                TmdbStatus = row.TmdbStatus,
                InCinemasDate = row.InCinemasDate,
                DigitalReleaseDate = row.DigitalReleaseDate,
                PhysicalReleaseDate = row.PhysicalReleaseDate,
            };

            var filmPlays = playsByMovie[row.Id].ToList();
            return new MovieSummary(
                row.Id,
                row.TmdbId,
                row.Title,
                row.SortTitle,
                row.Year,
                row.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                Availability.EffectiveStatus(movie, today),
                row.Versions.Any(v => v.Monitored),
                row.Versions
                    .OrderByDescending(v => v.IsDefault)
                    .ThenBy(v => v.Id)
                    .Select(v => new VersionSummary(
                        v.Id,
                        v.Name,
                        v.Mode,
                        v.IsDefault,
                        v.Monitored,
                        v.QualityProfileId,
                        Availability.StatusOf(movie, v.MinimumAvailability, v.File is not null, active.Contains(v.Id), today),
                        v.File is null ? null : qualities.GetValueOrDefault(v.File.Input.QualityId)?.Name,
                        v.MinimumAvailability,
                        v.RootFolderId,
                        v.TagIds.Order().ToList(),
                        v.File?.Input.Size,
                        v.File is null || v.Mode != VersionMode.Managed ? null : CutoffMet(v.File, profiles[v.QualityProfileId], qualities, formats!),
                        filmPlays.Where(p => p.VersionId == v.Id).Sum(p => p.Count),
                        filmPlays.Where(p => p.VersionId == v.Id).Max(p => (DateTime?)p.Last)))
                    .ToList(),
                row.AddedAt,
                row.Ratings?.Tmdb?.Value,
                row.Ratings?.Imdb?.Value,
                filmPlays.Sum(p => p.Count),
                filmPlays.Max(p => (DateTime?)p.Last));
        }).ToList();

        // Every file is on this list: the remembered matches of the others belong to files deleted or replaced since.
        matches.Retain(rows.SelectMany(r => r.Versions).Where(v => v.File is not null).Select(v => v.File!.Id).ToHashSet());
        return summaries;
    }

    /// <summary>
    /// The movie page's verdict (<see cref="CustomFormatScorer.Assess"/>). A profile that scores no custom format
    /// gives every file 0, and a file below the cutoff quality misses it whatever its score, so the file's formats are
    /// only matched (or remembered) when a score can change the answer.
    /// Only managed versions get a verdict: a one-shot version is never upgraded (<see cref="MovieMapper.CutoffMet"/>).
    /// </summary>
    private bool CutoffMet(FileRow file, QualityProfile profile, Dictionary<int, QualityDefinition> qualities, Formats formats)
    {
        if (profile.FormatScores.All(s => s.Score == 0))
        {
            return ProfileRanking.CutoffMet(profile, file.Input.QualityId) && profile.CutoffFormatScore <= 0;
        }

        if (!ProfileRanking.CutoffMet(profile, file.Input.QualityId))
        {
            return false;
        }

        var matched = matches.Match(file.Id, file.Input, qualities.GetValueOrDefault(file.Input.QualityId), formats.Calculator, formats.Signature);
        return ProfileRanking.FormatScore(profile, matched) >= profile.CutoffFormatScore;
    }

    private sealed record Formats(CustomFormatCalculator Calculator, string Signature);

    private sealed record MovieRow(
        int Id,
        int TmdbId,
        string Title,
        string SortTitle,
        int? Year,
        List<MediaImage> Images,
        MovieRatings? Ratings,
        DateTime AddedAt,
        MovieStatus TmdbStatus,
        DateOnly? InCinemasDate,
        DateOnly? DigitalReleaseDate,
        DateOnly? PhysicalReleaseDate,
        string? OriginalLanguage,
        List<VersionRow> Versions);

    private sealed record VersionRow(
        int Id,
        string Name,
        VersionMode Mode,
        bool IsDefault,
        bool Monitored,
        int QualityProfileId,
        MinimumAvailability MinimumAvailability,
        int RootFolderId,
        List<int> TagIds,
        FileRow? File);

    private sealed record FileRow(int Id, FileFormatInput Input);
}
