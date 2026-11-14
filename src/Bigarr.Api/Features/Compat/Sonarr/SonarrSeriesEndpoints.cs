using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Sonarr;

using Bigarr.Domain.Tv;

/// <summary>
/// <c>/series</c>: one line per series version the key sees, the line's id being the version's. Seerr's add flow always
/// starts with <c>lookup?term=tvdb:</c>: an <c>id</c> in the answer means "in the library" and leads to a <c>PUT</c>,
/// none to a <c>POST</c>, which creates the version the key designates. Two Sonarr entries in one Seerr, one per key,
/// so follow two versions of the same show without noticing each other.
/// </summary>
public static partial class SonarrSeriesEndpoints
{
    public static void MapSonarrSeries(this RouteGroupBuilder group)
    {
        group.MapGet("/series", ListAsync);
        group.MapGet("/series/lookup", LookupAsync);
        group.MapGet("/series/{id:int}", GetAsync);
        group.MapPost("/series", AddAsync);
        group.MapPut("/series", (SonarrSeriesRequest request, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, CancellationToken ct) =>
            UpdateAsync(request.Id, request, user, library, series, ct));
        group.MapPut("/series/{id:int}", (int id, SonarrSeriesRequest request, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, CancellationToken ct) =>
            UpdateAsync(id, request, user, library, series, ct));
        group.MapDelete("/series/{id:int}", DeleteAsync);
    }

    /// <summary>The message of the 400 every write through an <c>all</c> key gets: episode ids are shared, such a key designates no version.</summary>
    public const string AllViewWriteRefused = "This key sees every version and designates none. Change series with a version:<name> or default key, or from Bigarr.";

    private static async Task<JsonHttpResult<List<SonarrSeries>>> ListAsync(ClaimsPrincipal user, SonarrLibrary library, CancellationToken ct, int? tvdbId = null)
    {
        var rows = await library.RowsAsync(user.CompatView(), tvdbId, ct);
        return SonarrCompat.Ok(await ToSeriesAsync(rows, library, ct));
    }

    private static async Task<Results<JsonHttpResult<SonarrSeries>, NotFound>> GetAsync(int id, ClaimsPrincipal user, SonarrLibrary library, CancellationToken ct)
    {
        var row = await library.FindAsync(user.CompatView(), id, ct);
        return row is null ? TypedResults.NotFound() : SonarrCompat.Ok(await ToSeriesAsync(row, library, ct));
    }

    /// <summary>
    /// Seerr's existence test and add template. <c>tvdb:{id}</c> answers one row: the full resource with <c>id</c> when the
    /// key's view sees a version of the show, else the show's metadata (its season list included) without <c>id</c>.
    /// Other terms search by name; hits without a TVDB id are left out, Seerr adds by <c>tvdbId</c>.
    /// </summary>
    private static async Task<Results<JsonHttpResult<List<SonarrSeries>>, JsonHttpResult<RadarrError>>> LookupAsync(
        string? term, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, ISeriesMetadataProvider metadata, ILoggerFactory loggers, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return RadarrCompat.Error(StatusCodes.Status400BadRequest, "A search term is required.");
        }

        var view = user.CompatView();
        if (TvdbTermRegex().Match(term.Trim()) is { Success: true } match)
        {
            var tvdbId = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
            if (await library.FindShowByTvdbAsync(tvdbId, ct) is { } show)
            {
                var version = SonarrLibrary.Reading(view, show);
                return SonarrCompat.Ok(new List<SonarrSeries>
                {
                    version is null ? SonarrMapper.ToLookup(show) : await ToSeriesAsync(new SonarrRow(show, version), library, ct),
                });
            }

            SeriesMetadata? found;
            try
            {
                found = await metadata.GetByTvdbIdAsync(tvdbId, withEpisodes: true, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                loggers.CreateLogger("Bigarr.Compat.Sonarr").LogWarning(ex, "Series metadata provider failed for Sonarr lookup tvdb:{TvdbId}", tvdbId);
                return RadarrCompat.Error(StatusCodes.Status502BadGateway, "Metadata provider unavailable.");
            }

            return SonarrCompat.Ok(found is null ? [] : new List<SonarrSeries> { SonarrMapper.ToLookup(found with { TvdbId = found.TvdbId ?? tvdbId }) });
        }

        var result = await series.LookupAsync(term, ct);
        if (!result.IsSuccess)
        {
            return RadarrCompat.Error(result.Failure!.StatusCode, result.Failure.Detail ?? result.Failure.Title);
        }

        var resources = new List<SonarrSeries>();
        foreach (var hit in result.Value!.Where(h => h.TvdbId is > 0))
        {
            SonarrRow? row = null;
            if (hit.ExistingSeriesId is { } seriesId && await library.FindShowAsync(seriesId, ct) is { } show && SonarrLibrary.Reading(view, show) is { } version)
            {
                row = new SonarrRow(show, version);
            }

            resources.Add(row is null ? SonarrMapper.ToLookup(hit) : await ToSeriesAsync(row, library, ct));
        }

        return SonarrCompat.Ok(resources);
    }

