using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.SeriesFilms;

/// <summary>
/// Specials that are films (S8): detection after each add and refresh, the series page's list, "Add as film" (creating
/// or reusing the <c>Movie</c> by TMDB id) and unlinking. Hardlinks are the <see cref="SeriesFilmLinker"/>'s job.
/// </summary>
public sealed class SeriesFilmService(
    BigarrDbContext db,
    IMetadataProvider movies,
    MovieService movieService,
    SeriesFilmPlanner planner,
    SeriesFilmLinkQueue links,
    TimeProvider time,
    ILogger<SeriesFilmService> logger)
{
    /// <summary>Title searches per pass, so a show with fifty long specials cannot flood TMDB.</summary>
    public const int MaxTitleSearches = 10;

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Looks at the show's specials: unflagged long ones are searched on TMDB once; specials seen for the first time
    /// that are flagged films are added on their own when the auto-add setting is on. Returns the links it created.
    /// </summary>
    public async Task<List<SeriesMovieLink>> DetectAsync(int seriesId, CancellationToken ct)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return [];
        }

        var specials = await db.Episodes.Include(e => e.MovieLink).Where(e => e.SeriesId == seriesId && e.SeasonNumber == 0).ToListAsync(ct);
        var fresh = specials.Where(e => e.MovieCheckedAt is null).ToList();
        var searches = 0;
        foreach (var special in specials.Where(SeriesFilmDetection.WorthTitleSearch))
        {
            if (searches++ >= MaxTitleSearches)
            {
                break;
            }

            try
            {
                if (await FindByTitleAsync(special, series.Title, ct) is { } film)
                {
                    special.MovieMatch = SpecialMovieMatch.TitleAndRuntime;
                    special.MovieTmdbId = film.TmdbId;
                    special.MovieImdbId = film.ImdbId;
                    special.MovieTitle = film.Title;
                    special.MovieYear = film.Year;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "{Series}: TMDB search for special S00E{Episode:00} failed; retried next refresh", series.Title, special.EpisodeNumber);
                fresh.Remove(special);
                continue;
            }

            special.MovieCheckedAt = Now;
        }

        foreach (var special in fresh)
        {
            special.MovieCheckedAt ??= Now;
        }

        await NameFilmsAsync(series.Title, specials, ct);
        await db.SaveChangesAsync(ct);

        var config = await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();
        var added = new List<SeriesMovieLink>();
        if (!config.AutoAddSeriesFilms)
        {
            return added;
        }

        // Only flagged films, seen for the first time: a "likely" one waits for a human, an unlinked one stays unlinked.
        foreach (var special in fresh.Where(e => SeriesFilmDetection.Confidence(e) == FilmConfidence.Flagged && e.MovieTmdbId is not null && e.MovieLink is null))
        {
            var result = await AddAsync(seriesId, new AddSeriesFilmRequest(special.Id), ct, autoAdded: true);
            if (result.IsSuccess)
            {
                added.Add(result.Value!);
            }
            else
            {
                logger.LogWarning("{Series}: auto-add of film special S00E{Episode:00} failed: {Reason}", series.Title, special.EpisodeNumber, result.Failure!.Detail ?? result.Failure.Title);
            }
        }

        return added;
    }

    /// <summary>
    /// The TMDB title and year of each candidate's film not in the library yet, read once (TVDB's linked movie gives
    /// only ids), so the review page can say which film a special would become.
    /// </summary>
    private async Task NameFilmsAsync(string seriesTitle, List<Episode> specials, CancellationToken ct)
    {
        var unnamed = specials.Where(e => SeriesFilmDetection.IsCandidate(e) && e.MovieTmdbId is not null && e.MovieTitle is null).Take(MaxTitleSearches).ToList();
        foreach (var special in unnamed)
        {
            try
            {
                if (await movies.GetMovieAsync(special.MovieTmdbId!.Value, ct) is { } film)
                {
                    special.MovieTitle = film.Title;
                    special.MovieYear = film.Year;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogDebug(ex, "{Series}: TMDB film {TmdbId} not read; named next refresh", seriesTitle, special.MovieTmdbId);
            }
        }
    }

    private async Task<MovieMetadata?> FindByTitleAsync(Episode special, string seriesTitle, CancellationToken ct)
    {
        foreach (var query in SeriesFilmDetection.TitleQueries(special, seriesTitle))
        {
            var hits = await movies.SearchAsync(query, special.AirDate?.Year, ct);
            foreach (var hit in hits.Take(3))
            {
                // Search hits lack the runtime: the film itself is read before judging.
                var film = hit.Runtime is null ? await movies.GetMovieAsync(hit.TmdbId, ct) ?? hit : hit;
                if (SeriesFilmDetection.Matches(special, seriesTitle, film))
                {
                    return film;
                }
            }
        }

        return null;
    }

    public async Task<SeriesFilmsResponse?> ListAsync(int seriesId, CancellationToken ct)
    {
        if (!await db.Series.AnyAsync(s => s.Id == seriesId, ct))
        {
            return null;
        }

        var episodes = await db.Episodes.AsNoTracking().Where(e => e.SeriesId == seriesId).ToListAsync(ct);
        var linkRows = await db.SeriesMovieLinks.AsNoTracking()
            .Include(l => l.Movie).ThenInclude(m => m.Versions).ThenInclude(v => v.File)
            .Where(l => l.SeriesId == seriesId)
            .AsSplitQuery()
            .ToListAsync(ct);
        var byEpisode = linkRows.ToDictionary(l => l.EpisodeId);
        // A candidate the user dismissed leaves the suggestions (the review page still lists it).
        var shown = episodes.Where(e => (SeriesFilmDetection.IsCandidate(e) && e.MovieDismissedAt is null) || byEpisode.ContainsKey(e.Id)).OrderBy(e => e.EpisodeNumber).ToList();
        var shownIds = shown.Select(e => e.Id).ToList();
        var tmdbIds = shown.Where(e => e.MovieTmdbId is not null).Select(e => e.MovieTmdbId!.Value).ToList();
        var inLibrary = await db.Movies.AsNoTracking().Where(m => tmdbIds.Contains(m.TmdbId)).ToDictionaryAsync(m => m.TmdbId, m => m.Id, ct);
        var versions = await db.SeriesVersions.AsNoTracking().Where(v => v.SeriesId == seriesId).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).ToListAsync(ct);
        var targets = await db.EpisodeTargets.AsNoTracking().Include(t => t.EpisodeFile)
            .Where(t => shownIds.Contains(t.EpisodeId))
            .ToListAsync(ct);
        var config = await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();

        var films = shown.Select(e =>
        {
            var link = byEpisode.GetValueOrDefault(e.Id);
            var placement = link is not null
                ? new FilmPlacement(link.AfterSeason, link.BeforeSeason, link.PlacementSource)
                : SeriesFilmDetection.Place(e, episodes);
            SeriesFilmLinkResponse? linkResponse = null;
            if (link is not null)
            {
                linkResponse = new SeriesFilmLinkResponse(
                    link.Id,
                    link.MovieId,
                    link.Movie.Title,
                    link.Movie.Year,
                    link.Movie.Versions.Any(v => v.IsDefault && v.File is not null),
                    link.AutoAdded,
                    link.CreatedAt,
                    link.LinkError,
                    versions.Select(v =>
                    {
                        var target = targets.FirstOrDefault(t => t.VersionId == v.Id && t.EpisodeId == e.Id);
                        var file = target?.EpisodeFile;
                        return new SeriesFilmVersionLinkResponse(v.Id, v.Name, target?.Monitored ?? false, file?.IsFilmLink == true, file?.RelativePath);
                    }).ToList());
            }

            return new SeriesFilmResponse(
                e.Id,
                e.SeasonNumber,
                e.EpisodeNumber,
                $"S{e.SeasonNumber:00}E{e.EpisodeNumber:00}",
                e.Title,
                e.AirDate,
                e.Runtime,
                SeriesFilmDetection.Confidence(e),
                [.. SeriesFilmDetection.Evidence(e)],
                link?.Movie.TmdbId ?? e.MovieTmdbId,
                link?.Movie.ImdbId ?? e.MovieImdbId,
                placement.AfterSeason,
                placement.BeforeSeason,
                placement.Source,
                SeriesFilmDetection.Label(placement.AfterSeason, placement.BeforeSeason),
                e.MovieTmdbId is { } tmdb && inLibrary.TryGetValue(tmdb, out var movieId) ? movieId : null,
                linkResponse);
        }).ToList();

        return new SeriesFilmsResponse(config.AutoAddSeriesFilms, films);
    }

    /// <summary>
    /// Makes the special a film of the library: the <c>Movie</c> with that TMDB id is reused (a film followed from Radarr
    /// keeps its versions and files), else created with its default managed version on the default film profile and
    /// the chosen film root. The special is then monitored on every monitored series version so it is hardlinked there.
    /// </summary>
    public async Task<Result<SeriesMovieLink>> AddAsync(int seriesId, AddSeriesFilmRequest request, CancellationToken ct, bool autoAdded = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var episode = await db.Episodes.Include(e => e.MovieLink).Include(e => e.Targets).ThenInclude(t => t.Version)
            .FirstOrDefaultAsync(e => e.Id == request.EpisodeId && e.SeriesId == seriesId, ct);
        if (episode is null)
        {
            return Failure.NotFound("Episode");
        }

        if (episode.SeasonNumber != 0)
        {
            return Failure.Invalid("Not a special", "Only a special (season 0) can be linked to a film.");
        }

        if (episode.MovieLink is not null)
        {
            return Failure.Conflict("Already a film", $"S00E{episode.EpisodeNumber:00} is linked to film {episode.MovieLink.MovieId}.");
        }

        var tmdbId = request.TmdbId ?? episode.MovieTmdbId;
        if (tmdbId is null)
        {
            return Failure.Invalid("No TMDB film known", "Detection found no TMDB film for this special: give its TMDB id.");
        }

        var movie = await db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, ct);
        if (movie is null)
        {
            var profileId = request.QualityProfileId ?? (await planner.DefaultFilmProfileAsync(ct))?.Id;
            var rootId = request.RootFolderId ?? await db.RootFolders.Where(r => r.Kind == MediaKind.Movies).OrderBy(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
            if (profileId is null)
            {
                return Failure.Invalid("No film quality profile", "Create a film quality profile first.");
            }

            if (rootId is null)
            {
                return Failure.Invalid("No film root folder", "Add a film root folder in Settings → Library first.");
            }

            // The special's own file becomes the film's file (adopted by the keeper): nothing to search for then.
            var adopting = await planner.WillAdoptAsync(episode.Id, null, ct);
            var created = await movieService.AddMovieAsync(new AddMovieRequest(tmdbId.Value, profileId.Value, rootId.Value, SearchOnAdd: request.SearchOnAdd && !adopting), ct);
            if (!created.IsSuccess)
            {
                return created.Failure!;
            }

            movie = created.Value!;
        }

        var all = await db.Episodes.AsNoTracking().Where(e => e.SeriesId == seriesId).ToListAsync(ct);
        var placement = SeriesFilmDetection.Place(episode, all);
        var link = new SeriesMovieLink
        {
            SeriesId = seriesId,
            EpisodeId = episode.Id,
            MovieId = movie.Id,
            AfterSeason = placement.AfterSeason,
            BeforeSeason = placement.BeforeSeason,
            PlacementSource = placement.Source,
            AutoAdded = autoAdded,
            CreatedAt = Now,
        };
        db.SeriesMovieLinks.Add(link);
        if (episode.MovieTmdbId is null)
        {
            episode.MovieTmdbId = tmdbId;
        }

        episode.MovieDismissedAt = null;

        // Wanted in the show's folders: the special is monitored where the version is; the film engine does the searching.
        foreach (var target in episode.Targets.Where(t => t.Version.Monitored))
        {
            target.Monitored = true;
        }

        await db.SaveChangesAsync(ct);
        links.Enqueue(new LinkWork(movie.Id, null));
        logger.LogInformation("Series {SeriesId}: special S00E{Episode:00} is now film {Title} ({TmdbId}){Auto}", seriesId, episode.EpisodeNumber, movie.Title, movie.TmdbId, autoAdded ? ", auto-added" : string.Empty);
        return Result<SeriesMovieLink>.Ok(link);
    }

    /// <summary>Unlinks: the film stays a film, the special returns to the series engine. Its hardlink stays as a plain file.</summary>
    public async Task<Result<bool>> RemoveAsync(int seriesId, int episodeId, CancellationToken ct)
    {
        var link = await db.SeriesMovieLinks.FirstOrDefaultAsync(l => l.SeriesId == seriesId && l.EpisodeId == episodeId, ct);
        if (link is null)
        {
            return Failure.NotFound("Film link");
        }

        db.SeriesMovieLinks.Remove(link);
        await db.SaveChangesAsync(ct);
        links.Enqueue(new LinkWork(null, seriesId));
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Links several specials at once with the defaults (review page): each one as <see cref="AddAsync"/> does, one
    /// after the other; a failure is reported for its special and the others go on.
    /// </summary>
    public async Task<List<LinkSeriesFilmResult>> LinkManyAsync(IReadOnlyCollection<int> episodeIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(episodeIds);
        var seriesOf = await db.Episodes.AsNoTracking().Where(e => episodeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.SeriesId, ct);
        var results = new List<LinkSeriesFilmResult>();
        foreach (var episodeId in episodeIds.Distinct())
        {
            if (!seriesOf.TryGetValue(episodeId, out var seriesId))
            {
                results.Add(new LinkSeriesFilmResult(episodeId, false, "Episode not found."));
                continue;
            }

            var result = await AddAsync(seriesId, new AddSeriesFilmRequest(episodeId), ct);
            results.Add(new LinkSeriesFilmResult(episodeId, result.IsSuccess, result.IsSuccess ? null : result.Failure!.Detail ?? result.Failure.Title));
            db.ChangeTracker.Clear();
        }

        return results;
    }

    /// <summary>The user says the candidate is not a film to link (or takes that back). A linked special is unlinked first.</summary>
    public async Task<Result<bool>> SetDismissedAsync(int seriesId, int episodeId, bool dismissed, CancellationToken ct)
    {
        var episode = await db.Episodes.Include(e => e.MovieLink).FirstOrDefaultAsync(e => e.Id == episodeId && e.SeriesId == seriesId, ct);
        if (episode is null)
        {
            return Failure.NotFound("Episode");
        }

        if (dismissed && episode.MovieLink is not null)
        {
            return Failure.Conflict("Linked", $"S00E{episode.EpisodeNumber:00} is linked to a film: unlink it first.");
        }

        episode.MovieDismissedAt = dismissed ? Now : null;
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task SetAutoAddAsync(bool autoAdd, CancellationToken ct)
    {
        var config = await db.MediaManagement.FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct);
        if (config is null)
        {
            config = MediaManagementConfig.Default();
            db.MediaManagement.Add(config);
        }

        config.AutoAddSeriesFilms = autoAdd;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The film page's "Also special S00E02 of …" lines.</summary>
    public async Task<List<MovieSeriesLinkResponse>> ForMovieAsync(int movieId, CancellationToken ct)
    {
        var rows = await db.SeriesMovieLinks.AsNoTracking().Include(l => l.Series).Include(l => l.Episode)
            .Where(l => l.MovieId == movieId)
            .OrderBy(l => l.Series.SortTitle)
            .ToListAsync(ct);
        return rows.Select(l => new MovieSeriesLinkResponse(
            l.SeriesId,
            l.Series.Title,
            l.EpisodeId,
            $"S{l.Episode.SeasonNumber:00}E{l.Episode.EpisodeNumber:00}",
            l.AfterSeason,
            l.BeforeSeason,
            SeriesFilmDetection.Label(l.AfterSeason, l.BeforeSeason))).ToList();
    }
}
