using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bigarr.Api.Infrastructure.RadarrImport;

/// <summary>Where an existing Radarr answers: its URL (with any URL base) and its API key.</summary>
public sealed record RadarrConnection(string Url, string ApiKey)
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

// The subset of Radarr v3 resources the import reads. Names follow Radarr's JSON; unknown fields are ignored.

public sealed record RadarrSystemStatus(string? AppName, string? Version, string? InstanceName);

/// <summary>A custom format's score in a profile; Radarr lists every format, scored 0 when unused.</summary>
public sealed record RadarrFormatItem(int Format, string? Name, int Score);

/// <summary>A quality profile. The format scores are absent on a Radarr older than v4, <c>MinUpgradeFormatScore</c> before v5.</summary>
public sealed record RadarrProfile(int Id, string Name, List<RadarrFormatItem>? FormatItems = null, int? MinFormatScore = null, int? CutoffFormatScore = null, int? MinUpgradeFormatScore = null);

public sealed record RadarrRootFolder(int Id, string Path);

public sealed record RadarrTag(int Id, string Label);

public sealed record RadarrImage(string? CoverType, string? Url, string? RemoteUrl);

public sealed record RadarrRatingValue(int Votes, double Value);

public sealed record RadarrRatings(RadarrRatingValue? Tmdb, RadarrRatingValue? Imdb);

public sealed record RadarrAlternateTitle(string? Title, string? SourceType);

public sealed record RadarrLanguageRef(int Id, string? Name);

public sealed record RadarrCollectionRef(string? Title, int TmdbId);

public sealed record RadarrQualityDefinition(int Id, string? Name, string? Source, int Resolution, string? Modifier);

public sealed record RadarrRevision(int Version, int Real, bool IsRepack);

public sealed record RadarrQuality(RadarrQualityDefinition? Quality, RadarrRevision? Revision);

public sealed record RadarrMovieFile(
    int Id,
    string? RelativePath,
    string? Path,
    long Size,
    DateTime? DateAdded,
    string? SceneName,
    string? ReleaseGroup,
    string? Edition,
    string? OriginalFilePath,
    int IndexerFlags,
    RadarrQuality? Quality,
    List<RadarrLanguageRef>? Languages);

public sealed record RadarrMovie(
    int Id,
    string Title,
    string? OriginalTitle,
    string? SortTitle,
    int Year,
    int TmdbId,
    string? ImdbId,
    string? Overview,
    string? Status,
    int Runtime,
    string? Certification,
    bool Monitored,
    int QualityProfileId,
    string? MinimumAvailability,
    string? Path,
    string? RootFolderPath,
    string? FolderName,
    bool HasFile,
    DateTime? Added,
    DateTime? InCinemas,
    DateTime? PhysicalRelease,
    DateTime? DigitalRelease,
    List<int>? Tags,
    List<string>? Genres,
    List<RadarrImage>? Images,
    RadarrRatings? Ratings,
    List<RadarrAlternateTitle>? AlternateTitles,
    RadarrLanguageRef? OriginalLanguage,
    RadarrCollectionRef? Collection,
    RadarrMovieFile? MovieFile);

public sealed record RadarrExclusion(int Id, int TmdbId, string? MovieTitle, int? MovieYear);

public sealed record RadarrCustomFormatRef(int Id, string? Name);

/// <summary>One line of Radarr's history. <paramref name="EventType"/> is Radarr's camelCase name (<c>grabbed</c>, <c>downloadFolderImported</c>…).</summary>
public sealed record RadarrHistoryItem(
    int Id,
    int MovieId,
    string? SourceTitle,
    RadarrQuality? Quality,
    List<RadarrCustomFormatRef>? CustomFormats,
    DateTime Date,
    string? DownloadId,
    string? EventType,
    Dictionary<string, string?>? Data);

public sealed record RadarrHistoryPage(int Page, int PageSize, int TotalRecords, List<RadarrHistoryItem>? Records);

/// <summary>A release Radarr blocklisted. Radarr's resource carries no release guid and no info hash: only the title identifies it.</summary>
public sealed record RadarrBlocklistItem(int Id, int MovieId, string? SourceTitle, DateTime Date, string? Indexer, string? Message);

public sealed record RadarrBlocklistPage(int Page, int PageSize, int TotalRecords, List<RadarrBlocklistItem>? Records);

