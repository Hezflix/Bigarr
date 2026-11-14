using System.Security.Claims;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Sonarr;

using Bigarr.Domain.Tv;

/// <summary>
/// <c>/episode</c> and <c>/episodefile</c>. An episode's id is the shared <c>Episode</c> row's: the same id in every
/// version, its <c>monitored</c>, <c>hasFile</c> and <c>episodeFile</c> being those of the version the key reads it
/// through (the <c>seriesId</c> asked for, else the version the view designates). Episode files have their own ids,
/// one per version's file. Bazarr keeps only episodes with an embedded file; Seerr re-monitors requested seasons here.
/// </summary>
public static class SonarrEpisodeEndpoints
{
    public static void MapSonarrEpisodes(this RouteGroupBuilder group)
    {
        group.MapGet("/episode", ListAsync);
        group.MapGet("/episode/{id:int}", GetAsync);
        group.MapPut("/episode/monitor", MonitorAsync);
        group.MapPut("/episode/{id:int}", UpdateAsync);
        group.MapGet("/episodefile", FilesAsync);
        group.MapGet("/episodefile/{id:int}", FileAsync);
    }

    /// <summary>
    /// <c>?seriesId=</c> (Seerr, Bazarr) lists the episodes of that version, optionally one season; <c>?episodeIds=</c>
    /// reads episodes through the view. The file is embedded only with <c>includeEpisodeFile=true</c>, as in Sonarr.
    /// </summary>
    private static async Task<Results<JsonHttpResult<List<SonarrEpisode>>, NotFound, JsonHttpResult<List<RadarrValidationFailure>>>> ListAsync(
        HttpRequest http, ClaimsPrincipal user, SonarrLibrary library, CancellationToken ct, int? seriesId = null, int? seasonNumber = null, bool includeEpisodeFile = false)
    {
        var view = user.CompatView();
        if (seriesId is { } versionId)
        {
            var row = await library.FindAsync(view, versionId, ct);
            if (row is null)
            {
                return TypedResults.NotFound();
            }

            var targets = await library.EpisodesAsync(versionId, ct);
            var path = PathOf(row);
            return SonarrCompat.Ok(targets
                .Where(t => seasonNumber is null || t.Episode.SeasonNumber == seasonNumber)
                .Select(t => SonarrMapper.ToEpisode(WithVersion(t, row.Version), path, includeEpisodeFile))
                .ToList());
        }

        var ids = Ints(http.Query["episodeIds"]).Distinct().ToList();
        if (ids.Count == 0)
        {
            return RadarrCompat.BadRequest("SeriesId", "seriesId or episodeIds must be provided");
        }

        var result = new List<SonarrEpisode>();
        foreach (var (showId, episodeIds) in (await library.SeriesOfEpisodesAsync(ids, ct)).GroupBy(p => p.Value, p => p.Key).Select(g => (g.Key, g.ToList())))
        {
            var show = await library.FindShowAsync(showId, ct);
            var version = show is null ? null : SonarrLibrary.Reading(view, show);
            if (show is null || version is null)
            {
                continue;
            }

            var row = new SonarrRow(show, version);
            var path = PathOf(row);
            result.AddRange((await library.EpisodesAsync(version.Id, episodeIds, ct)).Select(t => SonarrMapper.ToEpisode(WithVersion(t, version), path, includeEpisodeFile)));
        }

        return SonarrCompat.Ok(result);
    }

    /// <summary>Bazarr's single sync on an episode event: always with the file embedded, as Sonarr's <c>GET /episode/{id}</c>.</summary>
    private static async Task<Results<JsonHttpResult<SonarrEpisode>, NotFound>> GetAsync(int id, ClaimsPrincipal user, SonarrLibrary library, CancellationToken ct)
    {
        var read = await ReadAsync(user.CompatView(), id, library, ct);
        return read is null ? TypedResults.NotFound() : SonarrCompat.Ok(SonarrMapper.ToEpisode(read.Value.Target, PathOf(read.Value.Row), withFile: true));
    }

    /// <summary>Seerr's <c>{episodeIds, monitored}</c> after re-requesting seasons. The episodes may span shows; each is set in the version the key designates there.</summary>
    private static async Task<Results<JsonHttpResult<List<SonarrEpisode>>, JsonHttpResult<List<RadarrValidationFailure>>>> MonitorAsync(
        SonarrEpisodesMonitoredRequest request, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, CancellationToken ct)
    {
        var view = user.CompatView();
        if (view.Kind == CompatViewKind.All)
        {
            return RadarrCompat.BadRequest("EpisodeIds", SonarrSeriesEndpoints.AllViewWriteRefused);
        }

        var ids = (request.EpisodeIds ?? []).Distinct().ToList();
        var shows = await library.SeriesOfEpisodesAsync(ids, ct);
        if (shows.Count != ids.Count)
        {
            return RadarrCompat.BadRequest("EpisodeIds", $"Unknown episode {string.Join(", ", ids.Where(i => !shows.ContainsKey(i)))}.");
        }

        var changed = new List<SonarrEpisode>();
        foreach (var group in shows.GroupBy(p => p.Value, p => p.Key))
        {
            var show = await library.FindShowAsync(group.Key, ct);
            var version = show is null ? null : SonarrLibrary.Designated(view, show);
            if (show is null || version is null)
            {
                return RadarrCompat.BadRequest("EpisodeIds", $"Episodes {string.Join(", ", group)} are outside this key's view.");
            }

            var episodeIds = group.ToList();
            var result = await series.SetEpisodesMonitoredAsync(show.Id, version.Id, episodeIds, request.Monitored, ct);
            if (!result.IsSuccess)
            {
                return RadarrCompat.BadRequest("EpisodeIds", result.Failure!.Detail ?? result.Failure.Title);
            }

            var path = PathOf(new SonarrRow(show, version));
            changed.AddRange((await library.EpisodesAsync(version.Id, episodeIds, ct)).Select(t => SonarrMapper.ToEpisode(WithVersion(t, version), path, withFile: false)));
        }

        return SonarrCompat.Accepted(changed);
    }

