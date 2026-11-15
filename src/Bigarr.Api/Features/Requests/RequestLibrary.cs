using Bigarr.Api.Features.Discover;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Requests;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Requests;

/// <summary>
/// An approved request into the library, through the library's own add paths (no compat hop): a new film or series, a new
/// version of a title the library has, or the requested seasons monitored on the version it already has. Searches follow
/// the library add (search on add); in shadow mode the grab choke point turns them into picks, nothing is sent.
/// </summary>
public sealed class RequestLibrary(
    BigarrDbContext db,
    MovieService movies,
    SeriesService series,
    SearchQueue searches,
    SeriesSearchQueue seriesSearches,
    LibraryAvailability library,
    RequestTargets targets,
    ArrForwarder forwarder,
    TimeProvider time)
{
    /// <summary>Adds the request; fills its library id and version name.</summary>
    public async Task<Result<bool>> AddAsync(MediaRequest request, QualityProfile profile, RootFolder root, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fourK = await library.FourKVersionAsync(ct);
        if (request.FourK && fourK is null)
        {
            return Failure.Invalid("4K is not offered", "Name the 4K version in Settings → Discover first.");
        }

        return request.MediaType == RequestMediaType.Movie
            ? await AddMovieAsync(request, profile, root, fourK, ct)
            : await AddSeriesAsync(request, profile, root, fourK, ct);
    }

    private async Task<Result<bool>> AddMovieAsync(MediaRequest request, QualityProfile profile, RootFolder root, string? fourK, CancellationToken ct)
    {
        var movie = await db.Movies.AsNoTracking().Include(m => m.Versions).ThenInclude(v => v.File).FirstOrDefaultAsync(m => m.TmdbId == request.TmdbId, ct);
        if (movie is null)
        {
            var name = request.FourK ? fourK! : await targets.StandardVersionNameAsync(RequestMediaType.Movie, profile, ct);
            var added = await movies.AddMovieAsync(new AddMovieRequest(request.TmdbId, profile.Id, root.Id, name, Monitored: true, MinimumAvailability.Released, SearchOnAdd: true), ct);
            if (!added.IsSuccess)
            {
                return added.Failure!;
            }

            request.MovieId = added.Value!.Id;
            request.VersionName = name;
            return Result<bool>.Ok(true);
        }

        request.MovieId = movie.Id;
        var slot = request.FourK
            ? movie.Versions.FirstOrDefault(v => string.Equals(v.Name, fourK, StringComparison.OrdinalIgnoreCase))
            : LibraryAvailability.StandardOf(movie.Versions, v => v.IsDefault, v => v.Name, v => v.Id, fourK);
        if (slot is null)
        {
            var name = request.FourK ? fourK! : await targets.StandardVersionNameAsync(RequestMediaType.Movie, profile, ct);
            name = VersionNaming.Unique(name, movie.Versions.Select(v => v.Name));
            var added = await movies.AddVersionAsync(movie.Id, new AddVersionRequest(name, VersionMode.Managed, profile.Id, root.Id, Monitored: true, SearchOnAdd: true), ct);
            if (!added.IsSuccess)
            {
                return added.Failure!;
            }

            request.VersionName = name;
            return Result<bool>.Ok(true);
        }

        // The version is there: wanted again, and searched when it has no file yet.
        request.VersionName = slot.Name;
        if (!slot.Monitored)
        {
            var updated = await movies.UpdateVersionAsync(movie.Id, slot.Id, new UpdateVersionRequest(Monitored: true), ct);
            if (!updated.IsSuccess)
            {
                return updated.Failure!;
            }
        }

        if (slot.File is null)
        {
            searches.TryEnqueue(slot.Id, SearchTrigger.Added);
        }

        return Result<bool>.Ok(true);
    }

    private async Task<Result<bool>> AddSeriesAsync(MediaRequest request, QualityProfile profile, RootFolder root, string? fourK, CancellationToken ct)
    {
        var show = await db.Series.AsNoTracking()
            .Include(s => s.Versions)
            .Where(s => s.TmdbId == request.TmdbId || (request.TvdbId != null && s.TvdbId == request.TvdbId))
            .OrderBy(s => s.TmdbId == request.TmdbId ? 0 : 1)
            .FirstOrDefaultAsync(ct);

        // New episodes are monitored only when every season was asked for: asking for season 1 does not sign up for the rest.
        var newItems = request.AllSeasons ? NewItemMonitoring.All : NewItemMonitoring.None;
        int seriesId;
        int versionId;
        if (show is null)
        {
            var name = request.FourK ? fourK! : await targets.StandardVersionNameAsync(RequestMediaType.Series, profile, ct);
            var added = await series.AddSeriesAsync(new AddSeriesRequest(profile.Id, root.Id, request.TvdbId, request.TmdbId, Monitor: MonitoringOption.None, MonitorNewItems: newItems, VersionName: name), ct);
            if (!added.IsSuccess)
            {
                return added.Failure!;
            }

            seriesId = added.Value!.Id;
            versionId = added.Value.Versions.Single().Id;
            request.VersionName = name;
        }
        else
        {
            seriesId = show.Id;
            var slot = request.FourK
                ? show.Versions.FirstOrDefault(v => string.Equals(v.Name, fourK, StringComparison.OrdinalIgnoreCase))
                : LibraryAvailability.StandardOf(show.Versions, v => v.IsDefault, v => v.Name, v => v.Id, fourK);
            if (slot is null)
            {
                var name = request.FourK ? fourK! : await targets.StandardVersionNameAsync(RequestMediaType.Series, profile, ct);
                name = VersionNaming.Unique(name, show.Versions.Select(v => v.Name));
                var added = await series.AddVersionAsync(seriesId, new AddSeriesVersionRequest(name, profile.Id, root.Id, Monitor: MonitoringOption.None, MonitorNewItems: newItems), ct);
                if (!added.IsSuccess)
                {
                    return added.Failure!;
                }

                versionId = added.Value!.Id;
                request.VersionName = name;
            }
            else
            {
                versionId = slot.Id;
                request.VersionName = slot.Name;
                if (!slot.Monitored)
                {
                    var updated = await series.UpdateVersionAsync(seriesId, slot.Id, new UpdateSeriesVersionRequest(Monitored: true), ct);
                    if (!updated.IsSuccess)
                    {
                        return updated.Failure!;
                    }
                }
            }
        }

        request.SeriesId = seriesId;
        var monitored = 0;
        foreach (var season in request.Seasons)
        {
            if ((await series.SetSeasonMonitoredAsync(seriesId, versionId, season, true, ct)).IsSuccess)
            {
                monitored++;
            }
        }

        if (monitored == 0 && request.Seasons.Count > 0)
        {
            return Failure.Invalid("Seasons not in the library", $"The library's episode list of {request.Title} has none of the seasons asked for ({string.Join(", ", request.Seasons)}).");
        }

        // Search on add, as Seerr asks Sonarr to: the aired episodes of the requested seasons that have no file.
        var now = time.GetUtcNow().UtcDateTime;
        var wanted = (await db.EpisodeTargets.AsNoTracking()
                .Where(t => t.VersionId == versionId && t.EpisodeFileId == null && request.Seasons.Contains(t.Episode.SeasonNumber))
                .Select(t => t.Episode)
                .ToListAsync(ct))
            .Where(e => e.HasAired(now))
            .Select(e => e.Id)
            .ToList();
        if (wanted.Count > 0)
        {
            seriesSearches.TryEnqueue(new SeriesSearchRequest(versionId, wanted, SearchTrigger.Added));
        }

        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Forward mode: the followed Radarr (films) or Sonarr (series) of the request's version, when one is enabled, gets
    /// the title too. Fills the request's forward state; never touches its lifecycle.
    /// </summary>
    public async Task ForwardAsync(MediaRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.VersionName is null || request.QualityProfileId is null || request.RootFolderId is null)
        {
            return;
        }

        var version = request.VersionName.ToLowerInvariant();
        ForwardOutcome outcome;
        string name;
        if (request.MediaType == RequestMediaType.Movie)
        {
            var radarr = (await db.FollowedRadarrs.AsNoTracking().Where(r => r.Enabled).OrderBy(r => r.Id).ToListAsync(ct))
                .FirstOrDefault(r => string.Equals(r.VersionName, version, StringComparison.OrdinalIgnoreCase));
            if (radarr is null)
            {
                request.ForwardStatus = ForwardStatus.None;
                return;
            }

            name = radarr.Name;
            outcome = await forwarder.ForwardMovieAsync(radarr, request.TmdbId, request.Title, request.Year, request.QualityProfileId.Value, request.RootFolderId.Value, ct);
        }
        else
        {
            var sonarr = (await db.FollowedSonarrs.AsNoTracking().Where(r => r.Enabled).OrderBy(r => r.Id).ToListAsync(ct))
                .FirstOrDefault(r => string.Equals(r.VersionName, version, StringComparison.OrdinalIgnoreCase));
            if (sonarr is null)
            {
                request.ForwardStatus = ForwardStatus.None;
                return;
            }

            name = sonarr.Name;
            var tvdbId = request.TvdbId ?? await db.Series.Where(s => s.Id == request.SeriesId).Select(s => s.TvdbId).FirstOrDefaultAsync(ct);
            var type = await db.Series.Where(s => s.Id == request.SeriesId).Select(s => (SeriesType?)s.SeriesType).FirstOrDefaultAsync(ct) ?? SeriesType.Standard;
            outcome = tvdbId is null
                ? new ForwardOutcome(ForwardStatus.Failed, $"Sonarr '{sonarr.Name}' needs a TVDB id, and {request.Title} has none.")
                : await forwarder.ForwardSeriesAsync(sonarr, tvdbId.Value, request.Title, request.Seasons, type, request.QualityProfileId.Value, request.RootFolderId.Value, ct);
        }

        request.ForwardStatus = outcome.Status;
        request.ForwardedTo = name;
        request.ForwardError = outcome.Error;
        request.ForwardedAt = outcome.Status == ForwardStatus.Failed ? request.ForwardedAt : time.GetUtcNow().UtcDateTime;
    }
}
