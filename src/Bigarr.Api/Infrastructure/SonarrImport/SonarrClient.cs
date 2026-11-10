using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.RadarrImport;

namespace Bigarr.Api.Infrastructure.SonarrImport;

/// <summary>Where an existing Sonarr answers: its URL (with any URL base) and its API key.</summary>
public sealed record SonarrConnection(string Url, string ApiKey)
{
    /// <summary>Scheme, host, port and URL base without a trailing slash; <c>/api/v3</c> is appended by the client.</summary>
    public string BaseUrl
    {
        get
        {
            var url = Url.Trim().TrimEnd('/');
            return url.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase) ? url[..^"/api/v3".Length] : url;
        }
    }
}

// The subset of Sonarr's v3 API (served as is by Sonarr v4) the import and the follow read. Names follow Sonarr's JSON;
// unknown fields are ignored. Shapes Sonarr shares with Radarr (tags, root folders, quality profiles, qualities, languages,
// images, custom format refs) reuse the Radarr records: the JSON is the same.

/// <summary>Sonarr's <c>alternateTitles</c>: scene names, some standing for one scene season only.</summary>
public sealed record SonarrAlternateTitle(string? Title, int? SeasonNumber, int? SceneSeasonNumber);

public sealed record SonarrRating(int Votes, double Value);

public sealed record SonarrSeasonStatistics(int EpisodeFileCount, int EpisodeCount, int TotalEpisodeCount, long SizeOnDisk);

public sealed record SonarrSeason(int SeasonNumber, bool Monitored, SonarrSeasonStatistics? Statistics = null);

public sealed record SonarrSeriesStatistics(int SeasonCount, int EpisodeFileCount, int EpisodeCount, int TotalEpisodeCount, long SizeOnDisk);

public sealed record SonarrSeries(
    int Id,
    string Title,
    string? SortTitle,
    string? Status,
    bool? Ended,
    string? Overview,
    string? Network,
    string? AirTime,
    List<RadarrImage>? Images,
    RadarrLanguageRef? OriginalLanguage,
    List<SonarrSeason>? Seasons,
    int Year,
    string? Path,
    string? RootFolderPath,
    int QualityProfileId,
    bool SeasonFolder,
    bool Monitored,
    bool UseSceneNumbering,
    int Runtime,
    int TvdbId,
    string? ImdbId,
    DateTime? FirstAired,
    DateTime? LastAired,
    string? SeriesType,
    string? Certification,
    List<string>? Genres,
    List<int>? Tags,
    DateTime? Added,
    SonarrRating? Ratings,
    List<SonarrAlternateTitle>? AlternateTitles,
    SonarrSeriesStatistics? Statistics,
    string? TitleSlug = null,
    /// <summary>Sonarr v4: TMDB id of the show (0 when unknown).</summary>
    int TmdbId = 0,
    /// <summary>Sonarr v4: <c>all</c> or <c>none</c>, whether seasons that appear later are monitored.</summary>
    string? MonitorNewItems = null,
    /// <summary>Sonarr v3 only: the language profile (v4 has none).</summary>
    int? LanguageProfileId = null,
    string? OriginalCountry = null);

public sealed record SonarrEpisode(
    int Id,
    int SeriesId,
    int TvdbId,
    int EpisodeFileId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    string? AirDate,
    DateTime? AirDateUtc,
    int Runtime,
    string? Overview,
    bool HasFile,
    bool Monitored,
    int? AbsoluteEpisodeNumber,
    int? SceneAbsoluteEpisodeNumber,
    int? SceneEpisodeNumber,
    int? SceneSeasonNumber,
    bool UnverifiedSceneNumbering);

public sealed record SonarrEpisodeFile(
    int Id,
    int SeriesId,
    int SeasonNumber,
    string? RelativePath,
    string? Path,
    long Size,
    DateTime? DateAdded,
    string? SceneName,
    string? ReleaseGroup,
    /// <summary>Sonarr v4: every language of the file.</summary>
    List<RadarrLanguageRef>? Languages,
    /// <summary>Sonarr v3: the one language of the file.</summary>
    RadarrLanguageRef? Language,
    RadarrQuality? Quality,
    int IndexerFlags = 0,
    int? CustomFormatScore = null);

/// <summary>A language profile of Sonarr v3 (gone in v4, where languages live in custom formats).</summary>
public sealed record SonarrLanguageProfile(int Id, string Name);

