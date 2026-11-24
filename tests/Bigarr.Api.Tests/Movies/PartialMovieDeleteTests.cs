using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.History;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Movies;

/// <summary>
/// A film with versions in several folders loses them one folder after the other. When one folder cannot go, the
/// folders that did go must leave the database too, with their history line: the library must not list files that
/// no longer exist.
/// </summary>
public sealed class PartialMovieDeleteTests : IDisposable
{
    private readonly string _rootA = Directory.CreateTempSubdirectory("bigarr-partial-movie-a-").FullName;
    private readonly string _rootB = Directory.CreateTempSubdirectory("bigarr-partial-movie-b-").FullName;

    public void Dispose()
    {
        TryDelete(_rootA);
        TryDelete(_rootB);
    }

    [Fact]
    public async Task A_film_delete_that_fails_on_one_folder_clears_the_files_that_did_go_and_keeps_the_film()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return; // a read-only folder stops nothing for root: the condition cannot be set up
        }

        var folderA = Path.Combine(_rootA, "The Matrix (1999)");
        var folderB = Path.Combine(_rootB, "The Matrix 1999 2160p UHD");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);
        File.WriteAllBytes(Path.Combine(folderA, "The.Matrix.1999.1080p.BluRay.x264-GRP.mkv"), new byte[4096]);
        var fileB = Path.Combine(folderB, "The.Matrix.1999.2160p.UHD.BluRay.REMUX.HDR.HEVC.TrueHD.Atmos-GRP.mkv");
        File.WriteAllBytes(fileB, new byte[4096]);

        using var factory = new BigarrApiFactory();
        var client = await ClientAsync(factory);
        var rootA = await AddRootAsync(client, _rootA);
        var rootB = await AddRootAsync(client, _rootB);
        await ScanAsync(client, rootA.Id);
        await ScanAsync(client, rootB.Id);
        var movie = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 603);
        var detail = await GetMovieAsync(client, movie.Id);
        var versionA = detail.Versions.Single(v => v.RootFolderId == rootA.Id);
        var versionB = detail.Versions.Single(v => v.RootFolderId == rootB.Id);
        Assert.NotNull(versionA.File);
        Assert.NotNull(versionB.File);

        try
        {
            File.SetUnixFileMode(folderB, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var failed = await client.DeleteAsync($"/api/v1/movies/{movie.Id}?deleteFiles=true");
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

            Assert.False(Directory.Exists(folderA));
            Assert.True(File.Exists(fileB));
            Assert.Contains((await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!, m => m.Id == movie.Id);
            var after = await GetMovieAsync(client, movie.Id);
            Assert.Null(after.Versions.Single(v => v.Id == versionA.Id).File);
            Assert.NotNull(after.Versions.Single(v => v.Id == versionB.Id).File);
            var deleted = await HistoryAsync(client, movie.Id, HistoryEventType.FileDeleted);
            var line = Assert.Single(deleted);
            Assert.Equal(versionA.Id, line.VersionId);
        }
        finally
        {
            File.SetUnixFileMode(folderB, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var retried = await client.DeleteAsync($"/api/v1/movies/{movie.Id}?deleteFiles=true");
        Assert.True(retried.IsSuccessStatusCode, await retried.Content.ReadAsStringAsync());
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!, m => m.Id == movie.Id);
        Assert.False(Directory.Exists(folderB));
    }

    private static async Task ScanAsync(HttpClient client, int rootId)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{rootId}/scan", null)).StatusCode);
        await WaitUntilAsync(async () =>
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/rootfolders/{rootId}/scan", Json))!;
            Assert.NotEqual(ScanState.Failed, status.State);
            return status.State is ScanState.Completed ? status : null;
        }, "the scan to finish");
    }
}
