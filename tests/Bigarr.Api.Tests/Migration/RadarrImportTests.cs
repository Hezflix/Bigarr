using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Domain.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// Moving in from Radarr (M8): a standard Radarr becomes <c>1080p</c> versions with their files, flags, tags and
/// exclusions, history, custom formats and profile scores; a 4K Radarr imported afterwards adds <c>4k</c> versions to the same films; a second pass updates instead
/// of duplicating. Radarr is played by a fake HTTP server; the library folder is a temp directory.
/// </summary>
public sealed class RadarrImportTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private const string Key = "radarr-secret";
    private const int PulpFiction = 680;
    private const int ForrestGump = 13;
    private const int Heat = 949;
    private const int FightClub = 550;

    [Fact]
    public async Task A_radarr_library_becomes_versions_and_a_4k_radarr_adds_second_versions_to_the_same_films()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var (hd, uhd, _) = await CompatTestHelpers.SetupAsync(session);
        var hdRoot = await AddRootAsync(session, Directory.CreateTempSubdirectory("bigarr-import-hd-").FullName);
        var uhdRoot = await AddRootAsync(session, Directory.CreateTempSubdirectory("bigarr-import-4k-").FullName);

        // Radarr's /radarr-movies is Bigarr's hdRoot: only Pulp Fiction's file is really there.
        var pulpFolder = Directory.CreateDirectory(Path.Combine(hdRoot.Path, "Pulp Fiction (1994)")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(pulpFolder, "Pulp.Fiction.1994.1080p.BluRay.x264-GRP.mkv"), new byte[2048]);

        await using var radarr = await FakeHttpServer.StartAsync();
        radarr.Responder = r => Answer(r, "/radarr-movies", StandardMovies(heatMonitored: false));

        // Preview: what the mapping screen shows.
        var previewResponse = await session.PostAsJsonAsync("/api/v1/migration/radarr/preview", new { url = radarr.Url, apiKey = Key });
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.ReadJsonAsync();
        Assert.Equal("1080p", preview.GetProperty("suggestedVersionName").GetString());
        Assert.Equal(3, preview.GetProperty("movies").GetInt32());
        Assert.Equal(2, preview.GetProperty("withFile").GetInt32());
        Assert.Equal(0, preview.GetProperty("alreadyInLibrary").GetInt32());
        Assert.Equal(1, preview.GetProperty("exclusions").GetInt32());
        Assert.Equal(History().Length, preview.GetProperty("historyRecords").GetInt32());
        Assert.Equal(2, preview.GetProperty("customFormats").GetInt32());
        var profiles = preview.GetProperty("profiles").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p);
        Assert.Equal(hd, profiles["HD-1080p"].GetProperty("suggestedProfileId").GetInt32());
        Assert.Equal(uhd, profiles["Ultra-HD"].GetProperty("suggestedProfileId").GetInt32());
        var root = preview.GetProperty("rootFolders").EnumerateArray().Single();
        Assert.Equal("/radarr-movies", root.GetProperty("path").GetString());
        Assert.False(root.GetProperty("reachable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("suggestedRootFolderId").ValueKind);
        Assert.Equal("seerr", preview.GetProperty("tags").EnumerateArray().Single().GetProperty("label").GetString());

        var refused = await session.PostAsJsonAsync("/api/v1/migration/radarr/preview", new { url = radarr.Url, apiKey = "nope" });
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);

        // The standard Radarr lands as 1080p versions.
        var status = await ImportAsync(session, radarr.Url, "1080p", new Dictionary<string, int> { ["1"] = hd, ["2"] = uhd }, new Dictionary<string, int> { ["/radarr-movies"] = hdRoot.Id });
        Assert.Equal("completed", status.GetProperty("state").GetString());
        Assert.Equal(3, status.GetProperty("moviesAdded").GetInt32());
        Assert.Equal(3, status.GetProperty("versionsAdded").GetInt32());
        Assert.Equal(1, status.GetProperty("filesAdded").GetInt32());
        Assert.Equal(1, status.GetProperty("filesMissing").GetInt32());
        Assert.Equal(1, status.GetProperty("tagsAdded").GetInt32());
        Assert.Equal(1, status.GetProperty("exclusionsAdded").GetInt32());
        Assert.Equal(5, status.GetProperty("historyAdded").GetInt32());
        Assert.Equal(2, status.GetProperty("customFormatsAdded").GetInt32());
        Assert.Equal(0, status.GetProperty("customFormatsUpdated").GetInt32());
        Assert.Equal(2, status.GetProperty("profilesScored").GetInt32());
        Assert.Equal(0, status.GetProperty("failed").GetInt32());
        Assert.Equal(0, status.GetProperty("skipped").GetInt32());
        var gumpIssue = status.GetProperty("issues").EnumerateArray().Single(i => i.GetProperty("tmdbId").GetInt32() == ForrestGump);
        Assert.StartsWith("File not found", gumpIssue.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Number, gumpIssue.GetProperty("movieId").ValueKind);

        var movies = await MoviesAsync(session);
        var pulp = movies[PulpFiction];
        var pulpVersion = pulp.GetProperty("versions").EnumerateArray().Single();
        Assert.Equal("1080p", pulpVersion.GetProperty("name").GetString());
        Assert.True(pulpVersion.GetProperty("monitored").GetBoolean());
        Assert.True(pulpVersion.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Pulp Fiction (1994)", pulpVersion.GetProperty("folderName").GetString());
        Assert.Equal("Bluray-1080p", pulpVersion.GetProperty("file").GetProperty("quality").GetString());
        Assert.Equal(2048, pulpVersion.GetProperty("file").GetProperty("size").GetInt64());
        Assert.Equal("GRP", pulpVersion.GetProperty("file").GetProperty("releaseGroup").GetString());
        Assert.Single(pulpVersion.GetProperty("tags").EnumerateArray());
        Assert.Equal("Pulp Fiction", pulp.GetProperty("title").GetString());
        Assert.Equal(1994, pulp.GetProperty("year").GetInt32());
        Assert.Equal("en", pulp.GetProperty("originalLanguage").GetString());

        Assert.Equal(JsonValueKind.Null, movies[ForrestGump].GetProperty("versions").EnumerateArray().Single().GetProperty("file").ValueKind);
        var heat = movies[Heat].GetProperty("versions").EnumerateArray().Single();
        Assert.False(heat.GetProperty("monitored").GetBoolean());
        Assert.Equal("announced", heat.GetProperty("minimumAvailability").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.True(await db.MovieExclusions.AnyAsync(e => e.TmdbId == FightClub));
            Assert.True(await db.Tags.AnyAsync(t => t.Label == "seerr"));

            // Radarr's history of Pulp Fiction, in order: the second import follows an upgrade deletion, so it is an upgrade.
            var pulpHistory = await db.History.Where(h => h.TmdbId == PulpFiction).OrderBy(h => h.CreatedAt).ToListAsync();
            Assert.Equal([HistoryEventType.Grabbed, HistoryEventType.Imported, HistoryEventType.FileDeleted, HistoryEventType.Upgraded], pulpHistory.Select(h => h.EventType));
            Assert.All(pulpHistory, h => Assert.Equal("1080p", h.VersionName));
            Assert.All(pulpHistory, h => Assert.Equal("radarr", h.Data["importedFrom"]));
            var grabbed = pulpHistory[0];
            Assert.Equal("Pulp.Fiction.1994.720p.BluRay.x264-OLD", grabbed.SourceTitle);
            Assert.Equal("The Old School (Prowlarr)", grabbed.IndexerName);
            Assert.Equal("qBittorrent", grabbed.Data["downloadClient"]);
            Assert.Equal("ABCDEF", grabbed.Data["downloadId"]);
            Assert.Equal("Bluray-720p", grabbed.Data["quality"]);
            Assert.Equal(new DateTime(2024, 1, 2, 10, 0, 0), grabbed.CreatedAt);
            Assert.Equal("/radarr-movies/Pulp Fiction (1994)/Pulp.Fiction.1994.720p.BluRay.x264-OLD.mkv", pulpHistory[1].Data["path"]);
            Assert.Null(pulpHistory[1].MediaFileId);
            Assert.Equal("Upgrade", pulpHistory[2].Data["reason"]);
            var upgraded = pulpHistory[3];
            Assert.Equal("Bluray-720p", upgraded.Data["previousQuality"]);
            Assert.Equal("Bluray-1080p", upgraded.Data["quality"]);
            Assert.NotNull(upgraded.MediaFileId);

            // Radarr's formats, and the scores and thresholds of each Radarr profile on the Bigarr profile it maps to.
            var x265 = await db.CustomFormats.SingleAsync(f => f.Name == "Radarr x265");
            Assert.Contains("ReleaseTitleSpecification", x265.SpecificationsJson, StringComparison.Ordinal);
            var uhdFormat = await db.CustomFormats.SingleAsync(f => f.Name == "Radarr 2160p");
            var hdProfile = await db.QualityProfiles.Include(p => p.FormatScores).SingleAsync(p => p.Id == hd);
            Assert.Equal(-10000, hdProfile.FormatScores.Single(s => s.CustomFormatId == x265.Id).Score);
            Assert.DoesNotContain(hdProfile.FormatScores, s => s.CustomFormatId == uhdFormat.Id);
            Assert.Equal(100, hdProfile.CutoffFormatScore);
            Assert.Equal(1, hdProfile.MinUpgradeFormatScore);
            var uhdProfile = await db.QualityProfiles.Include(p => p.FormatScores).SingleAsync(p => p.Id == uhd);
            Assert.Equal(500, uhdProfile.FormatScores.Single(s => s.CustomFormatId == uhdFormat.Id).Score);
            Assert.Equal(1000, uhdProfile.CutoffFormatScore);

            var gumpFailed = await db.History.SingleAsync(h => h.TmdbId == ForrestGump);
            Assert.Equal(HistoryEventType.DownloadFailed, gumpFailed.EventType);
            Assert.Equal("Stalled", gumpFailed.Data["reason"]);
        }

        // A 4K Radarr, imported as 4k: Pulp Fiction gains a second version, the 1080p stays the default.
        var pulp4kFolder = Directory.CreateDirectory(Path.Combine(uhdRoot.Path, "Pulp Fiction (1994)")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(pulp4kFolder, "Pulp.Fiction.1994.2160p.UHD.BluRay.x265-GRP.mkv"), new byte[4096]);
        await using var radarr4k = await FakeHttpServer.StartAsync();
        radarr4k.Responder = r => Answer(r, "/radarr-4k", UhdMovies());

        status = await ImportAsync(session, radarr4k.Url, "4k", new Dictionary<string, int> { ["2"] = uhd }, new Dictionary<string, int> { ["/radarr-4k"] = uhdRoot.Id });
        Assert.Equal("completed", status.GetProperty("state").GetString());
        Assert.Equal(0, status.GetProperty("moviesAdded").GetInt32());
        Assert.Equal(1, status.GetProperty("versionsAdded").GetInt32());
        Assert.Equal(1, status.GetProperty("filesAdded").GetInt32());
        Assert.Equal(0, status.GetProperty("customFormatsAdded").GetInt32());
        Assert.Equal(2, status.GetProperty("customFormatsUpdated").GetInt32());
        Assert.Equal(1, status.GetProperty("profilesScored").GetInt32());

        movies = await MoviesAsync(session);
        var pulpVersions = movies[PulpFiction].GetProperty("versions").EnumerateArray().ToDictionary(v => v.GetProperty("name").GetString()!, v => v);
        Assert.Equal(["1080p", "4k"], pulpVersions.Keys.Order());
        Assert.True(pulpVersions["1080p"].GetProperty("isDefault").GetBoolean());
        Assert.False(pulpVersions["4k"].GetProperty("isDefault").GetBoolean());
        Assert.Equal(uhd, pulpVersions["4k"].GetProperty("qualityProfileId").GetInt32());
        Assert.Equal("Bluray-2160p", pulpVersions["4k"].GetProperty("file").GetProperty("quality").GetString());
        Assert.Equal(uhdRoot.Id, pulpVersions["4k"].GetProperty("rootFolderId").GetInt32());

        // The standard Radarr again, Heat monitored now: settings are updated, nothing is duplicated.
        radarr.Responder = r => Answer(r, "/radarr-movies", StandardMovies(heatMonitored: true));
        status = await ImportAsync(session, radarr.Url, "1080p", new Dictionary<string, int> { ["1"] = hd, ["2"] = uhd }, new Dictionary<string, int> { ["/radarr-movies"] = hdRoot.Id });
        Assert.Equal("completed", status.GetProperty("state").GetString());
        Assert.Equal(0, status.GetProperty("moviesAdded").GetInt32());
        Assert.Equal(0, status.GetProperty("versionsAdded").GetInt32());
        Assert.Equal(3, status.GetProperty("versionsUpdated").GetInt32());
        Assert.Equal(0, status.GetProperty("historyAdded").GetInt32());

        movies = await MoviesAsync(session);
        Assert.True(movies[Heat].GetProperty("versions").EnumerateArray().Single().GetProperty("monitored").GetBoolean());
        Assert.Equal(2, movies[PulpFiction].GetProperty("versions").GetArrayLength());
    }

    [Fact]
    public async Task An_import_with_an_unknown_profile_or_root_folder_is_refused_before_it_starts()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var badProfile = await session.PostAsJsonAsync("/api/v1/migration/radarr/import", new { url = "http://radarr:7878", apiKey = Key, versionName = "1080p", profileMap = new Dictionary<string, int> { ["1"] = 99999 }, rootFolderMap = new Dictionary<string, int>() });
        Assert.Equal(HttpStatusCode.BadRequest, badProfile.StatusCode);

        var badName = await session.PostAsJsonAsync("/api/v1/migration/radarr/import", new { url = "http://radarr:7878", apiKey = Key, versionName = "Not A Slug", profileMap = new Dictionary<string, int>(), rootFolderMap = new Dictionary<string, int>() });
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
    }

    private static async Task<JsonElement> ImportAsync(HttpClient session, string url, string versionName, Dictionary<string, int> profileMap, Dictionary<string, int> rootFolderMap)
    {
        var started = await session.PostAsJsonAsync("/api/v1/migration/radarr/import", new { url, apiKey = Key, versionName, profileMap, rootFolderMap });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            var status = await (await session.GetAsync("/api/v1/migration/radarr/status")).ReadJsonAsync();
            var state = status.GetProperty("state").GetString();
            if (state is "completed" or "failed")
            {
                Assert.Equal(JsonValueKind.Null, status.GetProperty("error").ValueKind);
                return status;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"The import did not finish: {status}");
            }

            await Task.Delay(150);
        }
    }

    /// <summary>The full record of every film, by TMDB id: the list only carries summaries.</summary>
    private static async Task<Dictionary<int, JsonElement>> MoviesAsync(HttpClient session)
    {
        var list = await (await session.GetAsync("/api/v1/movies")).ReadJsonAsync();
        var movies = new Dictionary<int, JsonElement>();
        foreach (var summary in list.EnumerateArray())
        {
            var tmdbId = summary.GetProperty("tmdbId").GetInt32();
            if (tmdbId is PulpFiction or ForrestGump or Heat)
            {
                movies[tmdbId] = await (await session.GetAsync($"/api/v1/movies/{summary.GetProperty("id").GetInt32()}")).ReadJsonAsync();
            }
        }

        return movies;
    }

    private static async Task<RootFolderResponse> AddRootAsync(HttpClient session, string path)
    {
        var created = await session.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<RootFolderResponse>(CompatTestHelpers.Json))!;
    }

    /// <summary>The fake Radarr: key checked like the real one, then the six resources the import reads.</summary>
    private static FakeHttpServer.Answer? Answer(FakeHttpServer.Received request, string rootPath, object[] movies)
    {
        if (!request.Headers.TryGetValue("X-Api-Key", out var key) || key != Key)
        {
            return new FakeHttpServer.Answer(401, """{"message":"Unauthorized"}""");
        }

        return request.Path switch
        {
            "/api/v3/system/status" => Json(new { appName = "Radarr", version = "5.14.0.9383", instanceName = "Radarr" }),
            "/api/v3/qualityprofile" => Json(new object[]
            {
                new { id = 1, name = "HD-1080p", formatItems = new object[] { new { format = 1, name = "Radarr x265", score = -10000 }, new { format = 2, name = "Radarr 2160p", score = 0 } }, minFormatScore = 0, cutoffFormatScore = 100, minUpgradeFormatScore = 1 },
                new { id = 2, name = "Ultra-HD", formatItems = new object[] { new { format = 1, name = "Radarr x265", score = 0 }, new { format = 2, name = "Radarr 2160p", score = 500 } }, minFormatScore = 0, cutoffFormatScore = 1000, minUpgradeFormatScore = 10 },
            }),
            "/api/v3/customformat" => Json(new object[]
            {
                CustomFormat(1, "Radarr x265", "ReleaseTitleSpecification", "[xh][ ._-]?265|\\bHEVC(\\b|\\d)"),
                CustomFormat(2, "Radarr 2160p", "ResolutionSpecification", 2160),
            }),
            "/api/v3/rootfolder" => Json(new object[] { new { id = 1, path = rootPath } }),
            "/api/v3/tag" => Json(new object[] { new { id = 1, label = "Seerr" } }),
            "/api/v3/movie" => Json(movies),
            "/api/v3/exclusions" => Json(new object[] { new { id = 1, tmdbId = FightClub, movieTitle = "Fight Club", movieYear = 1999 } }),
            "/api/v3/history" => HistoryPage(request.Query),
            _ => null,
        };
    }

    /// <summary>Radarr's paged history: <c>page</c> and <c>pageSize</c> from the query, records oldest first.</summary>
    private static FakeHttpServer.Answer HistoryPage(string query)
    {
        var parameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query);
        var page = int.Parse(parameters["page"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var pageSize = int.Parse(parameters["pageSize"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var all = History();
        return Json(new { page, pageSize, totalRecords = all.Length, records = all.Skip((page - 1) * pageSize).Take(pageSize).ToArray() });
    }

    /// <summary>
    /// Pulp Fiction (Radarr id 1) grabbed in 720p, imported, then upgraded to the 1080p file on disk (file id 700); an
    /// ignored download that Bigarr has no word for; a failed Forrest Gump (id 2); a line of a film Radarr no longer has.
    /// </summary>
    private static object[] History() =>
    [
        HistoryItem(1, 1, "grabbed", "2024-01-02T10:00:00Z", "Pulp.Fiction.1994.720p.BluRay.x264-OLD", 6, "Bluray-720p", "ABCDEF",
            new() { ["indexer"] = "The Old School (Prowlarr)", ["downloadClient"] = "qBittorrent", ["downloadClientName"] = "qBittorrent", ["size"] = "4000000000", ["guid"] = "tos-1", ["protocol"] = "torrent", ["indexerFlags"] = "0" }),
        HistoryItem(2, 1, "downloadFolderImported", "2024-01-02T11:00:00Z", "Pulp.Fiction.1994.720p.BluRay.x264-OLD", 6, "Bluray-720p", "ABCDEF",
            new() { ["fileId"] = "650", ["droppedPath"] = "/downloads/Pulp.Fiction.1994.720p.BluRay.x264-OLD.mkv", ["importedPath"] = "/radarr-movies/Pulp Fiction (1994)/Pulp.Fiction.1994.720p.BluRay.x264-OLD.mkv", ["downloadClientName"] = "qBittorrent" }),
        HistoryItem(3, 1, "movieFileDeleted", "2024-02-02T00:00:00Z", "/radarr-movies/Pulp Fiction (1994)/Pulp.Fiction.1994.720p.BluRay.x264-OLD.mkv", 6, "Bluray-720p", null,
            new() { ["reason"] = "Upgrade", ["size"] = "4000000000" }),
        HistoryItem(4, 1, "downloadFolderImported", "2024-02-02T00:00:01Z", "Pulp.Fiction.1994.1080p.BluRay.x264-GRP", 7, "Bluray-1080p", "123456",
            new() { ["fileId"] = "700", ["droppedPath"] = "/downloads/Pulp.Fiction.1994.1080p.BluRay.x264-GRP.mkv", ["importedPath"] = "/radarr-movies/Pulp Fiction (1994)/Pulp.Fiction.1994.1080p.BluRay.x264-GRP.mkv" }),
        HistoryItem(5, 1, "downloadIgnored", "2024-03-01T00:00:00Z", "Pulp.Fiction.1994.CAM", 0, "Unknown", "ZZZ", new() { ["message"] = "Manually ignored" }),
        HistoryItem(6, 2, "downloadFailed", "2024-03-02T00:00:00Z", "Forrest.Gump.1994.1080p.WEB-DL.x264-GRP", 3, "WEBDL-1080p", "FFF", new() { ["message"] = "Stalled", ["downloadClientName"] = "qBittorrent" }),
        HistoryItem(7, 42, "grabbed", "2024-03-03T00:00:00Z", "Gone.Film.2001.1080p", 7, "Bluray-1080p", "GGG", new() { ["indexer"] = "Nyaa.si" }),
    ];

    private static object HistoryItem(int id, int movieId, string eventType, string date, string sourceTitle, int qualityId, string qualityName, string? downloadId, Dictionary<string, string> data) => new
    {
        id,
        movieId,
        sourceTitle,
        languages = new object[] { new { id = 1, name = "English" } },
        quality = new { quality = new { id = qualityId, name = qualityName }, revision = new { version = 1, real = 0, isRepack = false } },
        customFormats = Array.Empty<object>(),
        customFormatScore = 0,
        qualityCutoffNotMet = false,
        date,
        downloadId,
        eventType,
        data,
    };

    /// <summary>A custom format in Radarr's API shape: <c>fields</c> as an array of <c>{name, value}</c>.</summary>
    private static object CustomFormat(int id, string name, string implementation, object value) => new
    {
        id,
        name,
        includeCustomFormatWhenRenaming = false,
        specifications = new object[]
        {
            new { name, implementation, negate = false, required = true, fields = new object[] { new { order = 0, name = "value", label = "Value", value } } },
        },
    };

    private static FakeHttpServer.Answer Json(object body) => new(200, JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static object[] StandardMovies(bool heatMonitored) =>
    [
        Movie(1, "Pulp Fiction", 1994, PulpFiction, "tt0110912", "/radarr-movies", "Pulp Fiction (1994)", true, 1, [1],
            MovieFile("Pulp.Fiction.1994.1080p.BluRay.x264-GRP.mkv", 7, "Bluray-1080p", 1080, 2048)),
        Movie(2, "Forrest Gump", 1994, ForrestGump, "tt0109830", "/radarr-movies", "Forrest Gump (1994)", true, 1, [],
            MovieFile("Forrest.Gump.1994.1080p.WEB-DL.x264-GRP.mkv", 3, "WEBDL-1080p", 1080, 1024)),
        Movie(3, "Heat", 1995, Heat, "tt0113277", "/radarr-movies", "Heat (1995)", heatMonitored, 1, [], null, "announced"),
    ];

    private static object[] UhdMovies() =>
    [
        Movie(7, "Pulp Fiction", 1994, PulpFiction, "tt0110912", "/radarr-4k", "Pulp Fiction (1994)", true, 2, [],
            MovieFile("Pulp.Fiction.1994.2160p.UHD.BluRay.x265-GRP.mkv", 19, "Bluray-2160p", 2160, 4096)),
    ];

    private static object Movie(int id, string title, int year, int tmdbId, string imdbId, string rootFolderPath, string folder, bool monitored, int profileId, int[] tags, object? movieFile, string minimumAvailability = "released") => new
    {
        id,
        title,
        originalTitle = title,
        sortTitle = title.ToLowerInvariant(),
        year,
        tmdbId,
        imdbId,
        overview = $"{title}, as Radarr knows it.",
        status = "released",
        runtime = 150,
        certification = "R",
        monitored,
        qualityProfileId = profileId,
        minimumAvailability,
        path = $"{rootFolderPath}/{folder}",
        rootFolderPath,
        folderName = $"{rootFolderPath}/{folder}",
        hasFile = movieFile is not null,
        added = "2024-01-01T00:00:00Z",
        inCinemas = $"{year}-10-14T00:00:00Z",
        tags,
        genres = new[] { "Crime", "Drama" },
        images = new object[]
        {
            new { coverType = "poster", url = $"/MediaCover/{id}/poster.jpg", remoteUrl = $"https://image.tmdb.org/t/p/original/{tmdbId}-poster.jpg" },
            new { coverType = "fanart", url = $"/MediaCover/{id}/fanart.jpg", remoteUrl = $"https://image.tmdb.org/t/p/original/{tmdbId}-fanart.jpg" },
        },
        ratings = new { tmdb = new { votes = 1000, value = 8.5, type = "user" } },
        alternateTitles = new object[] { new { title = title + " (alt)", sourceType = "tmdb" } },
        originalLanguage = new { id = 1, name = "English" },
        movieFile,
    };

    private static object MovieFile(string name, int qualityId, string qualityName, int resolution, long size) => new
    {
        id = qualityId * 100,
        relativePath = name,
        size,
        dateAdded = "2024-02-02T00:00:00Z",
        sceneName = Path.GetFileNameWithoutExtension(name),
        releaseGroup = "GRP",
        edition = "",
        indexerFlags = 0,
        quality = new { quality = new { id = qualityId, name = qualityName, source = "bluray", resolution, modifier = "none" }, revision = new { version = 1, real = 0, isRepack = false } },
        languages = new object[] { new { id = 1, name = "English" } },
    };
}