    /// <summary>Sonarr's episode toggle: only <c>monitored</c> is read.</summary>
    private static async Task<Results<JsonHttpResult<SonarrEpisode>, NotFound, JsonHttpResult<List<RadarrValidationFailure>>>> UpdateAsync(
        int id, SonarrEpisodeRequest request, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, CancellationToken ct)
    {
        var view = user.CompatView();
        if (view.Kind == CompatViewKind.All)
        {
            return RadarrCompat.BadRequest("Id", SonarrSeriesEndpoints.AllViewWriteRefused);
        }

        var read = await ReadAsync(view, id, library, ct);
        if (read is null)
        {
            return TypedResults.NotFound();
        }

        var (row, _) = read.Value;
        var result = await series.SetEpisodesMonitoredAsync(row.Series.Id, row.Version.Id, [id], request.Monitored, ct);
        if (!result.IsSuccess)
        {
            return RadarrCompat.BadRequest("Id", result.Failure!.Detail ?? result.Failure.Title);
        }

        var fresh = await ReadAsync(view, id, library, ct);
        return SonarrCompat.Accepted(SonarrMapper.ToEpisode(fresh!.Value.Target, PathOf(fresh.Value.Row), withFile: true));
    }

    private static async Task<Results<JsonHttpResult<List<SonarrEpisodeFile>>, NotFound, JsonHttpResult<List<RadarrValidationFailure>>>> FilesAsync(
        HttpRequest http, ClaimsPrincipal user, SonarrLibrary library, CancellationToken ct, int? seriesId = null)
    {
        var view = user.CompatView();
        if (seriesId is { } versionId)
        {
            var row = await library.FindAsync(view, versionId, ct);
            if (row is null)
            {
                return TypedResults.NotFound();
            }

            var path = PathOf(row);
            return SonarrCompat.Ok((await library.FilesAsync(versionId, ct)).Select(f => SonarrMapper.ToEpisodeFile(f, row.Version.QualityProfile, path)).ToList());
        }

        var ids = Ints(http.Query["episodeFileIds"]).Distinct().ToList();
        if (ids.Count == 0)
        {
            return RadarrCompat.BadRequest("SeriesId", "seriesId or episodeFileIds must be provided");
        }

        var result = new List<SonarrEpisodeFile>();
        foreach (var file in await library.FilesAsync(ids, ct))
        {
            if (await library.FindAsync(view, file.VersionId, ct) is { } row)
            {
                result.Add(SonarrMapper.ToEpisodeFile(file, row.Version.QualityProfile, PathOf(row)));
            }
        }

        return SonarrCompat.Ok(result);
    }

    /// <summary>Called by Bazarr for every episode with a file on an episode event: a file whose version the key does not see is a 404.</summary>
    private static async Task<Results<JsonHttpResult<SonarrEpisodeFile>, NotFound>> FileAsync(int id, ClaimsPrincipal user, SonarrLibrary library, CancellationToken ct)
    {
        var file = await library.FileAsync(id, ct);
        var row = file is null ? null : await library.FindAsync(user.CompatView(), file.VersionId, ct);
        return file is null || row is null ? TypedResults.NotFound() : SonarrCompat.Ok(SonarrMapper.ToEpisodeFile(file, row.Version.QualityProfile, PathOf(row)));
    }

    /// <summary>The episode read through the version the view designates in its show (the default one for <c>all</c>), or null.</summary>
    private static async Task<(SonarrRow Row, EpisodeTarget Target)?> ReadAsync(CompatView view, int episodeId, SonarrLibrary library, CancellationToken ct)
    {
        var showId = await library.SeriesOfEpisodeAsync(episodeId, ct);
        var show = showId is null ? null : await library.FindShowAsync(showId.Value, ct);
        var version = show is null ? null : SonarrLibrary.Reading(view, show);
        if (show is null || version is null)
        {
            return null;
        }

        var target = (await library.EpisodesAsync(version.Id, [episodeId], ct)).FirstOrDefault();
        return target is null ? null : (new SonarrRow(show, version), WithVersion(target, version));
    }

    /// <summary>The target loaded without its version: give it the one already in hand (its profile judges the file's cutoff).</summary>
    private static EpisodeTarget WithVersion(EpisodeTarget target, SeriesVersion version)
    {
        target.Version = version;
        return target;
    }

    private static string PathOf(SonarrRow row) => Path.Combine(row.Version.RootFolder.Path, row.Version.FolderName);

    public static IEnumerable<int> Ints(Microsoft.Extensions.Primitives.StringValues values) =>
        values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => int.TryParse(v, out var i) ? i : (int?)null)
            .OfType<int>();
}
