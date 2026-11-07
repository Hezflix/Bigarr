using System.Globalization;
using System.Text.Json;
using Bigarr.Api.Tests.Hosting;
using Microsoft.AspNetCore.WebUtilities;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// A Radarr whose library and history the test edits between two passes of the follow: movies with their file, history
/// lines answered by <c>/history/since</c>, a <c>/parse</c> the test scripts. The key is checked like Radarr does; it can
/// also be taken down.
/// </summary>
internal sealed class FakeRadarr : IAsyncDisposable
{
    public const string Key = "follow-secret-key-0123456789";

    public const string RootPath = "/radarr-movies";

    private readonly FakeHttpServer _server;

    private FakeRadarr(FakeHttpServer server)
    {
        _server = server;
        _server.Responder = Answer;
    }

    public sealed class Film
    {
        public int Id { get; init; }

        public required string Title { get; init; }

        public int Year { get; init; }

        public int TmdbId { get; init; }

        public string Folder => $"{Title} ({Year})";

        public bool Monitored { get; set; } = true;

        public int ProfileId { get; set; } = 1;

        /// <summary>When the film was added to Radarr: the follow searches on add only what was added since it started.</summary>
        public DateTime Added { get; init; } = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public FilmFile? File { get; set; }

        /// <summary>TMDB knows only its cinema release, as for many older or foreign films: no digital or physical date.</summary>
        public bool CinemaOnly { get; init; }
    }

    public sealed record FilmFile(int Id, string RelativePath, int QualityId, string QualityName, long Size, string ReleaseGroup, int CustomFormatScore = 0);

    public sealed record Line(int Id, int MovieId, string EventType, DateTime Date, string SourceTitle, int QualityId, string QualityName, string? DownloadId, Dictionary<string, string> Data);

    public List<Film> Films { get; } = [];

    public List<Line> History { get; } = [];

    /// <summary>Title → (Radarr film id it maps to or 0, quality name, custom formats, score).</summary>
    public Func<string, (int MovieId, string Quality, string[] Formats, int Score)>? Parse { get; set; }

    public bool Down { get; set; }

    /// <summary>Answer gzipped to a client that accepts it.</summary>
    public bool Gzip { get; set; }

    /// <summary>The quality profiles <c>/qualityprofile</c> returns, in Radarr's shape; two bare ones by default.</summary>
    public List<object> Profiles { get; } = [new { id = 1, name = "HD-1080p" }, new { id = 2, name = "Ultra-HD" }];

    public string Url => _server.Url;

    public IEnumerable<FakeHttpServer.Received> Requests => _server.Requests;

    public long BytesSent => _server.BytesSent;