    /// <summary>
    /// Seerr's add, new shows only. A show not in the library is added with the key's version (named after the view, or
    /// after the profile for a default key); a show already there gets the version the key designates, or a 400 when the
    /// view already sees one, like Sonarr's "already added". Without <c>addOptions.monitor</c> (Seerr sends none) the
    /// episodes follow <c>seasons[].monitored</c>, as in Sonarr; <c>searchForMissingEpisodes</c> queues a search of the
    /// new version's missing episodes.
    /// </summary>
    private static async Task<Results<JsonHttpResult<SonarrSeries>, JsonHttpResult<List<RadarrValidationFailure>>, JsonHttpResult<RadarrError>>> AddAsync(
        SonarrSeriesRequest request, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, RootFolderService roots, SeriesSearchQueue searches, ILoggerFactory loggers, CancellationToken ct)
    {
        var view = user.CompatView();
        if (view.Kind == CompatViewKind.All)
        {
            return RadarrCompat.BadRequest("TvdbId", AllViewWriteRefused);
        }

        if (request.TvdbId <= 0)
        {
            return RadarrCompat.BadRequest("TvdbId", "A TVDB id is required.");
        }

        if (request.QualityProfileId <= 0)
        {
            return RadarrCompat.BadRequest("QualityProfileId", "A quality profile is required.");
        }

        var root = await roots.FindByPathAsync(request.RootFolderPath, ct);
        if (root is null || root.Kind != MediaKind.Series)
        {
            return RadarrCompat.BadRequest("RootFolderPath", $"Unknown series root folder '{request.RootFolderPath}'. Add it in Bigarr first.");
        }

        var monitor = SonarrMapper.ParseMonitor(request.AddOptions?.Monitor);
        var newItems = SonarrMapper.ParseNewItems(request.MonitorNewItems) ?? NewItemMonitoring.All;
        var show = await library.FindShowByTvdbAsync(request.TvdbId, ct);
        int seriesId;
        int versionId;
        if (show is null)
        {
            var name = view.Kind == CompatViewKind.Version ? view.VersionName : null;
            var added = await series.AddSeriesAsync(
                new AddSeriesRequest(
                    request.QualityProfileId,
                    root.Id,
                    TvdbId: request.TvdbId,
                    SeriesType: SonarrMapper.ParseType(request.SeriesType),
                    Monitor: monitor ?? MonitoringOption.None,
                    SeasonFolder: request.SeasonFolder ?? true,
                    MonitorNewItems: newItems,
                    VersionName: name,
                    Monitored: request.Monitored,
                    TagIds: request.Tags),
                ct);
            if (!added.IsSuccess)
            {
                return Translate(added.Failure!);
            }

            seriesId = added.Value!.Id;
            versionId = added.Value.Versions.Single().Id;
        }
        else
        {
            if (SonarrLibrary.Designated(view, show) is not null)
            {
                return RadarrCompat.BadRequest("TvdbId", "This series has already been added");
            }

            var added = await series.AddVersionAsync(
                show.Id,
                new AddSeriesVersionRequest(view.VersionName!, request.QualityProfileId, root.Id, Monitor: monitor ?? MonitoringOption.None, SeasonFolder: request.SeasonFolder ?? true, MonitorNewItems: newItems, Monitored: request.Monitored, TagIds: request.Tags),
                ct);
            if (!added.IsSuccess)
            {
                return Translate(added.Failure!);
            }

            seriesId = show.Id;
            versionId = added.Value!.Id;
        }

        if (monitor is null)
        {
            foreach (var season in (request.Seasons ?? []).Where(s => s.Monitored).Select(s => s.SeasonNumber).Distinct())
            {
                var flagged = await series.SetSeasonMonitoredAsync(seriesId, versionId, season, true, ct);
                if (!flagged.IsSuccess)
                {
                    loggers.CreateLogger("Bigarr.Compat.Sonarr").LogInformation("Sonarr add of TVDB {TvdbId}: season {Season} is not in the show, not monitored", request.TvdbId, season);
                }
            }
        }

        if (request.AddOptions?.SearchForMissingEpisodes == true)
        {
            searches.TryEnqueue(new SeriesSearchRequest(versionId, null, SearchTrigger.User));
        }

        var row = await library.FindAsync(view, versionId, ct);
        return row is null
            ? RadarrCompat.Error(StatusCodes.Status500InternalServerError, "The version was created but is not visible to this key.")
            : SonarrCompat.Created(await ToSeriesAsync(row, library, ct));
    }

