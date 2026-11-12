using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.SeriesFilms;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>
/// The review of specials that are films (S8): every candidate of every show, its state, and what linking it would do
/// on disk, worked out from the files as they are now. It is the <see cref="SeriesFilmLinker"/>'s dry run: the same
/// rules, nothing written. For a linked special it says what the keeper would do next (often: nothing, already linked).
/// </summary>
public sealed class SeriesFilmPlanner(BigarrDbContext db, IOptions<ShadowOptions> shadow)
{
    /// <summary>A special's own file that linking would make the film's file, in the series version it comes from.</summary>
    public sealed record Adoption(SeriesVersion Version, EpisodeTarget Target, EpisodeFile File, string Path);

    /// <summary>
    /// The special's own file the film adopts when the film has none: in a version that wants the special, a plain file
    /// holding this episode only, on disk. The series' default version first, then the oldest.
    /// </summary>
    public static Adoption? Adoptable(IEnumerable<SeriesVersion> versions, IEnumerable<EpisodeTarget> targets, int episodeId, Func<EpisodeTarget, SeriesVersion, bool> wanted)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(wanted);
        foreach (var version in versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
        {
            var target = targets.FirstOrDefault(t => t.VersionId == version.Id && t.EpisodeId == episodeId);
            if (target?.EpisodeFile is not { IsFilmLink: false } file || !wanted(target, version) || file.Episodes.Any(t => t.EpisodeId != episodeId))
            {
                continue;
            }

            var path = Path.Combine(version.RootFolder.Path, version.FolderName, file.RelativePath);
            if (File.Exists(path))
            {
                return new Adoption(version, target, file, path);
            }
        }

        return null;
    }

    /// <summary>Would linking this special now adopt its file (so the film need not be searched)? False in shadow mode.</summary>
    public async Task<bool> WillAdoptAsync(int episodeId, Movie? movie, CancellationToken ct)
    {
        if (shadow.Value.Enabled)
        {
            return false;
        }

        if (movie is not null)
        {
            var loaded = await db.Movies.AsNoTracking().Include(m => m.Versions).ThenInclude(v => v.File).Include(m => m.Versions).ThenInclude(v => v.RootFolder)
                .FirstAsync(m => m.Id == movie.Id, ct);
            if (SeriesFilmLinker.SourceOf(loaded) is not null)
            {
                return false;
            }
        }

        var episode = await db.Episodes.AsNoTracking().FirstAsync(e => e.Id == episodeId, ct);
        var versions = await db.SeriesVersions.AsNoTracking().Include(v => v.RootFolder).Where(v => v.SeriesId == episode.SeriesId).ToListAsync(ct);
        var targets = await db.EpisodeTargets.AsNoTrackingWithIdentityResolution().Include(t => t.EpisodeFile!).ThenInclude(f => f.Episodes)
            .Where(t => t.EpisodeId == episodeId).ToListAsync(ct);
        return Adoptable(versions, targets, episodeId, (t, v) => t.Monitored || v.Monitored) is not null;
    }

    /// <summary>The system <c>1080p</c> profile when it takes films, else the first film profile.</summary>
    public async Task<(int Id, string Name)?> DefaultFilmProfileAsync(CancellationToken ct)
    {
        var profiles = await db.QualityProfiles.AsNoTracking().Where(p => p.Kind == ProfileKind.Movies || p.Kind == ProfileKind.Both)
            .Select(p => new { p.Id, p.Name, p.IsSystem }).ToListAsync(ct);
        var best = profiles.OrderByDescending(p => p.IsSystem && p.Name == "1080p").ThenByDescending(p => p.IsSystem).ThenBy(p => p.Id).FirstOrDefault();
        return best is null ? null : (best.Id, best.Name);
    }

    public async Task<SeriesFilmSummaryResponse> SummaryAsync(CancellationToken ct)
    {
        var rows = await db.Episodes.AsNoTracking()
            .Where(e => e.SeasonNumber == 0 && (e.IsMovie || e.MovieMatch != SpecialMovieMatch.None || e.MovieLink != null))
            .Select(e => new { Linked = e.MovieLink != null, Dismissed = e.MovieDismissedAt != null })
            .ToListAsync(ct);
        return new SeriesFilmSummaryResponse(
            rows.Count(r => !r.Linked && !r.Dismissed),
            rows.Count(r => r.Linked),
            rows.Count(r => !r.Linked && r.Dismissed));
    }