    public static async Task<FakeRadarr> StartAsync() => new(await FakeHttpServer.StartAsync());

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private FakeHttpServer.Answer? Answer(FakeHttpServer.Received request)
    {
        if (Down)
        {
            return new FakeHttpServer.Answer(503);
        }

        if (!request.Headers.TryGetValue("X-Api-Key", out var key) || key != Key)
        {
            return new FakeHttpServer.Answer(401, """{"message":"Unauthorized"}""");
        }

        if (request.Method != "GET")
        {
            return new FakeHttpServer.Answer(405);
        }

        var query = QueryHelpers.ParseQuery(request.Query);
        if (request.Path.StartsWith("/api/v3/movie/", StringComparison.Ordinal) && int.TryParse(request.Path["/api/v3/movie/".Length..], out var filmId))
        {
            return Films.FirstOrDefault(f => f.Id == filmId) is { } one ? Json(MovieJson(one)) : new FakeHttpServer.Answer(404);
        }

        return request.Path switch
        {
            "/api/v3/system/status" => Json(new { appName = "Radarr", version = "5.14.0.9383" }),
            "/api/v3/qualityprofile" => Json(Profiles),
            "/api/v3/tag" => Json(Array.Empty<object>()),
            "/api/v3/movie" => Json(Films.Select(MovieJson).ToArray()),
            "/api/v3/history/since" => Json(History
                .Where(h => h.Date >= DateTime.Parse(query["date"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal))
                .Select(LineJson)
                .ToArray()),
            "/api/v3/blocklist" => Json(new { page = 1, pageSize = 500, totalRecords = 0, records = Array.Empty<object>() }),
            // What the one-time import reads on top.
            "/api/v3/rootfolder" => Json(new object[] { new { id = 1, path = RootPath } }),
            "/api/v3/customformat" => Json(Array.Empty<object>()),
            "/api/v3/exclusions" => Json(Array.Empty<object>()),
            "/api/v3/history" => Json(new { page = 1, pageSize = 500, totalRecords = History.Count, records = History.OrderBy(h => h.Date).Select(LineJson).ToArray() }),
            "/api/v3/parse" => ParseJson(query["title"].ToString()),
            _ => null,
        };
    }

    private FakeHttpServer.Answer ParseJson(string title)
    {
        var (movieId, quality, formats, score) = Parse?.Invoke(title) ?? (0, "Unknown", [], 0);
        var film = Films.FirstOrDefault(f => f.Id == movieId);
        return Json(new
        {
            title,
            parsedMovieInfo = new { quality = new { quality = new { id = 0, name = quality }, revision = new { version = 1, real = 0, isRepack = false } }, releaseGroup = "GRP" },
            movie = film is null ? null : MovieJson(film),
            customFormats = formats.Select((name, i) => new { id = i + 1, name }).ToArray(),
            customFormatScore = score,
        });
    }

    private static object LineJson(Line line) => new
    {
        id = line.Id,
        movieId = line.MovieId,
        sourceTitle = line.SourceTitle,
        languages = new object[] { new { id = 1, name = "English" } },
        quality = new { quality = new { id = line.QualityId, name = line.QualityName }, revision = new { version = 1, real = 0, isRepack = false } },
        customFormats = Array.Empty<object>(),
        customFormatScore = 0,
        date = line.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        downloadId = line.DownloadId,
        eventType = line.EventType,
        data = line.Data,
    };

    private static object MovieJson(Film film) => new
    {
        id = film.Id,
        title = film.Title,
        originalTitle = film.Title,
        sortTitle = film.Title.ToLowerInvariant(),
        year = film.Year,
        tmdbId = film.TmdbId,
        overview = $"{film.Title}, as Radarr knows it.",
        status = "released",
        runtime = 120,
        monitored = film.Monitored,
        qualityProfileId = film.ProfileId,
        minimumAvailability = "released",
        path = $"{RootPath}/{film.Folder}",
        rootFolderPath = RootPath,
        hasFile = film.File is not null,
        added = film.Added.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        inCinemas = $"{film.Year}-06-01T00:00:00Z",
        physicalRelease = film.CinemaOnly ? null : $"{film.Year}-12-01T00:00:00Z",
        tags = Array.Empty<int>(),
        genres = new[] { "Drama" },
        images = Array.Empty<object>(),
        originalLanguage = new { id = 1, name = "English" },
        movieFile = film.File is not { } file ? null : new
        {
            id = file.Id,
            relativePath = file.RelativePath,
            size = file.Size,
            dateAdded = "2024-02-02T00:00:00Z",
            sceneName = Path.GetFileNameWithoutExtension(file.RelativePath),
            releaseGroup = file.ReleaseGroup,
            indexerFlags = 0,
            quality = new { quality = new { id = file.QualityId, name = file.QualityName, source = "bluray", resolution = 1080, modifier = "none" }, revision = new { version = 1, real = 0, isRepack = false } },
            languages = new object[] { new { id = 1, name = "English" } },
            customFormatScore = file.CustomFormatScore,
        },
    };

    private FakeHttpServer.Answer Json(object body) => new(200, JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Gzip: Gzip);
}