/// <summary>
/// One line of Sonarr's history: one episode each, a grab of a season pack writes one per episode with the same download
/// id. <paramref name="EventType"/> is Sonarr's camelCase name (<c>grabbed</c>, <c>downloadFolderImported</c>…).
/// </summary>
public sealed record SonarrHistoryItem(
    int Id,
    int EpisodeId,
    int SeriesId,
    string? SourceTitle,
    RadarrQuality? Quality,
    List<RadarrCustomFormatRef>? CustomFormats,
    DateTime Date,
    string? DownloadId,
    string? EventType,
    Dictionary<string, string?>? Data,
    List<RadarrLanguageRef>? Languages = null,
    int? CustomFormatScore = null,
    /// <summary>With <c>includeEpisode=true</c>: the episode the line is about.</summary>
    SonarrEpisode? Episode = null);

public sealed record SonarrHistoryPage(int Page, int PageSize, int TotalRecords, List<SonarrHistoryItem>? Records);

/// <summary>A release Sonarr blocklisted, with the episodes it was for. Like Radarr's, no guid and no info hash.</summary>
public sealed record SonarrBlocklistItem(int Id, int SeriesId, List<int>? EpisodeIds, string? SourceTitle, DateTime Date, string? Indexer, string? Message);

public sealed record SonarrBlocklistPage(int Page, int PageSize, int TotalRecords, List<SonarrBlocklistItem>? Records);

public sealed record SonarrExclusion(int Id, int TvdbId, string? Title);

/// <summary>What Sonarr's parser reads in a release title.</summary>
public sealed record SonarrParsedEpisodeInfo(
    string? SeriesTitle,
    RadarrQuality? Quality,
    string? ReleaseGroup,
    List<RadarrLanguageRef>? Languages,
    int SeasonNumber,
    List<int>? EpisodeNumbers,
    List<int>? AbsoluteEpisodeNumbers,
    bool FullSeason);

/// <summary>
/// Sonarr's <c>GET /api/v3/parse?title=</c>: the series of its library and the episodes the title maps to (absent when
/// none), the quality, and the custom formats and score in that series' profile.
/// </summary>
public sealed record SonarrParseResult(
    string? Title,
    SonarrParsedEpisodeInfo? ParsedEpisodeInfo,
    SonarrSeries? Series,
    List<SonarrEpisode>? Episodes,
    List<RadarrCustomFormatRef>? CustomFormats,
    int CustomFormatScore);

