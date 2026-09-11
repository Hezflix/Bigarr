using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Library;

/// <summary>Scans a real (temporary) library laid out like a Radarr one, against the fixture catalogue.</summary>
public sealed class LibraryScanTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-lib-").FullName;
    private readonly string _root4k = Directory.CreateTempSubdirectory("bigarr-lib4k-").FullName;

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_root4k);
    }

    [Fact]
    public async Task Scanning_an_existing_library_creates_one_version_per_file_and_lists_unmapped_folders()
    {
        Video(_root, "The Matrix (1999)", "The.Matrix.1999.1080p.BluRay.x264-GRP.mkv");
        Video(_root, "The Matrix (1999)", "The.Matrix.1999.2160p.UHD.BluRay.x265-GRP.mkv");
        Video(_root, "The Matrix (1999)", "The.Matrix.1999.1080p.BluRay.x264-GRP-sample.mkv");
        Video(_root, "The Matrix (1999)/Extras", "Making.Of.The.Matrix.mkv");
        Text(_root, "The Matrix (1999)", "The.Matrix.1999.1080p.BluRay.x264-GRP.nfo");
        Video(_root, "Amelie (2001)", "Amelie.2001.720p.BluRay.x264-GRP.mkv");
        Video(_root, "Interstellar (2014) {tmdb-157336}", "Interstellar.2014.1080p.WEB-DL.DDP5.1.H.264-GRP.mkv");
        Video(_root, "Parasite (2019)", "Parasite.2019.1080p.BluRay.x264-GRP.mkv");
        Video(_root, "Blade Runner 2049 (2017)", "Blade.Runner.2049.2017.2160p.UHD.BluRay.x265-GRP.mkv");
        Video(_root, "Some Unknown Movie (2011)", "Some.Unknown.Movie.2011.1080p.BluRay.x264-GRP.mkv");
        Directory.CreateDirectory(Path.Combine(_root, "Empty Folder"));
        Directory.CreateDirectory(Path.Combine(_root, ".hidden"));
        Video(_root4k, "The Matrix 1999 2160p UHD", "The.Matrix.1999.2160p.UHD.BluRay.REMUX.HDR.HEVC.TrueHD.Atmos-GRP.mkv");

        var client = await ClientAsync();
        var root = await AddRootAsync(client, _root);

        var first = await ScanAsync(client, root.Id);
        Assert.Equal(ScanState.Completed, first.State);
        Assert.Equal(7, first.FoldersTotal);
        Assert.Equal(5, first.MoviesAdded);
        Assert.Equal(6, first.VersionsAdded);
        Assert.Equal(6, first.FilesAdded);
        Assert.Equal(2, first.Unmapped);

        var movies = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!;
        var matrix = movies.Single(m => m.TmdbId == 603);
        var detail = (await client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{matrix.Id}", Json))!;

        Assert.Equal(["1080p", "2160p"], detail.Versions.Select(v => v.Name));
        // The scan files a version under the profile of the resolution it found.
        Assert.Equal([QualitySeeder.HdProfileName, QualitySeeder.UhdProfileName], detail.Versions.Select(v => v.QualityProfile));
        Assert.All(detail.Versions, v =>
        {
            Assert.False(v.Monitored);
            Assert.Equal(VersionStatus.Available, v.Status);
            Assert.Equal("The Matrix (1999)", v.FolderName);
            Assert.Equal("GRP", v.File!.ReleaseGroup);
        });
        Assert.True(detail.Versions[0].IsDefault);
        Assert.Equal("Bluray-1080p", detail.Versions[0].File!.Quality);
        Assert.Equal("Bluray-2160p", detail.Versions[1].File!.Quality);
        Assert.Equal("The.Matrix.1999.2160p.UHD.BluRay.x265-GRP.mkv", detail.Versions[1].File!.RelativePath);
        Assert.False(detail.Monitored);

        Assert.Contains(movies, m => m.TmdbId == 194);
        Assert.Contains(movies, m => m.TmdbId == 157336);
        Assert.Contains(movies, m => m.TmdbId == 496243);
        Assert.Contains(movies, m => m.TmdbId == 335984);

        var unmapped = (await client.GetFromJsonAsync<List<UnmappedFolderResponse>>($"/api/v1/rootfolders/{root.Id}/unmapped", Json))!;
        Assert.Equal(2, unmapped.Count);
        Assert.Equal(UnmappedReason.NoVideoFiles, unmapped.Single(u => u.RelativePath == "Empty Folder").Reason);
        Assert.Equal(UnmappedReason.NoMatch, unmapped.Single(u => u.RelativePath == "Some Unknown Movie (2011)").Reason);

        var roots = (await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!;
        var listed = roots.Single(r => r.Id == root.Id);
        Assert.Equal(6, listed.VersionCount);
        Assert.Equal(2, listed.UnmappedFolderCount);
        Assert.True(listed.Accessible);

        // A second scan changes nothing.
        var second = await ScanAsync(client, root.Id);
        Assert.Equal(0, second.MoviesAdded);
        Assert.Equal(0, second.VersionsAdded);
        Assert.Equal(0, second.FilesAdded);
        Assert.Equal(2, second.Unmapped);

        // A 4K library migrated from a second Radarr: same film, its own folder name on its own root.
        var root4k = await AddRootAsync(client, _root4k);
        var third = await ScanAsync(client, root4k.Id);
        Assert.Equal(0, third.MoviesAdded);
        Assert.Equal(1, third.VersionsAdded);

        detail = (await client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{matrix.Id}", Json))!;
        var uhd = detail.Versions.Single(v => v.RootFolderId == root4k.Id);
        Assert.Equal("2160p-2", uhd.Name);
        Assert.Equal("The Matrix 1999 2160p UHD", uhd.FolderName);
        Assert.Equal("Remux-2160p", uhd.File!.Quality);
        Assert.Equal(3, detail.Versions.Count);

        // Root folders in use cannot be deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/rootfolders/{root.Id}")).StatusCode);
    }

    [Fact]
    public async Task Root_folders_are_validated()
    {
        var client = await ClientAsync();

        var missing = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(Path.Combine(_root, "does-not-exist")));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var relative = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest("movies"));
        Assert.Equal(HttpStatusCode.BadRequest, relative.StatusCode);

        var empty = Directory.CreateTempSubdirectory("bigarr-empty-").FullName;
        try
        {
            var created = await AddRootAsync(client, empty);
            var duplicate = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(empty + Path.DirectorySeparatorChar));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            var scan = await ScanAsync(client, created.Id);
            Assert.Equal(ScanState.Completed, scan.State);
            Assert.Equal(0, scan.FoldersTotal);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/rootfolders/{created.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/rootfolders/{created.Id}/scan")).StatusCode);
        }
        finally
        {
            TryDelete(empty);
        }
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<RootFolderResponse> AddRootAsync(HttpClient client, string path)
    {
        var response = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }

    private static async Task<ScanStatusResponse> ScanAsync(HttpClient client, int rootFolderId)
    {
        var start = await client.PostAsync($"/api/v1/rootfolders/{rootFolderId}/scan", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/rootfolders/{rootFolderId}/scan", Json))!;
            if (status.State is ScanState.Completed or ScanState.Failed)
            {
                Assert.Null(status.Error);
                return status;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Scan did not finish within 30 seconds.");
    }

    private static void Video(string root, string folder, string file)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, folder)).FullName;
        File.WriteAllBytes(Path.Combine(dir, file), new byte[4096]);
    }

    private static void Text(string root, string folder, string file)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, folder)).FullName;
        File.WriteAllText(Path.Combine(dir, file), "not a video");
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
