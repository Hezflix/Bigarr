using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Xem;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Series;

/// <summary>The app on the fixture catalogue with TheXEM answering from recordings, clock fixed on 2026-11-09.</summary>
public sealed class SeriesScanApiFactory()
    : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Xem:Enabled"] = "true" }, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now))
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IXemClient>(XemFakes.RecordedClient()));
    }
}

/// <summary>Scans a temporary series library laid out like a Sonarr one: empty video files, real show names.</summary>
public sealed class SeriesLibraryScanTests(SeriesScanApiFactory factory) : IClassFixture<SeriesScanApiFactory>, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-tv-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Scanning_a_series_library_attaches_files_to_episodes_and_lists_what_it_cannot_place()
    {
        const string bb = "Breaking Bad (2008)";
        Video(bb + "/Season 01", "Breaking.Bad.S01E01.720p.BluRay.x264-GRP.mkv");
        Video(bb + "/Season 01", "Breaking.Bad.S01E01.1080p.WEB-DL.DD5.1.H.264-OTHER.mkv");
        Video(bb + "/Season 01", "Breaking.Bad.S01E02E03.720p.BluRay.x264-GRP.mkv");
        Video(bb + "/Season 01", "Breaking.Bad.S01E04.720p.BluRay.x264-GRP-sample.mkv");
        Text(bb + "/Season 01", "Breaking.Bad.S01E04.720p.BluRay.x264-GRP.nfo");
        Video(bb + "/Season 2", "05.mkv");
        Video(bb + "/Specials", "Breaking.Bad.S00E01.Good.Cop.Bad.Cop.mkv");
        Video(bb + "/Extras", "Making.Of.mkv");
        Video(bb, "random-junk.mkv");
        Video(bb, "Breaking.Bad.S09E01.720p.HDTV.x264-GRP.mkv");
        Video("Frieren {tvdb-424536}", "[SubsPlease] Sousou no Frieren - 05 (1080p) [ABCD1234].mkv");
        Video("Frieren {tvdb-424536}", "[SubsPlease] Sousou no Frieren S2 - 05 (1080p) [ABCD1234].mkv");
        Video("The Daily Show", "The.Daily.Show.2026.11.10.Guest.1080p.WEB.h264-GRP.mkv");
        Video("Some Unknown Show (2011)", "Some.Unknown.Show.S01E01.720p.HDTV.x264-GRP.mkv");
        Directory.CreateDirectory(Path.Combine(_root, "Empty Folder"));

        var client = await ClientAsync();
        // Scene names first, as the slow schedule would have done: "Sousou no Frieren S2" is XEM's name for season 2.
        var xem = await client.PostAsync($"/api/v1/system/tasks/{SceneMappingRefreshJob.JobName}/run", null);
        Assert.True(xem.IsSuccessStatusCode, await xem.Content.ReadAsStringAsync());

        var root = await AddRootAsync(client, _root, MediaKind.Series);
        var first = await ScanRootAsync(client, root.Id);

        Assert.Equal(5, first.FoldersTotal);
        Assert.Equal(3, first.SeriesAdded);
        Assert.Equal(7, first.FilesAdded);
        Assert.Equal(3, first.UnmatchedFiles);
        Assert.Equal(2, first.Unmapped);

        // The film side saw nothing of it.
        Assert.Empty((await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!);

        var list = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!;
        var breakingBad = list.Single(s => s.TvdbId == 81189);
        var detail = (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{breakingBad.Id}", Json))!;
        var version = Assert.Single(detail.Versions);
        Assert.Equal(bb, version.FolderName);
        Assert.False(version.Monitored);
        Assert.False(detail.Monitored);

        var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{breakingBad.Id}/files", Json))!;
        Assert.Equal(["S00E01", "S01E01", "S01E02-E03", "S02E05"], files.Files.Select(f => f.Episodes).Order(StringComparer.Ordinal));
        var multi = files.Files.Single(f => f.Episodes == "S01E02-E03");
        Assert.Equal(2, multi.EpisodeIds.Count);
        Assert.Equal("Bluray-720p", multi.Quality);
        Assert.Equal("GRP", multi.ReleaseGroup);
        Assert.Equal(Path.Combine("Season 01", "Breaking.Bad.S01E02E03.720p.BluRay.x264-GRP.mkv"), multi.RelativePath);

        Assert.Equal(
            [(UnmatchedFileReason.Duplicate, "Breaking.Bad.S01E01.720p.BluRay.x264-GRP.mkv"), (UnmatchedFileReason.EpisodeNotFound, "Breaking.Bad.S09E01.720p.HDTV.x264-GRP.mkv"), (UnmatchedFileReason.NotParsable, "random-junk.mkv")],
            files.Unmatched.Select(u => (u.Reason, Path.GetFileName(u.RelativePath))).OrderBy(u => u.Item2, StringComparer.Ordinal));
        Assert.Contains("already has", files.Unmatched.Single(u => u.Reason == UnmatchedFileReason.Duplicate).Detail);

        // The episode list shows the files per version.
        var episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{breakingBad.Id}/episodes", Json))!;
        Assert.Equal(5, episodes.Count(e => e.Versions.Single().HasFile));

        // Frieren: absolute 05 is S01E05 (TVDB); "S2 - 05" is XEM's scene numbering for TVDB S02E05 (absolute 33).
        var frieren = list.Single(s => s.TvdbId == XemFakes.FrierenTvdbId);
        var frierenFiles = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{frieren.Id}/files", Json))!;
        Assert.Equal(["S01E05", "S02E05"], frierenFiles.Files.Select(f => f.Episodes).Order(StringComparer.Ordinal));
        Assert.Empty(frierenFiles.Unmatched);
        Assert.Equal(SeriesType.Anime, (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{frieren.Id}", Json))!.SeriesType);

        var daily = list.Single(s => s.TvdbId == 71256);
        var dailyFile = Assert.Single((await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{daily.Id}/files", Json))!.Files);
        var dailyEpisode = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{daily.Id}/episodes", Json))!.Single(e => e.Id == dailyFile.EpisodeIds.Single());
        Assert.Equal(new DateOnly(2026, 11, 10), dailyEpisode.AirDate);

        var unmapped = (await client.GetFromJsonAsync<List<UnmappedFolderResponse>>($"/api/v1/rootfolders/{root.Id}/unmapped", Json))!;
        Assert.Equal(
            [("Empty Folder", UnmappedReason.NoVideoFiles), ("Some Unknown Show (2011)", UnmappedReason.NoMatch)],
            unmapped.Select(u => (u.RelativePath, u.Reason)).OrderBy(u => u.RelativePath, StringComparer.Ordinal));
        Assert.All(unmapped, u => Assert.Equal(MediaKind.Series, u.Kind));

        // Nothing on disk moved.
        Assert.True(File.Exists(Path.Combine(_root, bb, "random-junk.mkv")));
        Assert.True(File.Exists(Path.Combine(_root, bb, "Season 01", "Breaking.Bad.S01E01.720p.BluRay.x264-GRP.mkv")));

        // A second scan changes nothing.
        var second = await ScanRootAsync(client, root.Id);
        Assert.Equal((0, 0, 3, 2), (second.SeriesAdded, second.FilesAdded, second.UnmatchedFiles, second.Unmapped));
        Assert.Equal(4, (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{breakingBad.Id}/files", Json))!.Files.Count);

        // A junk file removed from disk leaves the list on the next series rescan; a new episode file joins.
        File.Delete(Path.Combine(_root, bb, "random-junk.mkv"));
        Video(bb + "/Season 01", "Breaking.Bad.S01E04.720p.BluRay.x264-GRP.mkv");
        var rescan = await RescanSeriesAsync(client, breakingBad.Id);
        Assert.Equal((1, 2), (rescan.FilesAdded, rescan.UnmatchedFiles));
        files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{breakingBad.Id}/files", Json))!;
        Assert.DoesNotContain(files.Unmatched, u => u.RelativePath == "random-junk.mkv");
        Assert.Contains(files.Files, f => f.Episodes == "S01E04");

        // The unknown folder is mapped by hand to a library series: it becomes a second, unmonitored version.
        var someUnknown = unmapped.Single(u => u.Reason == UnmappedReason.NoMatch);
        var mapped = await client.PostAsJsonAsync($"/api/v1/series/unmapped/{someUnknown.Id}/map", new MapSeriesFolderRequest(SeriesId: breakingBad.Id));
        Assert.Equal(HttpStatusCode.OK, mapped.StatusCode);
        var mapping = (await mapped.Content.ReadFromJsonAsync<MapSeriesFolderResponse>(Json))!;
        await WaitAsync(client, $"/api/v1/series/{breakingBad.Id}/rescan");
        detail = (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{breakingBad.Id}", Json))!;
        var second720 = detail.Versions.Single(v => v.Id == mapping.VersionId);
        Assert.Equal("Some Unknown Show (2011)", second720.FolderName);
        Assert.False(second720.Monitored);
        files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{breakingBad.Id}/files?versionId={mapping.VersionId}", Json))!;
        Assert.Equal("S01E01", Assert.Single(files.Files).Episodes);
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<UnmappedFolderResponse>>($"/api/v1/rootfolders/{root.Id}/unmapped", Json))!, u => u.Id == someUnknown.Id);
    }

    [Fact]
    public async Task Root_folders_have_a_kind_that_the_add_pages_and_the_radarr_api_respect()
    {
        await using var fresh = new ConfiguredXemFactory(XemFakes.RecordedClient());
        var client = fresh.CreateClientWithCookies();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword })).StatusCode);
        var films = await AddRootAsync(client, Directory.CreateTempSubdirectory("bigarr-films-").FullName, MediaKind.Movies);
        var shows = await AddRootAsync(client, Path.Combine(_root, "kind"), MediaKind.Series, create: true);
        Assert.Equal(MediaKind.Series, shows.Kind);

        var profiles = await client.GetFromJsonAsync<List<Bigarr.Api.Features.Quality.QualityProfileSummary>>("/api/v1/qualityprofiles", Json);
        var profileId = profiles!.First().Id;

        var seriesOnFilmRoot = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profileId, films.Id, TvdbId: 81189));
        Assert.Equal(HttpStatusCode.BadRequest, seriesOnFilmRoot.StatusCode);
        var movieOnSeriesRoot = await client.PostAsJsonAsync("/api/v1/movies", new { tmdbId = 603, qualityProfileId = profileId, rootFolderId = shows.Id });
        Assert.Equal(HttpStatusCode.BadRequest, movieOnSeriesRoot.StatusCode);

        // A root in use keeps its kind; an empty one can switch.
        var created = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profileId, shows.Id, TvdbId: 81189, FolderName: "Breaking Bad (2008)"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/rootfolders/{shows.Id}", new UpdateRootFolderRequest(MediaKind.Movies))).StatusCode);
        var switched = await client.PutAsJsonAsync($"/api/v1/rootfolders/{films.Id}", new UpdateRootFolderRequest(MediaKind.Series));
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal(MediaKind.Series, (await switched.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!.Kind);
        await client.PutAsJsonAsync($"/api/v1/rootfolders/{films.Id}", new UpdateRootFolderRequest(MediaKind.Movies));

        // Seerr only sees film roots through the Radarr API.
        var key = await CompatKeyAsync(client);
        var compat = fresh.CreateAnonymousClient();
        compat.DefaultRequestHeaders.Add("X-Api-Key", key);
        var radarrRoots = (await compat.GetFromJsonAsync<List<JsonElement>>("/api/v3/rootfolder"))!;
        Assert.Contains(radarrRoots, r => r.GetProperty("id").GetInt32() == films.Id);
        Assert.DoesNotContain(radarrRoots, r => r.GetProperty("id").GetInt32() == shows.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await compat.GetAsync($"/api/v3/rootfolder/{shows.Id}")).StatusCode);

        var deleted = await client.DeleteAsync($"/api/v1/series/{(await created.Content.ReadFromJsonAsync<SeriesResponse>(Json))!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Xem_being_unreachable_never_fails_adding_or_refreshing_a_series()
    {
        await using var offline = new ConfiguredXemFactory(XemFakes.UnreachableClient());
        var client = offline.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var root = await AddRootAsync(client, Path.Combine(_root, "offline"), MediaKind.Series, create: true);
        var profiles = await client.GetFromJsonAsync<List<Bigarr.Api.Features.Quality.QualityProfileSummary>>("/api/v1/qualityprofiles", Json);
        var added = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profiles!.First().Id, root.Id, TvdbId: XemFakes.FrierenTvdbId));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var id = (await added.Content.ReadFromJsonAsync<SeriesResponse>(Json))!.Id;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/series/{id}/refresh", null)).StatusCode);

        var run = await client.PostAsync($"/api/v1/system/tasks/{SceneMappingRefreshJob.JobName}/run", null);
        Assert.True(run.IsSuccessStatusCode);
        Assert.Contains("scene names unavailable", await run.Content.ReadAsStringAsync());
    }

    private sealed class ConfiguredXemFactory(IXemClient xem)
        : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Xem:Enabled"] = "true" }, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now))
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton(xem));
        }
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<string> CompatKeyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/apikeys", new { name = $"seerr-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;
    }

    private static async Task<RootFolderResponse> AddRootAsync(HttpClient client, string path, MediaKind kind, bool create = false)
    {
        if (create)
        {
            Directory.CreateDirectory(path);
        }

        var response = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path, kind), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }

    private static async Task<ScanStatusResponse> ScanRootAsync(HttpClient client, int rootFolderId)
    {
        var start = await client.PostAsync($"/api/v1/rootfolders/{rootFolderId}/scan", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        return await WaitAsync(client, $"/api/v1/rootfolders/{rootFolderId}/scan");
    }

    private static async Task<ScanStatusResponse> RescanSeriesAsync(HttpClient client, int seriesId)
    {
        var start = await client.PostAsync($"/api/v1/series/{seriesId}/rescan", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        return await WaitAsync(client, $"/api/v1/series/{seriesId}/rescan");
    }

    private static async Task<ScanStatusResponse> WaitAsync(HttpClient client, string statusUrl)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>(statusUrl, Json))!;
            if (status.State is ScanState.Completed or ScanState.Failed)
            {
                Assert.Null(status.Error);
                return status;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"{statusUrl} did not finish.");
    }

    private void Video(string folder, string file)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        File.WriteAllBytes(Path.Combine(dir, file), new byte[1024]);
    }

    private void Text(string folder, string file)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        File.WriteAllText(Path.Combine(dir, file), "not a video");
    }
}