/// <summary>Raised when Sonarr cannot be reached or refuses the key; the message is meant for the screen.</summary>
public sealed class SonarrUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads an existing Sonarr over its v3 API (Sonarr v3 and v4 both serve it), to migrate its library into Bigarr (S4) and
/// to follow it in shadow mode. Only ever GETs.
/// </summary>
public sealed class SonarrClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "bigarr-sonarr-import";

    public const int HistoryPageSize = 1000;

    public const int BlocklistPageSize = 500;

    /// <summary>A bound on the pages read, so a Sonarr that keeps answering full pages cannot hold the import forever.</summary>
    private const int MaxPages = 2000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<RadarrSystemStatus> StatusAsync(SonarrConnection connection, CancellationToken ct) =>
        GetAsync<RadarrSystemStatus>(connection, "/system/status", ct);

    public Task<List<RadarrProfile>> ProfilesAsync(SonarrConnection connection, CancellationToken ct) =>
        GetAsync<List<RadarrProfile>>(connection, "/qualityprofile", ct);

    /// <summary>Sonarr v3's language profiles; v4 has none (the route answers 404, or an empty list).</summary>
    public Task<List<SonarrLanguageProfile>> LanguageProfilesAsync(SonarrConnection connection, CancellationToken ct) =>
        OrEmptyAsync(() => GetAsync<List<SonarrLanguageProfile>>(connection, "/languageprofile", ct));

    public Task<List<RadarrRootFolder>> RootFoldersAsync(SonarrConnection connection, CancellationToken ct) =>
        GetAsync<List<RadarrRootFolder>>(connection, "/rootfolder", ct);

    public Task<List<RadarrTag>> TagsAsync(SonarrConnection connection, CancellationToken ct) =>
        GetAsync<List<RadarrTag>>(connection, "/tag", ct);

    public Task<List<SonarrSeries>> SeriesAsync(SonarrConnection connection, CancellationToken ct) =>
        GetAsync<List<SonarrSeries>>(connection, "/series", ct);

    /// <summary>One series of the library, or null when Sonarr no longer has it.</summary>
    public async Task<SonarrSeries?> SeriesAsync(SonarrConnection connection, int id, CancellationToken ct)
    {
        try
        {
            return await GetAsync<SonarrSeries>(connection, $"/series/{id}", ct);
        }
        catch (SonarrUnavailableException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public Task<List<SonarrEpisode>> EpisodesAsync(SonarrConnection connection, int seriesId, CancellationToken ct) =>
        GetAsync<List<SonarrEpisode>>(connection, $"/episode?seriesId={seriesId}", ct);

    public Task<List<SonarrEpisodeFile>> EpisodeFilesAsync(SonarrConnection connection, int seriesId, CancellationToken ct) =>
        GetAsync<List<SonarrEpisodeFile>>(connection, $"/episodefile?seriesId={seriesId}", ct);

    /// <summary>Import list exclusions; a Sonarr without the route answers with an empty list rather than a failure.</summary>
    public Task<List<SonarrExclusion>> ExclusionsAsync(SonarrConnection connection, CancellationToken ct) =>
        OrEmptyAsync(() => GetAsync<List<SonarrExclusion>>(connection, "/importlistexclusion", ct));

    /// <summary>How many lines Sonarr's history holds; 0 when the route is missing.</summary>
    public async Task<int> HistoryCountAsync(SonarrConnection connection, CancellationToken ct)
    {
        try
        {
            return (await GetAsync<SonarrHistoryPage>(connection, "/history?page=1&pageSize=1", ct)).TotalRecords;
        }
        catch (SonarrUnavailableException ex) when (IsNotFound(ex))
        {
            return 0;
        }
    }

    /// <summary>Sonarr's whole history, oldest first, read page by page; an empty list when the route is missing.</summary>
    public async Task<List<SonarrHistoryItem>> HistoryAsync(SonarrConnection connection, CancellationToken ct)
    {
        var all = new List<SonarrHistoryItem>();
        for (var page = 1; page <= MaxPages; page++)
        {
            SonarrHistoryPage result;
            try
            {
                result = await GetAsync<SonarrHistoryPage>(connection, $"/history?page={page}&pageSize={HistoryPageSize}&sortKey=date&sortDirection=ascending", ct);
            }
            catch (SonarrUnavailableException ex) when (page == 1 && IsNotFound(ex))
            {
                return [];
            }

            var records = result.Records ?? [];
            all.AddRange(records);
            if (records.Count < HistoryPageSize || all.Count >= result.TotalRecords)
            {
                break;
            }
        }

        return all;
    }

    /// <summary>
    /// Every history line from <paramref name="since"/> on, oldest first, each with its episode: what the follow reads on
    /// each pass. Sonarr includes the line dated exactly <paramref name="since"/>.
    /// </summary>
    public async Task<List<SonarrHistoryItem>> HistorySinceAsync(SonarrConnection connection, DateTime since, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind(since, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var lines = await GetAsync<List<SonarrHistoryItem>>(connection, $"/history/since?date={Uri.EscapeDataString(date)}&includeEpisode=true", ct);
        return lines.OrderBy(h => h.Date).ThenBy(h => h.Id).ToList();
    }

    /// <summary>What Sonarr makes of a release title: the series and episodes it maps it to, its quality, custom formats and score.</summary>
    public Task<SonarrParseResult> ParseAsync(SonarrConnection connection, string title, CancellationToken ct) =>
        GetAsync<SonarrParseResult>(connection, $"/parse?title={Uri.EscapeDataString(title)}", ct);

    /// <summary>How many releases Sonarr's blocklist holds; 0 when the route is missing.</summary>
    public async Task<int> BlocklistCountAsync(SonarrConnection connection, CancellationToken ct)
    {
        try
        {
            return (await GetAsync<SonarrBlocklistPage>(connection, "/blocklist?page=1&pageSize=1", ct)).TotalRecords;
        }
        catch (SonarrUnavailableException ex) when (IsNotFound(ex))
        {
            return 0;
        }
    }

    /// <summary>Sonarr's whole blocklist, oldest first, read page by page; an empty list when the route is missing.</summary>
    public async Task<List<SonarrBlocklistItem>> BlocklistAsync(SonarrConnection connection, CancellationToken ct)
    {
        var all = new List<SonarrBlocklistItem>();
        for (var page = 1; page <= MaxPages; page++)
        {
            SonarrBlocklistPage result;
            try
            {
                result = await GetAsync<SonarrBlocklistPage>(connection, $"/blocklist?page={page}&pageSize={BlocklistPageSize}&sortKey=date&sortDirection=ascending", ct);
            }
            catch (SonarrUnavailableException ex) when (page == 1 && IsNotFound(ex))
            {
                return [];
            }

            var records = result.Records ?? [];
            all.AddRange(records);
            if (records.Count < BlocklistPageSize || all.Count >= result.TotalRecords)
            {
                break;
            }
        }

        return all;
    }

    private static bool IsNotFound(SonarrUnavailableException ex) => ex.Message.Contains("HTTP 404", StringComparison.Ordinal);

    private static async Task<List<T>> OrEmptyAsync<T>(Func<Task<List<T>>> read)
    {
        try
        {
            return await read();
        }
        catch (SonarrUnavailableException ex) when (IsNotFound(ex))
        {
            return [];
        }
    }

    private async Task<T> GetAsync<T>(SonarrConnection connection, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new SonarrUnavailableException("The Sonarr URL must be absolute and start with http:// or https://.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, connection.BaseUrl + "/api/v3" + path);
        request.Headers.TryAddWithoutValidation("X-Api-Key", connection.ApiKey.Trim());
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            throw new SonarrUnavailableException(ex is TaskCanceledException ? "Sonarr did not answer before the timeout." : $"Sonarr could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new SonarrUnavailableException("Sonarr refused the API key (HTTP 401).");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SonarrUnavailableException($"Sonarr answered HTTP {(int)response.StatusCode} on {PathOnly(path)}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new SonarrUnavailableException($"Sonarr answered an empty body on {PathOnly(path)}.");
            }
            catch (JsonException ex)
            {
                throw new SonarrUnavailableException($"Sonarr's answer on {PathOnly(path)} is not the JSON of a Sonarr v3 API: {ex.Message}", ex);
            }
        }
    }

    /// <summary>The route without its query, so a release title or a date never ends up in a message.</summary>
    private static string PathOnly(string path) => path.Split('?')[0];
}

/// <summary>
/// Sonarr and Bigarr number qualities differently: Bigarr uses Radarr's ids (its parsers share them), Sonarr has its own
/// for the Bluray-480p/576p and remux qualities. Everything else carries the same id and name.
/// </summary>
public static class SonarrQualities
{
    private static readonly Dictionary<int, int> SonarrToBigarr = new()
    {
        [13] = 20, // Bluray-480p
        [20] = 30, // Bluray-1080p Remux → Remux-1080p
        [21] = 31, // Bluray-2160p Remux → Remux-2160p
        [22] = 21, // Bluray-576p
    };

    private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bluray-480p"] = 20,
        ["Bluray-576p"] = 21,
        ["Bluray-1080p Remux"] = 30,
        ["Bluray-2160p Remux"] = 31,
    };

    /// <summary>Bigarr's id of a Sonarr quality; the name decides when Sonarr's id is not one of the shared ones.</summary>
    public static int ToBigarr(int sonarrId, string? name = null)
    {
        if (name is not null && ByName.TryGetValue(name.Trim(), out var byName))
        {
            return byName;
        }

        return SonarrToBigarr.GetValueOrDefault(sonarrId, sonarrId);
    }

    /// <summary>Bigarr's quality of a Sonarr quality block, or null when Sonarr gives none or Unknown.</summary>
    public static RadarrQualityDefinition? ToBigarr(RadarrQualityDefinition? quality)
    {
        if (quality is null)
        {
            return null;
        }

        var id = ToBigarr(quality.Id, quality.Name);
        string? name;
        try
        {
            name = Bigarr.Parsing.Qualities.Quality.FindById(id)?.Name;
        }
        catch (Exception)
        {
            name = null;
        }

        return quality with { Id = id, Name = string.IsNullOrEmpty(name) ? quality.Name : name };
    }

    /// <summary>A Sonarr quality profile in Bigarr's quality ids, so the Radarr profile rules (qualities, cutoff, scores) apply as is.</summary>
    public static RadarrProfile ToBigarr(RadarrProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        RadarrProfileItem Map(RadarrProfileItem item) => item with
        {
            Quality = ToBigarr(item.Quality),
            Items = item.Items?.Select(Map).ToList(),
        };

        var items = profile.Items?.Select(Map).ToList();
        // The cutoff is a quality id or a group id (from 1000): only a quality id needs translating.
        var cutoff = profile.Cutoff is { } c && c < 1000 ? ToBigarr(c) : profile.Cutoff;
        return profile with { Items = items, Cutoff = cutoff };
    }
}
