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
/// column rather than loading the movie graph, so a library of a few thousand films stays a quick read.
/// </summary>
public sealed class MovieSummaryQuery(BigarrDbContext db, CustomFormatScorer scorer, TimeProvider time)
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
                        : new FileRow(v.File.QualityId, v.File.Size, v.File.RelativePath, v.File.OriginalReleaseTitle, v.File.Languages, v.File.IndexerFlags, v.File.Edition, v.File.ReleaseGroup)))
                    .ToList()))
            .AsSplitQuery()
            .ToListAsync(ct);

        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
        var profiles = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).ToDictionaryAsync(p => p.Id, ct);
        var qualities = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var calculator = rows.Any(r => r.Versions.Any(v => v.File is not null)) ? await scorer.CalculatorAsync(ct) : null;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        return rows.Select(row =>
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
                        v.File is null ? null : qualities.GetValueOrDefault(v.File.QualityId)?.Name,
                        v.MinimumAvailability,
                        v.RootFolderId,
                        v.TagIds.Order().ToList(),
                        v.File?.Size,
                        v.File is null || v.Mode != VersionMode.Managed ? null : CutoffMet(row, v.File, profiles[v.QualityProfileId], qualities.GetValueOrDefault(v.File.QualityId), calculator!)))
                    .ToList(),
                row.AddedAt,
                row.Ratings?.Tmdb?.Value,
                row.Ratings?.Imdb?.Value);
        }).ToList();
    }

    /// <summary>
    /// The movie page's verdict (<see cref="CustomFormatScorer.Assess"/>). A profile that scores no custom format
    /// gives every file 0, so the file name is only parsed when a score can change the answer.
    /// Only managed versions get a verdict: a one-shot version is never upgraded (<see cref="MovieMapper.CutoffMet"/>).
    /// </summary>
    private static bool CutoffMet(MovieRow movie, FileRow file, QualityProfile profile, QualityDefinition? quality, CustomFormatCalculator calculator)
    {
        if (profile.FormatScores.All(s => s.Score == 0))
        {
            return ProfileRanking.CutoffMet(profile, file.QualityId) && profile.CutoffFormatScore <= 0;
        }

        var matched = calculator.Match(CustomFormatScorer.FileInput(
            file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
        return CustomFormatScorer.Assess(profile, file.QualityId, matched).CutoffMet;
    }

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

    private sealed record FileRow(
        int QualityId,
        long Size,
        string RelativePath,
        string? OriginalReleaseTitle,
        List<string> Languages,
        IndexerFlags IndexerFlags,
        string? Edition,
        string? ReleaseGroup);
}