/// <summary>Raised when Radarr cannot be reached or refuses the key; the message is meant for the screen.</summary>
public sealed class RadarrUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reads an existing Radarr over its v3 API, to migrate its library into Bigarr (M8).</summary>
public sealed class RadarrClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "bigarr-radarr-import";

    public const int HistoryPageSize = 500;

    public const int BlocklistPageSize = 500;

    /// <summary>A bound on the pages read, so a Radarr that keeps answering full pages cannot hold the import forever.</summary>
    private const int MaxHistoryPages = 2000;

    private const int MaxBlocklistPages = 2000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<RadarrSystemStatus> StatusAsync(RadarrConnection connection, CancellationToken ct) =>
        GetAsync<RadarrSystemStatus>(connection, "/system/status", ct);

    public Task<List<RadarrProfile>> ProfilesAsync(RadarrConnection connection, CancellationToken ct) =>
        GetAsync<List<RadarrProfile>>(connection, "/qualityprofile", ct);

    public Task<List<RadarrRootFolder>> RootFoldersAsync(RadarrConnection connection, CancellationToken ct) =>
        GetAsync<List<RadarrRootFolder>>(connection, "/rootfolder", ct);

    public Task<List<RadarrTag>> TagsAsync(RadarrConnection connection, CancellationToken ct) =>
        GetAsync<List<RadarrTag>>(connection, "/tag", ct);

    public Task<List<RadarrMovie>> MoviesAsync(RadarrConnection connection, CancellationToken ct) =>
        GetAsync<List<RadarrMovie>>(connection, "/movie", ct);

    /// <summary>Import exclusions; an older Radarr without the route answers with an empty list rather than a failure.</summary>
    public async Task<List<RadarrExclusion>> ExclusionsAsync(RadarrConnection connection, CancellationToken ct)
    {
        try
        {
            return await GetAsync<List<RadarrExclusion>>(connection, "/exclusions", ct);
        }
        catch (RadarrUnavailableException ex) when (ex.Message.Contains("404", StringComparison.Ordinal))
        {
            return [];
        }
    }

    /// <summary>Custom formats as Radarr's API returns them (a JSON array, <c>fields</c> as <c>{name, value}</c> pairs); an empty array when the route is missing.</summary>
    public async Task<JsonElement> CustomFormatsAsync(RadarrConnection connection, CancellationToken ct)
    {
        try
        {
            var formats = await GetAsync<JsonElement>(connection, "/customformat", ct);
            return formats.ValueKind == JsonValueKind.Array ? formats : EmptyArray;
        }
        catch (RadarrUnavailableException ex) when (ex.Message.Contains("404", StringComparison.Ordinal))
        {
            return EmptyArray;
        }
    }

    private static JsonElement EmptyArray => JsonDocument.Parse("[]").RootElement.Clone();

    /// <summary>How many lines Radarr's history holds; 0 when the route is missing.</summary>
    public async Task<int> HistoryCountAsync(RadarrConnection connection, CancellationToken ct)
    {
        try
        {
            return (await GetAsync<RadarrHistoryPage>(connection, "/history?page=1&pageSize=1", ct)).TotalRecords;
        }
        catch (RadarrUnavailableException ex) when (ex.Message.Contains("404", StringComparison.Ordinal))
        {
            return 0;
        }
    }

    /// <summary>Radarr's whole history, oldest first, read page by page; an empty list when the route is missing.</summary>
    public async Task<List<RadarrHistoryItem>> HistoryAsync(RadarrConnection connection, CancellationToken ct)
    {
        var all = new List<RadarrHistoryItem>();
        for (var page = 1; page <= MaxHistoryPages; page++)
        {
            RadarrHistoryPage result;
            try
            {
                result = await GetAsync<RadarrHistoryPage>(connection, $"/history?page={page}&pageSize={HistoryPageSize}&sortKey=date&sortDirection=ascending", ct);
            }
            catch (RadarrUnavailableException ex) when (page == 1 && ex.Message.Contains("404", StringComparison.Ordinal))
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

    /// <summary>How many releases Radarr's blocklist holds; 0 when the route is missing.</summary>
    public async Task<int> BlocklistCountAsync(RadarrConnection connection, CancellationToken ct)
    {
        try
        {
            return (await GetAsync<RadarrBlocklistPage>(connection, "/blocklist?page=1&pageSize=1", ct)).TotalRecords;
        }
        catch (RadarrUnavailableException ex) when (ex.Message.Contains("404", StringComparison.Ordinal))
        {
            return 0;
        }
    }

    /// <summary>Radarr's whole blocklist, oldest first, read page by page; an empty list when the route is missing.</summary>
    public async Task<List<RadarrBlocklistItem>> BlocklistAsync(RadarrConnection connection, CancellationToken ct)
    {
        var all = new List<RadarrBlocklistItem>();
        for (var page = 1; page <= MaxBlocklistPages; page++)
        {
            RadarrBlocklistPage result;
            try
            {
                result = await GetAsync<RadarrBlocklistPage>(connection, $"/blocklist?page={page}&pageSize={BlocklistPageSize}&sortKey=date&sortDirection=ascending", ct);
            }
            catch (RadarrUnavailableException ex) when (page == 1 && ex.Message.Contains("404", StringComparison.Ordinal))
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

    private async Task<T> GetAsync<T>(RadarrConnection connection, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new RadarrUnavailableException("The Radarr URL must be absolute and start with http:// or https://.");
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
            throw new RadarrUnavailableException(ex is TaskCanceledException ? "Radarr did not answer before the timeout." : $"Radarr could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new RadarrUnavailableException("Radarr refused the API key (HTTP 401).");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new RadarrUnavailableException($"Radarr answered HTTP {(int)response.StatusCode} on {path}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new RadarrUnavailableException($"Radarr answered an empty body on {path}.");
            }
            catch (JsonException ex)
            {
                throw new RadarrUnavailableException($"Radarr's answer on {path} is not the JSON of a Radarr v3: {ex.Message}", ex);
            }
        }
    }
}
