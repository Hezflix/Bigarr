using System.Globalization;
using System.Text.Json;
using Bigarr.Api.Tests.Hosting;
using Microsoft.AspNetCore.WebUtilities;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// A Sonarr v4 whose library and history the test edits between two passes: series with their episodes and episode files
/// (multi-episode files, anime absolute numbers, specials), per-episode history lines answered by <c>/history</c> and
/// <c>/history/since</c> (with the episode when asked), a blocklist, exclusions and a <c>/parse</c> the test scripts. The key
/// is checked like Sonarr does; it can be taken down. Shapes follow Sonarr v4's API (camelCase, quality blocks, statistics).
/// </summary>
internal sealed class FakeSonarr : IAsyncDisposable
{
    public const string Key = "sonarr-follow-secret-0123456789";

    public const string RootPath = "/sonarr-tv";

    public const string AnimeRootPath = "/sonarr-anime";

    private readonly FakeHttpServer _server;

    private FakeSonarr(FakeHttpServer server)
    {
        _server = server;
        _server.Responder = Answer;
    }

    public sealed class Show
    {
        public int Id { get; init; }

        public required string Title { get; init; }

        public int Year { get; init; }

        public int TvdbId { get; init; }

        public string Type { get; init; } = "standard";

        public string Root { get; init; } = RootPath;

        public string Folder => $"{Title} ({Year})";

        public bool Monitored { get; set; } = true;

        public int ProfileId { get; set; } = 1;

        public bool SeasonFolder { get; set; } = true;

        public string MonitorNewItems { get; set; } = "all";

        public List<int> Tags { get; set; } = [];

        public DateTime Added { get; init; } = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Dictionary<int, bool> Seasons { get; } = [];

        public List<Ep> Episodes { get; } = [];

        public List<EpFile> Files { get; } = [];
    }

    public sealed class Ep
    {
        public int Id { get; init; }

        public int Season { get; init; }

        public int Number { get; init; }

        public int? Absolute { get; init; }

        public required string Title { get; init; }

        public DateOnly AirDate { get; init; } = new(2010, 1, 1);

        public bool Monitored { get; set; } = true;

        public int FileId { get; set; }
    }

    /// <param name="QualityId">Sonarr's quality id (Sonarr's numbering: 20 is Bluray-1080p Remux).</param>
    public sealed record EpFile(int Id, int Season, string RelativePath, int QualityId, string QualityName, long Size, string ReleaseGroup, string[]? Languages = null);

    public sealed record Line(int Id, int SeriesId, int EpisodeId, string EventType, DateTime Date, string SourceTitle, int QualityId, string QualityName, string? DownloadId, Dictionary<string, string> Data);

    public sealed record Blocked(int Id, int SeriesId, int[] EpisodeIds, string SourceTitle, DateTime Date, string Indexer, string Message);

    public List<Show> Series { get; } = [];

    public List<Line> History { get; } = [];

    public List<Blocked> Blocklist { get; } = [];

    public List<(int TvdbId, string Title)> Exclusions { get; } = [];

    /// <summary>What <c>/config/naming</c> answers; null answers 404 (an old Sonarr).</summary>
    public object? Naming { get; set; }

    /// <summary>Title → (Sonarr series id or 0, Sonarr episode ids, quality name, custom formats, score).</summary>
    public Func<string, (int SeriesId, int[] EpisodeIds, string Quality, string[] Formats, int Score)>? Parse { get; set; }

    public bool Down { get; set; }

    /// <summary>The quality profiles <c>/qualityprofile</c> returns, in Sonarr's shape; two bare ones by default.</summary>
    public List<object> Profiles { get; } = [new { id = 1, name = "HD-1080p" }, new { id = 2, name = "Ultra-HD" }];

    public List<(int Id, string Label)> Tags { get; } = [];

    /// <summary>The raw JSON <c>/customformat</c> returns (Sonarr v4's shape); none by default.</summary>
    public string CustomFormatsJson { get; set; } = "[]";

    /// <summary>Sonarr's quality sizes, <c>/qualitydefinition</c>: (Sonarr's quality id, name, min, preferred, max).</summary>
    public List<(int QualityId, string Name, double? Min, double? Preferred, double? Max)> QualityDefinitions { get; } =
    [
        (3, "WEBDL-1080p", 5, 95, 140),
        (20, "Bluray-1080p Remux", 40, 95, null),
    ];

    public string Url => _server.Url;

    public IEnumerable<FakeHttpServer.Received> Requests => _server.Requests;