    public async Task<SeriesFilmReviewResponse> ReviewAsync(CancellationToken ct)
    {
        var config = await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();
        var candidates = await db.Episodes.AsNoTracking()
            .Include(e => e.Series)
            .Include(e => e.MovieLink)
            .Where(e => e.SeasonNumber == 0 && (e.IsMovie || e.MovieMatch != SpecialMovieMatch.None || e.MovieLink != null))
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return new SeriesFilmReviewResponse(shadow.Value.Enabled, config.AutoAddSeriesFilms, []);
        }

        var seriesIds = candidates.Select(e => e.SeriesId).Distinct().ToList();
        var episodeIds = candidates.Select(e => e.Id).ToList();
        var linkedMovieIds = candidates.Where(e => e.MovieLink is not null).Select(e => e.MovieLink!.MovieId).ToList();
        var tmdbIds = candidates.Where(e => e.MovieTmdbId is not null).Select(e => e.MovieTmdbId!.Value).ToList();

        var movies = await db.Movies.AsNoTracking()
            .Include(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .Include(m => m.Versions).ThenInclude(v => v.RootFolder)
            .Where(m => linkedMovieIds.Contains(m.Id) || tmdbIds.Contains(m.TmdbId))
            .AsSplitQuery()
            .ToListAsync(ct);
        var versions = await db.SeriesVersions.AsNoTracking().Include(v => v.RootFolder).Where(v => seriesIds.Contains(v.SeriesId)).ToListAsync(ct);
        var targets = await db.EpisodeTargets.AsNoTrackingWithIdentityResolution().Include(t => t.EpisodeFile!).ThenInclude(f => f.Episodes)
            .Where(t => episodeIds.Contains(t.EpisodeId))
            .ToListAsync(ct);
        var seasonsOf = await db.Episodes.AsNoTracking().Where(e => seriesIds.Contains(e.SeriesId) && e.SeasonNumber > 0)
            .Select(e => new Episode { Id = e.Id, SeriesId = e.SeriesId, Title = e.Title, SeasonNumber = e.SeasonNumber, EpisodeNumber = e.EpisodeNumber, AirDate = e.AirDate })
            .ToListAsync(ct);
        var filmRoot = await db.RootFolders.AsNoTracking().Where(r => r.Kind == MediaKind.Movies).OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
        var profile = await DefaultFilmProfileAsync(ct);

        var rows = new List<SeriesFilmReviewRow>();
        foreach (var episode in candidates.OrderBy(e => e.Series.SortTitle).ThenBy(e => e.EpisodeNumber))
        {
            var link = episode.MovieLink;
            var movie = link is not null ? movies.FirstOrDefault(m => m.Id == link.MovieId) : movies.FirstOrDefault(m => m.TmdbId == episode.MovieTmdbId);
            var showVersions = versions.Where(v => v.SeriesId == episode.SeriesId).ToList();
            var episodeTargets = targets.Where(t => t.EpisodeId == episode.Id).ToList();
            var placement = link is not null
                ? new FilmPlacement(link.AfterSeason, link.BeforeSeason, link.PlacementSource)
                : SeriesFilmDetection.Place(episode, [.. seasonsOf.Where(e => e.SeriesId == episode.SeriesId), episode]);
            var source = movie is null ? null : SeriesFilmLinker.SourceOf(movie);
            var state = link is not null ? SeriesFilmState.Linked : episode.MovieDismissedAt is not null ? SeriesFilmState.Dismissed : SeriesFilmState.Suggested;
            var newFilmFolder = movie is null && filmRoot is not null && episode.MovieTitle is { } title && episode.MovieTmdbId is { } tmdb
                ? Path.Combine(filmRoot.Path, MovieService.FolderName(config, title, null, episode.MovieYear, tmdb, episode.MovieImdbId, VersionNaming.FromProfile(profile?.Name ?? "1080p")))
                : filmRoot?.Path;
            var plan = Plan(config, episode, link, movie, source, showVersions, episodeTargets, newFilmFolder);

            rows.Add(new SeriesFilmReviewRow(
                episode.SeriesId,
                episode.Series.Title,
                episode.Id,
                $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}",
                episode.Title,
                episode.Runtime,
                SeriesFilmDetection.Confidence(episode),
                [.. SeriesFilmDetection.Evidence(episode)],
                SeriesFilmDetection.Label(placement.AfterSeason, placement.BeforeSeason),
                movie?.TmdbId ?? episode.MovieTmdbId,
                movie?.Title ?? episode.MovieTitle,
                movie?.Year ?? episode.MovieYear,
                movie?.Id,
                movie is not null,
                state,
                episodeTargets.Any(t => t.EpisodeFile is not null),
                source is not null,
                link?.LinkError,
                plan));
        }

