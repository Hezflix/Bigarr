using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Import;

/// <summary>Media management settings, the rename command and physical deletion through the recycle bin, on a scanned library.</summary>
public sealed class MediaManagementTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-mm-lib-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-mm-bin-").FullName;

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_bin);
    }

    [Fact]
    public async Task Settings_are_validated_previewed_and_drive_new_folder_names()
    {
        var client = await ClientAsync(factory);

        // The other test of this class may have run first: start from the shipped defaults.
        var reset = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, null, 7), Json);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var defaults = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
        Assert.True(defaults.RenameFiles);
        Assert.Equal(MediaManagementConfig.DefaultFileNameTemplate, defaults.FileNameTemplate);
        Assert.Equal(TransferMode.HardlinkOrCopy, defaults.TransferMode);
        Assert.Null(defaults.RecycleBinPath);
        Assert.Equal("The Matrix (1999) {edition-Director's Cut} [Bluray-1080p Proper].mkv", defaults.FileNamePreview);
        Assert.Equal("The Matrix (1999)", defaults.FolderNamePreview);
        Assert.Contains(defaults.Tokens, t => t.Token == "{Version}");

        var noDiscriminant = await client.PutAsJsonAsync("/api/v1/mediamanagement", defaults.ToRequest() with { FileNameTemplate = "{Movie Title} ({Release Year})" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, noDiscriminant.StatusCode);
        Assert.Contains("must contain", await noDiscriminant.Content.ReadAsStringAsync());

        var qualityInFolder = await client.PutAsJsonAsync("/api/v1/mediamanagement", defaults.ToRequest() with { FolderNameTemplate = "{Movie Title} {Quality Full}" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, qualityInFolder.StatusCode);
        Assert.Contains("cannot be used in a folder name", await qualityInFolder.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/mediamanagement", defaults.ToRequest() with { RecycleBinRetentionDays = -1 }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/mediamanagement", defaults.ToRequest() with { RecycleBinPath = "relative/bin" }, Json)).StatusCode);

        var preview = await client.PostAsJsonAsync("/api/v1/mediamanagement/naming/preview", new NamingPreviewRequest("{Movie.Title}.{Release Year}.{Quality.Title}-{Release Group}", "{Movie TitleThe} ({Release Year})"), Json);
        var previewed = (await preview.Content.ReadFromJsonAsync<NamingPreviewResponse>(Json))!;
        Assert.Equal("The.Matrix.1999.Bluray-1080p-SPARKS.mkv", previewed.FileName);
        Assert.Equal("Matrix, The (1999)", previewed.FolderName);
        Assert.Empty(previewed.FileErrors);
        Assert.Empty(previewed.FolderErrors);

        var saved = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(false, defaults.FileNameTemplate, "{Movie TitleThe} ({Release Year})", TransferMode.Copy, _bin, 3), Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var updated = (await saved.Content.ReadFromJsonAsync<MediaManagementResponse>(Json))!;
        Assert.False(updated.RenameFiles);
        Assert.Equal(TransferMode.Copy, updated.TransferMode);
        Assert.Equal(_bin, updated.RecycleBinPath);
        Assert.Equal(3, updated.RecycleBinRetentionDays);
        Assert.Equal("Matrix, The (1999)", updated.FolderNamePreview);

        // A version created now follows the folder template; the folder name is then frozen.
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "settings"));
        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        Assert.Equal("Matrix, The (1999)", movie.Versions.Single().FolderName);
        await client.DeleteAsync($"/api/v1/movies/{movie.Id}");

        // Sonarr's season-pack upgrade: all by default, a threshold or any; the threshold is a percentage.
        Assert.Equal(SeasonPackUpgrade.All, defaults.SeasonPackUpgrade);
        Assert.Equal(100, defaults.SeasonPackUpgradeThreshold);
        var pack = await client.PutAsJsonAsync("/api/v1/mediamanagement", defaults.ToRequest() with { SeasonPackUpgrade = SeasonPackUpgrade.Threshold, SeasonPackUpgradeThreshold = 75 }, Json);
        var packSaved = (await pack.Content.ReadFromJsonAsync<MediaManagementResponse>(Json))!;
        Assert.Equal((SeasonPackUpgrade.Threshold, 75d), (packSaved.SeasonPackUpgrade, packSaved.SeasonPackUpgradeThreshold));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/mediamanagement", defaults.ToRequest() with { SeasonPackUpgradeThreshold = 120 }, Json)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/mediamanagement", defaults.ToRequest() with { SeasonPackUpgrade = SeasonPackUpgrade.All, SeasonPackUpgradeThreshold = 100 }, Json)).StatusCode);
    }

    [Fact]
    public async Task Files_are_renamed_to_the_template_and_deleted_through_the_recycle_bin()
    {
        var client = await ClientAsync(factory);
        var library = Path.Combine(_root, "rename");
        var video = Video(library, "The Matrix (1999)", "The.Matrix.1999.1080p.BluRay.x264-GRP.mkv");
        var root = await AddRootAsync(client, library);
        await ScanAsync(client, root.Id);
        var movie = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 603);
        var version = (await GetMovieAsync(client, movie.Id)).Versions.Single();
        Assert.Equal("The.Matrix.1999.1080p.BluRay.x264-GRP.mkv", version.File!.RelativePath);

        var configured = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, _bin, 7), Json);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);

        var preview = (await client.GetFromJsonAsync<List<RenamePreviewResponse>>($"/api/v1/rename?movieId={movie.Id}", Json))!;
        var row = Assert.Single(preview);
        Assert.True(row.Changed);
        Assert.Equal("The Matrix (1999) [Bluray-1080p].mkv", row.NewRelativePath);

        var renamed = await client.PostAsJsonAsync("/api/v1/rename", new RenameRequest(movie.Id), Json);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var result = Assert.Single((await renamed.Content.ReadFromJsonAsync<List<RenameResultResponse>>(Json))!);
        Assert.True(result.Renamed, result.Error);
        Assert.False(File.Exists(video));
        Assert.True(File.Exists(Path.Combine(library, "The Matrix (1999)", "The Matrix (1999) [Bluray-1080p].mkv")));
        Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Renamed));
        Assert.All(await client.GetFromJsonAsync<List<RenamePreviewResponse>>($"/api/v1/rename?movieId={movie.Id}", Json) ?? [], r => Assert.False(r.Changed));

        // Delete the file only: the version stays, missing again; the file waits in the recycle bin.
        var deleted = await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{version.Id}/file");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var without = (await deleted.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
        Assert.Null(without.File);
        Assert.Equal(VersionStatus.Missing, without.Status);
        Assert.True(File.Exists(Path.Combine(_bin, "The Matrix (1999)", "The Matrix (1999) [Bluray-1080p].mkv")));
        var fileDeleted = Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.FileDeleted));
        Assert.EndsWith("[Bluray-1080p].mkv", fileDeleted.Data["recycledTo"], StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{version.Id}/file")).StatusCode);

        // Delete the movie with its files: the whole folder goes to the bin.
        Video(library, "The Matrix (1999)", "The.Matrix.1999.720p.BluRay.x264-GRP.mkv");
        await ScanAsync(client, root.Id);
        var gone = await client.DeleteAsync($"/api/v1/movies/{movie.Id}?deleteFiles=true");
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(library, "The Matrix (1999)")));
        Assert.Contains(Directory.GetDirectories(_bin), d => Path.GetFileName(d).StartsWith("The Matrix (1999)", StringComparison.Ordinal) && Directory.GetFiles(d).Any(f => f.EndsWith("720p.BluRay.x264-GRP.mkv", StringComparison.Ordinal)));
    }

    private static string Video(string root, string folder, string name)
    {
        var path = Path.Combine(root, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    private static async Task ScanAsync(HttpClient client, int rootId)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{rootId}/scan", null)).StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/rootfolders/{rootId}/scan", Json))!;
            if (status.State is ScanState.Completed)
            {
                return;
            }

            Assert.NotEqual(ScanState.Failed, status.State);
            Assert.True(DateTime.UtcNow < deadline, "scan did not finish");
            await Task.Delay(100);
        }
    }
}

internal static class MediaManagementResponseExtensions
{
    public static MediaManagementRequest ToRequest(this MediaManagementResponse response) =>
        new(response.RenameFiles, response.FileNameTemplate, response.FolderNameTemplate, response.TransferMode, response.RecycleBinPath, response.RecycleBinRetentionDays);
}