    /// <summary>
    /// Seerr's re-request of a show in the library, always through here (monitored or not): the lookup resource comes back
    /// with <c>monitored</c>, <c>tags</c> and the requested seasons flipped on. A season whose flag changes takes its
    /// episodes with it, as in Sonarr. Answers 202 with the updated resource.
    /// </summary>
    private static async Task<Results<JsonHttpResult<SonarrSeries>, JsonHttpResult<List<RadarrValidationFailure>>, NotFound>> UpdateAsync(
        int id, SonarrSeriesRequest request, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, CancellationToken ct)
    {
        var view = user.CompatView();
        var row = await library.FindAsync(view, id, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        if (view.Kind == CompatViewKind.All)
        {
            return RadarrCompat.BadRequest("Id", AllViewWriteRefused);
        }

        var update = new UpdateSeriesVersionRequest(
            QualityProfileId: request.QualityProfileId > 0 ? request.QualityProfileId : null,
            Monitored: request.Monitored,
            SeasonFolder: request.SeasonFolder,
            MonitorNewItems: SonarrMapper.ParseNewItems(request.MonitorNewItems),
            TagIds: request.Tags);
        var updated = await series.UpdateVersionAsync(row.Series.Id, id, update, ct);
        if (!updated.IsSuccess)
        {
            return RadarrCompat.BadRequest("Id", updated.Failure!.Detail ?? updated.Failure.Title);
        }

        if (SonarrMapper.ParseType(request.SeriesType) is { } type && type != row.Series.SeriesType)
        {
            await series.UpdateSeriesAsync(row.Series.Id, new UpdateSeriesRequest(type), ct);
        }

        var numbers = row.Series.Seasons.ToDictionary(s => s.Id, s => s.SeasonNumber);
        var current = row.Version.Seasons.Where(s => numbers.ContainsKey(s.SeasonId)).ToDictionary(s => numbers[s.SeasonId], s => s.Monitored);
        foreach (var season in request.Seasons ?? [])
        {
            if (current.TryGetValue(season.SeasonNumber, out var monitored) && monitored != season.Monitored)
            {
                await series.SetSeasonMonitoredAsync(row.Series.Id, id, season.SeasonNumber, season.Monitored, ct);
            }
        }

        var fresh = await library.FindAsync(view, id, ct);
        return SonarrCompat.Accepted(await ToSeriesAsync(fresh!, library, ct));
    }

    /// <summary>
    /// Deletes the version (Seerr always sends <c>deleteFiles=true</c>); the last one takes the show with it; a default
    /// version hands over to the oldest managed sibling, as <c>DELETE /movie/{id}</c> does.
    /// </summary>
    private static async Task<Results<Ok, NotFound, JsonHttpResult<List<RadarrValidationFailure>>, JsonHttpResult<RadarrError>>> DeleteAsync(
        int id, ClaimsPrincipal user, SonarrLibrary library, SeriesService series, CancellationToken ct, bool deleteFiles = false, bool addImportExclusion = false)
    {
        var view = user.CompatView();
        var row = await library.FindAsync(view, id, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        if (view.Kind == CompatViewKind.All)
        {
            return RadarrCompat.BadRequest("Id", AllViewWriteRefused);
        }

        int? promote = null;
        var siblings = row.Series.Versions.Where(v => v.Id != id).ToList();
        if (row.Version.IsDefault && siblings.Count > 0)
        {
            promote = siblings.Where(v => v.Mode == Bigarr.Domain.Movies.VersionMode.Managed).OrderBy(v => v.Id).Select(v => (int?)v.Id).FirstOrDefault();
            if (promote is null)
            {
                return RadarrCompat.BadRequest("Id", "This is the series' default version and no other managed version can take over. Delete the series from Bigarr instead.");
            }
        }

        var result = await series.DeleteVersionAsync(row.Series.Id, id, promote, ct, deleteFiles);
        return result.IsSuccess ? TypedResults.Ok() : RadarrCompat.Error(result.Failure!.StatusCode, result.Failure.Detail ?? result.Failure.Title);
    }

    public static async Task<SonarrSeries> ToSeriesAsync(SonarrRow row, SonarrLibrary library, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(library);
        var statistics = await library.StatisticsAsync([row.Version.Id], ct);
        return SonarrMapper.ToSeries(row, statistics);
    }

    private static async Task<List<SonarrSeries>> ToSeriesAsync(List<SonarrRow> rows, SonarrLibrary library, CancellationToken ct)
    {
        var statistics = await library.StatisticsAsync(rows.Select(r => r.Version.Id).ToList(), ct);
        return rows.Select(r => SonarrMapper.ToSeries(r, statistics)).ToList();
    }

    /// <summary>Slice failures in Sonarr's terms: a 400 with a validation list, or a plain error for the rest.</summary>
    private static Results<JsonHttpResult<SonarrSeries>, JsonHttpResult<List<RadarrValidationFailure>>, JsonHttpResult<RadarrError>> Translate(Failure failure) => failure.StatusCode switch
    {
        StatusCodes.Status404NotFound => RadarrCompat.BadRequest("TvdbId", "Series not found on TVDB."),
        StatusCodes.Status409Conflict => RadarrCompat.BadRequest("TvdbId", "This series has already been added"),
        StatusCodes.Status400BadRequest => RadarrCompat.BadRequest(failure.Title.Contains("profile", StringComparison.OrdinalIgnoreCase) ? "QualityProfileId" : "TvdbId", failure.Detail ?? failure.Title),
        _ => RadarrCompat.Error(failure.StatusCode, failure.Detail ?? failure.Title),
    };

    [GeneratedRegex(@"^tvdb:\s*(?<id>\d{1,10})$", RegexOptions.IgnoreCase)]
    private static partial Regex TvdbTermRegex();
}