    public static async Task<FakeSonarr> StartAsync() => new(await FakeHttpServer.StartAsync());

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
        if (request.Path.StartsWith("/api/v3/series/", StringComparison.Ordinal) && int.TryParse(request.Path["/api/v3/series/".Length..], out var seriesId))
        {
            return Series.FirstOrDefault(s => s.Id == seriesId) is { } one ? Json(SeriesJson(one)) : new FakeHttpServer.Answer(404);
        }

        int SeriesParam() => int.Parse(query.GetValueOrDefault("seriesId").ToString(), CultureInfo.InvariantCulture);
        var includeEpisode = string.Equals(query.GetValueOrDefault("includeEpisode").ToString(), "true", StringComparison.OrdinalIgnoreCase);
        return request.Path switch
        {
            "/api/v3/system/status" => Json(new { appName = "Sonarr", instanceName = "Sonarr", version = "4.0.11.2680" }),
            "/api/v3/qualityprofile" => Json(Profiles),
            "/api/v3/languageprofile" => new FakeHttpServer.Answer(404),
            "/api/v3/customformat" => new FakeHttpServer.Answer(200, CustomFormatsJson),
            "/api/v3/qualitydefinition" => Json(QualityDefinitions.Select((d, i) => new { id = i + 1, quality = new { id = d.QualityId, name = d.Name, source = "web", resolution = 1080 }, title = d.Name, weight = i + 1, minSize = d.Min, maxSize = d.Max, preferredSize = d.Preferred }).ToArray()),
            "/api/v3/tag" => Json(Tags.Select(t => new { id = t.Id, label = t.Label }).ToArray()),
            "/api/v3/rootfolder" => Json(new object[] { new { id = 1, path = RootPath, freeSpace = 1L << 40 }, new { id = 2, path = AnimeRootPath, freeSpace = 1L << 40 } }),
            "/api/v3/series" => Json(Series.Select(SeriesJson).ToArray()),
            "/api/v3/episode" => Json(ShowOf(SeriesParam())?.Episodes.Select(e => EpisodeJson(ShowOf(SeriesParam())!, e)).ToArray() ?? []),
            "/api/v3/episodefile" => Json(ShowOf(SeriesParam())?.Files.Select(f => FileJson(ShowOf(SeriesParam())!, f)).ToArray() ?? []),
            "/api/v3/importlistexclusion" => Json(Exclusions.Select((e, i) => new { id = i + 1, tvdbId = e.TvdbId, title = e.Title }).ToArray()),
            "/api/v3/history/since" => Json(History
                .Where(h => h.Date >= DateTime.Parse(query.GetValueOrDefault("date").ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal))
                .Select(h => LineJson(h, includeEpisode))
                .ToArray()),
            "/api/v3/history" => Json(Page(History.OrderBy(h => h.Date).Select(h => LineJson(h, includeEpisode)).ToList(), query)),
            "/api/v3/blocklist" => Json(Page(Blocklist.OrderBy(b => b.Date).Select(BlockedJson).ToList(), query)),
            "/api/v3/config/naming" => Naming is null ? new FakeHttpServer.Answer(404) : Json(Naming),
            "/api/v3/parse" => ParseJson(query.GetValueOrDefault("title").ToString()),
            _ => null,
        };
    }

    private Show? ShowOf(int id) => Series.FirstOrDefault(s => s.Id == id);

    private static object Page(List<object> records, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query)
    {
        var page = int.TryParse(query.GetValueOrDefault("page").ToString(), out var p) ? p : 1;
        var size = int.TryParse(query.GetValueOrDefault("pageSize").ToString(), out var s) ? s : 10;
        return new { page, pageSize = size, sortKey = "date", sortDirection = "ascending", totalRecords = records.Count, records = records.Skip((page - 1) * size).Take(size).ToArray() };
    }

    private FakeHttpServer.Answer ParseJson(string title)
    {
        var (seriesId, episodeIds, quality, formats, score) = Parse?.Invoke(title) ?? (0, [], "Unknown", [], 0);
        var show = ShowOf(seriesId);
        return Json(new
        {
            title,
            parsedEpisodeInfo = new
            {
                releaseTitle = title,
                seriesTitle = show?.Title ?? "Unknown",
                quality = Quality(0, quality),
                seasonNumber = 1,
                episodeNumbers = Array.Empty<int>(),
                absoluteEpisodeNumbers = Array.Empty<int>(),
                fullSeason = false,
                releaseGroup = "GRP",
                languages = new object[] { new { id = 1, name = "English" } },
            },
            series = show is null ? null : SeriesJson(show),
            episodes = show is null ? [] : show.Episodes.Where(e => episodeIds.Contains(e.Id)).Select(e => EpisodeJson(show, e)).ToArray(),
            languages = new object[] { new { id = 1, name = "English" } },
            customFormats = formats.Select((name, i) => new { id = i + 1, name }).ToArray(),
            customFormatScore = score,
        });
    }

    private object LineJson(Line line, bool includeEpisode)
    {
        var show = ShowOf(line.SeriesId);
        var episode = show?.Episodes.FirstOrDefault(e => e.Id == line.EpisodeId);
        return new
        {
            id = line.Id,
            episodeId = line.EpisodeId,
            seriesId = line.SeriesId,
            sourceTitle = line.SourceTitle,
            languages = new object[] { new { id = 1, name = "English" } },
            quality = Quality(line.QualityId, line.QualityName),
            customFormats = Array.Empty<object>(),
            customFormatScore = 0,
            qualityCutoffNotMet = false,
            date = line.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            downloadId = line.DownloadId,
            eventType = line.EventType,
            data = line.Data,
            episode = includeEpisode && show is not null && episode is not null ? EpisodeJson(show, episode) : null,
        };
    }

    private static object BlockedJson(Blocked b) => new
    {
        id = b.Id,
        seriesId = b.SeriesId,
        episodeIds = b.EpisodeIds,
        sourceTitle = b.SourceTitle,
        languages = new object[] { new { id = 1, name = "English" } },
        quality = Quality(3, "WEBDL-1080p"),
        customFormats = Array.Empty<object>(),
        date = b.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        protocol = "torrent",
        indexer = b.Indexer,
        message = b.Message,
    };

    private static object Quality(int id, string name) => new
    {
        quality = new { id, name, source = name.Contains("Bluray", StringComparison.OrdinalIgnoreCase) ? "bluray" : "web", resolution = name.Contains("2160", StringComparison.Ordinal) ? 2160 : name.Contains("720", StringComparison.Ordinal) ? 720 : 1080 },
        revision = new { version = 1, real = 0, isRepack = false },
    };

    private static object SeriesJson(Show show) => new
    {
        title = show.Title,
        alternateTitles = Array.Empty<object>(),
        sortTitle = show.Title.ToLowerInvariant(),
        status = "ended",
        ended = true,
        overview = $"{show.Title}, as Sonarr knows it.",
        network = "AMC",
        airTime = "21:00",
        images = new object[] { new { coverType = "poster", url = $"/MediaCover/{show.Id}/poster.jpg", remoteUrl = $"https://artworks.thetvdb.invalid/{show.TvdbId}/poster.jpg" } },
        originalLanguage = new { id = show.Type == "anime" ? 8 : 1, name = show.Type == "anime" ? "Japanese" : "English" },
        seasons = show.Seasons.OrderBy(s => s.Key).Select(s => new
        {
            seasonNumber = s.Key,
            monitored = s.Value,
            statistics = new
            {
                episodeFileCount = show.Files.Count(f => f.Season == s.Key),
                episodeCount = show.Episodes.Count(e => e.Season == s.Key && (e.Monitored || e.FileId > 0)),
                totalEpisodeCount = show.Episodes.Count(e => e.Season == s.Key),
                sizeOnDisk = show.Files.Where(f => f.Season == s.Key).Sum(f => f.Size),
                percentOfEpisodes = 0.0,
            },
        }).ToArray(),
        year = show.Year,
        path = $"{show.Root}/{show.Folder}",
        qualityProfileId = show.ProfileId,
        seasonFolder = show.SeasonFolder,
        monitored = show.Monitored,
        monitorNewItems = show.MonitorNewItems,
        useSceneNumbering = false,
        runtime = show.Type == "anime" ? 24 : 47,
        tvdbId = show.TvdbId,
        tvRageId = 0,
        tvMazeId = 0,
        tmdbId = 0,
        firstAired = $"{show.Year}-01-20T00:00:00Z",
        lastAired = $"{show.Year + 1}-09-29T00:00:00Z",
        seriesType = show.Type,
        cleanTitle = show.Title.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal),
        imdbId = string.Empty,
        titleSlug = show.Title.ToLowerInvariant().Replace(' ', '-'),
        rootFolderPath = show.Root + "/",
        certification = "TV-MA",
        genres = new[] { show.Type == "anime" ? "Anime" : "Drama" },
        tags = show.Tags,
        added = show.Added.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        ratings = new { votes = 100, value = 9.1 },
        statistics = new
        {
            seasonCount = show.Seasons.Keys.Count(k => k > 0),
            episodeFileCount = show.Files.Count,
            episodeCount = show.Episodes.Count(e => e.Monitored || e.FileId > 0),
            totalEpisodeCount = show.Episodes.Count,
            sizeOnDisk = show.Files.Sum(f => f.Size),
            percentOfEpisodes = 0.0,
        },
        languageProfileId = 1,
        id = show.Id,
    };

    private static object EpisodeJson(Show show, Ep episode) => new
    {
        seriesId = show.Id,
        tvdbId = show.TvdbId * 100 + episode.Season * 1000 + episode.Number,
        episodeFileId = episode.FileId,
        seasonNumber = episode.Season,
        episodeNumber = episode.Number,
        title = episode.Title,
        airDate = episode.AirDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        airDateUtc = episode.AirDate.ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        runtime = show.Type == "anime" ? 24 : 47,
        overview = $"{episode.Title}.",
        hasFile = episode.FileId > 0,
        monitored = episode.Monitored,
        absoluteEpisodeNumber = episode.Absolute,
        unverifiedSceneNumbering = false,
        id = episode.Id,
    };

    private static object FileJson(Show show, EpFile file) => new
    {
        seriesId = show.Id,
        seasonNumber = file.Season,
        relativePath = file.RelativePath,
        path = $"{show.Root}/{show.Folder}/{file.RelativePath}",
        size = file.Size,
        dateAdded = "2024-02-02T00:00:00Z",
        sceneName = Path.GetFileNameWithoutExtension(file.RelativePath),
        releaseGroup = file.ReleaseGroup,
        languages = (file.Languages ?? ["English"]).Select((name, i) => new { id = i + 1, name }).ToArray(),
        quality = Quality(file.QualityId, file.QualityName),
        customFormats = Array.Empty<object>(),
        customFormatScore = 0,
        indexerFlags = 0,
        releaseType = "singleEpisode",
        qualityCutoffNotMet = false,
        id = file.Id,
    };

    private static FakeHttpServer.Answer Json(object body) => new(200, JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    /// <summary>Writes the file a Sonarr episode file names under the Bigarr root the test mapped, so the import finds it.</summary>
    public static string Write(string library, Show show, EpFile file)
    {
        var full = Path.Combine(library, show.Folder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[file.Size]);
        return full;
    }

    /// <summary>A public show in Sonarr's numbering: Breaking Bad, season 1 (7 episodes) and 2 (13), S01E01E02 one file.</summary>
    public static Show BreakingBad(int id = 1)
    {
        var show = new Show { Id = id, Title = "Breaking Bad", Year = 2008, TvdbId = 81189 };
        show.Seasons[0] = false;
        show.Seasons[1] = true;
        show.Seasons[2] = true;
        var next = id * 1000;
        show.Episodes.Add(new Ep { Id = ++next, Season = 0, Number = 1, Title = "Good Cop Bad Cop", AirDate = new(2009, 2, 17), Monitored = false });
        for (var n = 1; n <= 7; n++)
        {
            show.Episodes.Add(new Ep { Id = ++next, Season = 1, Number = n, Title = $"Season 1 episode {n}", AirDate = new DateOnly(2008, 1, 20).AddDays(7 * (n - 1)) });
        }

        for (var n = 1; n <= 13; n++)
        {
            show.Episodes.Add(new Ep { Id = ++next, Season = 2, Number = n, Title = $"Season 2 episode {n}", AirDate = new DateOnly(2009, 3, 8).AddDays(7 * (n - 1)), Monitored = n != 13 });
        }

        return show;
    }

    /// <summary>An anime in Sonarr's numbering: Frieren, 28 episodes with absolute numbers 1–28, and one special.</summary>
    public static Show Frieren(int id = 2)
    {
        var show = new Show { Id = id, Title = "Frieren Beyond Journeys End", Year = 2023, TvdbId = 424536, Type = "anime", Root = AnimeRootPath, ProfileId = 1 };
        show.Seasons[0] = true;
        show.Seasons[1] = true;
        var next = id * 1000;
        show.Episodes.Add(new Ep { Id = ++next, Season = 0, Number = 1, Title = "Mini Anime 1", AirDate = new(2024, 1, 5) });
        for (var n = 1; n <= 28; n++)
        {
            show.Episodes.Add(new Ep { Id = ++next, Season = 1, Number = n, Absolute = n, Title = $"Episode {n}", AirDate = new DateOnly(2023, 9, 29).AddDays(7 * (n - 1)) });
        }

        return show;
    }

    public Ep Episode(Show show, int season, int number) => show.Episodes.Single(e => e.Season == season && e.Number == number);

    /// <summary>Adds a file to the show and points the episodes it holds at it.</summary>
    public static EpFile AddFile(Show show, EpFile file, params (int Season, int Number)[] episodes)
    {
        show.Files.Add(file);
        foreach (var (season, number) in episodes)
        {
            show.Episodes.Single(e => e.Season == season && e.Number == number).FileId = file.Id;
        }

        return file;
    }
}