        return new SeriesFilmReviewResponse(shadow.Value.Enabled, config.AutoAddSeriesFilms, rows);
    }

    /// <summary>The linker's rules (<see cref="SeriesFilmLinker"/>), run on paper for one special.</summary>
    private List<FilmFileStep> Plan(
        MediaManagementConfig config,
        Episode episode,
        SeriesMovieLink? link,
        Movie? movie,
        (MediaFile File, string Path)? source,
        List<SeriesVersion> versions,
        List<EpisodeTarget> targets,
        string? newFilmFolder)
    {
        var steps = new List<FilmFileStep>();
        var linked = link is not null;
        if (link?.LinkError is { } error)
        {
            steps.Add(new FilmFileStep(FilmFileStepKind.LinkError, Note: error));
        }

        if (!linked)
        {
            steps.Add(movie is null ? new FilmFileStep(FilmFileStepKind.AddFilm, To: newFilmFolder) : new FilmFileStep(FilmFileStepKind.ReuseFilm));
        }

        if (shadow.Value.Enabled)
        {
            steps.Add(new FilmFileStep(FilmFileStepKind.ShadowNoFiles));
            return steps;
        }

        // Linking monitors the special wherever the version is monitored; once linked, the target says.
        bool Wanted(EpisodeTarget t, SeriesVersion v) => linked ? t.Monitored : t.Monitored || v.Monitored;

        var filmVersion = movie?.Versions.FirstOrDefault(v => v.IsDefault);
        var filmFolder = filmVersion is not null ? Path.Combine(filmVersion.RootFolder.Path, filmVersion.FolderName) : newFilmFolder;
        Adoption? adoption = null;
        if (source is null && (movie is null || filmVersion is not null))
        {
            adoption = Adoptable(versions, targets, episode.Id, Wanted);
            if (adoption is not null)
            {
                steps.Add(new FilmFileStep(FilmFileStepKind.AdoptSpecialFile, adoption.Version.Name, adoption.Path, filmFolder));
            }
            else if (!linked)
            {
                steps.Add(new FilmFileStep(movie is null ? FilmFileStepKind.SearchFilm : FilmFileStepKind.NothingOnDisk));
            }
        }

        var filmHasFile = source is not null || adoption is not null;
        foreach (var version in versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id))
        {
            var target = targets.FirstOrDefault(t => t.VersionId == version.Id);
            if (target is null)
            {
                continue;
            }

            var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
            var current = target.EpisodeFile;
            var currentPath = current is null ? null : Path.Combine(folder, current.RelativePath);
            if (!filmHasFile || !Wanted(target, version))
            {
                if (current is { IsFilmLink: true })
                {
                    steps.Add(new FilmFileStep(FilmFileStepKind.RemoveHardlink, version.Name, currentPath));
                }

                continue;
            }

            string? destination = null;
            if (source is { } src)
            {
                var relative = SeriesNaming.RelativePath(config, episode.Series, version, [episode], SeriesFilmLinker.FactsOf(src.File, src.Path), Path.GetExtension(src.Path), Path.GetFileName(src.Path));
                destination = Path.Combine(folder, relative);
                if (current is { IsFilmLink: true } && current.LinkedMediaFileId == src.File.Id && current.RelativePath == relative && File.Exists(destination))
                {
                    steps.Add(new FilmFileStep(FilmFileStepKind.AlreadyHardlinked, version.Name, To: destination));
                    continue;
                }
            }

            if (adoption?.Target == target)
            {
                // The moved file comes back under the special's name: still one copy on disk.
                steps.Add(new FilmFileStep(FilmFileStepKind.HardlinkIntoSpecials, version.Name, To: adoption.Path));
                continue;
            }

            if (current is { IsFilmLink: false })
            {
                if (current.Episodes.Any(t => t.EpisodeId != episode.Id))
                {
                    steps.Add(new FilmFileStep(FilmFileStepKind.SharedFileLeft, version.Name, currentPath));
                    continue;
                }

                steps.Add(new FilmFileStep(FilmFileStepKind.FilmFileWins, version.Name, currentPath));
            }

            steps.Add(new FilmFileStep(FilmFileStepKind.HardlinkIntoSpecials, version.Name, To: destination ?? Path.Combine(folder, "Specials")));
        }

        if (steps.Count == 0)
        {
            steps.Add(new FilmFileStep(FilmFileStepKind.NothingOnDisk));
        }

        return steps;
    }
}
