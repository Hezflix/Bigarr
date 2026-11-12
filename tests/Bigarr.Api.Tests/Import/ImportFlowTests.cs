using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure.Downloads.Mock;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Import;

/// <summary>The M5 acceptance loop on the mock client: finished download → renamed file in the library without breaking the seed, upgrades through the recycle bin, refusals resolved by hand, versions side by side, hand-added torrents.</summary>
public sealed class ImportFlowTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-import-lib-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-import-bin-").FullName;

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_bin);
    }

    [Fact]
    public async Task A_finished_download_is_imported_then_upgraded_then_refused_then_imported_by_hand()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, _root);
        await ConfigureAsync(client, _bin);

        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        var version = movie.Versions.Single();
        var libraryFolder = Path.Combine(_root, "The Matrix (1999)");
        var indexer = await AddMockIndexerAsync(client, "Mock import idx");
        var downloadClient = await AddMockClientAsync(client, "Mock import qbt");
        try
        {
            var search = await SearchAsync(client, movie.Id);

            // 1. WEBDL-1080p: imported, renamed by the template, hardlinked so the torrent keeps seeding.
            var webdl = search.Releases.Single(r => r.Title.Contains("1080p.WEB-DL.DDP5.1.H.264-NTb", StringComparison.Ordinal));
            var grab1 = await GrabAndCompleteAsync(client, webdl, version.Id);
            var imported = await WaitForFileAsync(client, movie.Id, version.Id);
            Assert.Equal("The Matrix (1999) [WEBDL-1080p].mkv", imported.File!.RelativePath);
            Assert.Equal(VersionStatus.Available, imported.Status);
            Assert.True(File.Exists(Path.Combine(libraryFolder, imported.File.RelativePath)));
            Assert.Equal(webdl.Title, imported.File.OriginalReleaseTitle);
            Assert.Equal("NTb", imported.File.ReleaseGroup);
            Assert.Equal("WEBDL-1080p", imported.File.Quality);
            Assert.Equal(["English"], imported.File.Languages); // nothing in the name: the clip's audio language, or the film's original language

            var state = factory.Services.GetRequiredService<MockDownloadClientState>();
            var torrent = state.All.Single(t => t.Hash == grab1.DownloadId);
            var seed = Path.Combine(state.ContentPath(torrent), FolderNameBuilder.Sanitize(torrent.Title) + ".mkv");
            Assert.True(File.Exists(seed), "the seed must keep its file");
            Assert.Equal(new FileInfo(seed).Length, imported.File.Size);
            // With ffmpeg around the mock writes a real clip and the import probes it; without it the file is filler and media info stays empty.
            Assert.Equal(new FfprobeMediaInfoReader(Options.Create(new MediaOptions()), NullLogger<FfprobeMediaInfoReader>.Instance).IsAvailable && ExecutableLocator.Find("ffmpeg") is not null, imported.File.HasMediaInfo);
            if (imported.File.MediaInfo is { } probed)
            {
                Assert.Equal("h264", probed.VideoCodec); // the release title says H.264
                Assert.Equal("AAC", probed.AudioCodec);
                Assert.Equal(320, probed.Width);
            }

            var importedEvents = await HistoryAsync(client, movie.Id, HistoryEventType.Imported);
            var entry = Assert.Single(importedEvents);
            Assert.Equal(webdl.Title, entry.SourceTitle);
            Assert.Equal(imported.File.Id, entry.MediaFileId);
            Assert.Equal("Hardlinked", entry.Data["transfer"]);
            Assert.Equal(Path.Combine(libraryFolder, imported.File.RelativePath), entry.Data["path"]);
            Assert.Equal("Mock import qbt", entry.Data["downloadClient"]);
            Assert.Equal(indexer.Id, entry.IndexerId);

            Assert.Empty((await RefreshAsync(client)).Items); // imported rows leave the queue

            // 2. Bluray-1080p is better: it replaces the file, the old one goes to the recycle bin.
            var bluray = search.Releases.Single(r => r.Title.EndsWith("1080p.BluRay.x264-SPARKS", StringComparison.Ordinal) && !r.Title.Contains("REPACK", StringComparison.Ordinal));
            await GrabAndCompleteAsync(client, bluray, version.Id);
            var upgraded = await WaitForFileAsync(client, movie.Id, version.Id, v => v.File!.Quality == "Bluray-1080p");
            Assert.Equal("The Matrix (1999) [Bluray-1080p].mkv", upgraded.File!.RelativePath);
            Assert.Equal(upgraded.File.RelativePath, Path.GetFileName(Assert.Single(Directory.GetFiles(libraryFolder))));
            Assert.True(File.Exists(Path.Combine(_bin, "The Matrix (1999)", "The Matrix (1999) [WEBDL-1080p].mkv")), "the replaced file goes to the recycle bin");

            var upgradedEvents = await HistoryAsync(client, movie.Id, HistoryEventType.Upgraded);
            var upgrade = Assert.Single(upgradedEvents);
            Assert.Equal("WEBDL-1080p", upgrade.Data["previousQuality"]);
            Assert.EndsWith("The Matrix (1999) [WEBDL-1080p].mkv", upgrade.Data["recycledTo"], StringComparison.Ordinal);

            // 3. A lower release grabbed on purpose is refused at import: visible, never blocklisted.
            var yify = search.Releases.Single(r => r.Title.Contains("720p.BluRay.x264-YIFY", StringComparison.Ordinal));
            var grab3 = await GrabAndCompleteAsync(client, yify, version.Id);
            var failed = await WaitForQueueRowAsync(client, r => r.GrabId == grab3.GrabId && r.State == GrabState.ImportFailed, "import failed");
            Assert.Contains(failed.StatusMessages, m => m.Title == ImportService.FailedMessageTitle && m.Messages[0].Contains("lower than the existing Bluray-1080p", StringComparison.Ordinal));
            Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.ImportFailed));
            Assert.Empty(await HistoryAsync(client, movie.Id, HistoryEventType.Blocklisted));
            Assert.Equal("Bluray-1080p", (await GetMovieAsync(client, movie.Id)).Versions.Single().File!.Quality);

            // 4. Manual import shows the verdict and lets the user force it.
            var listing = (await client.GetFromJsonAsync<ManualImportResponse>($"/api/v1/manualimport?grabId={grab3.GrabId}", Json))!;
            Assert.Equal(grab3.GrabId, listing.GrabId);
            Assert.Equal(yify.Title, listing.ReleaseTitle);
            Assert.Equal(2, listing.Files.Count);
            var main = listing.Files.Single(f => !f.IsSample);
            Assert.Equal(movie.Id, main.MovieId);
            Assert.Equal(version.Id, main.VersionId);
            Assert.Equal("Bluray-720p", main.Quality);
            var option = Assert.Single(main.Versions);
            Assert.False(option.Approved);
            Assert.Contains(option.Rejections, r => r.Contains("lower than the existing", StringComparison.Ordinal));

            var forced = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(main.Path, version.Id, GrabId: grab3.GrabId)]), Json);
            Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
            var result = Assert.Single((await forced.Content.ReadFromJsonAsync<List<ManualImportResultResponse>>(Json))!);
            Assert.True(result.Imported, result.Reason);
            Assert.True(result.Upgraded);

            var afterManual = (await GetMovieAsync(client, movie.Id)).Versions.Single();
            Assert.Equal("The Matrix (1999) [Bluray-720p].mkv", afterManual.File!.RelativePath);
            Assert.Empty((await RefreshAsync(client)).Items);
            Assert.Equal(2, (await HistoryAsync(client, movie.Id, HistoryEventType.Upgraded)).Count);
            Assert.Equal("true", (await HistoryAsync(client, movie.Id, HistoryEventType.Upgraded))[0].Data["manual"]);

            // A retry only makes sense for a failed import.
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/queue/{grab3.GrabId}/import", null)).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task An_import_interrupted_by_a_shutdown_is_picked_up_again_at_startup()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "interrupted"));
        await ConfigureAsync(client, _bin);

        var movie = await AddMovieAsync(client, 335984, hd, root.Id); // Blade Runner 2049
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock interrupted idx");
        var downloadClient = await AddMockClientAsync(client, "Mock interrupted qbt");
        try
        {
            var release = (await SearchAsync(client, movie.Id)).Releases.First(r => r.Title.Contains("1080p.WEB-DL", StringComparison.Ordinal));
            var grab = await GrabAsync(client, release, version.Id);
            await RefreshAsync(client);
            await CompleteAsync(client, grab.DownloadId!);

            // The process died while the worker held the grab: the row says importing and nobody is working on it.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                Assert.Equal(1, await db.Grabs.Where(g => g.Id == grab.GrabId).ExecuteUpdateAsync(s => s.SetProperty(g => g.State, GrabState.Importing)));
            }

            // Polling alone never leaves that state, and the retry action refuses it.
            Assert.Equal(GrabState.Importing, (await RefreshAsync(client)).Items.Single(i => i.GrabId == grab.GrabId).State);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/queue/{grab.GrabId}/import", null)).StatusCode);

            // What every start does before any worker runs.
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<InterruptedImports>().RunAsync(CancellationToken.None);
            }

            await RefreshAsync(client);
            var imported = await WaitForFileAsync(client, movie.Id, version.Id);
            Assert.Equal(VersionStatus.Available, imported.Status);
            await WaitOffQueueAsync(client, grab.GrabId!.Value);
            Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Imported));
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task Three_versions_live_side_by_side_and_the_one_shot_stops_monitoring_once_imported()
    {
        var client = await ClientAsync(factory);
        var (hd, uhd) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "side-by-side"));
        await ConfigureAsync(client, _bin);

        var movie = await AddMovieAsync(client, 27205, hd, root.Id); // Inception
        var main = movie.Versions.Single();
        var uhdVersion = await AddVersionAsync(client, movie.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd));
        var cut = await AddVersionAsync(client, movie.Id, new AddVersionRequest("directors-cut", VersionMode.OneShot, hd, Constraints: new ConstraintsDto("director'?s cut", null, null, null)));
        var indexer = await AddMockIndexerAsync(client, "Mock trio idx");
        var downloadClient = await AddMockClientAsync(client, "Mock trio qbt");
        try
        {
            var search = await SearchAsync(client, movie.Id);
            var bluray = search.Releases.Single(r => r.Title.EndsWith("1080p.BluRay.x264-SPARKS", StringComparison.Ordinal) && !r.Title.Contains("REPACK", StringComparison.Ordinal));
            var remux4k = search.Releases.Single(r => r.Title.Contains("2160p.UHD.BluRay.x265.HDR", StringComparison.Ordinal));
            var directors = search.Releases.Single(r => r.Title.Contains("Directors.Cut", StringComparison.Ordinal));
            Assert.True(directors.Versions.Single(v => v.VersionId == cut.Id).Approved);
            Assert.True(remux4k.Versions.Single(v => v.VersionId == uhdVersion.Id).Approved);

            await GrabAndCompleteAsync(client, bluray, main.Id);
            await GrabAndCompleteAsync(client, remux4k, uhdVersion.Id);
            await GrabAndCompleteAsync(client, directors, cut.Id);

            var mainFile = await WaitForFileAsync(client, movie.Id, main.Id);
            var uhdFile = await WaitForFileAsync(client, movie.Id, uhdVersion.Id);
            var cutFile = await WaitForFileAsync(client, movie.Id, cut.Id);

            var folder = Path.Combine(root.Path, "Inception (2010)");
            Assert.Equal("Inception (2010) [Bluray-1080p].mkv", mainFile.File!.RelativePath);
            Assert.Equal("Inception (2010) [Bluray-2160p].mkv", uhdFile.File!.RelativePath);
            Assert.StartsWith("Inception (2010) {edition-", cutFile.File!.RelativePath, StringComparison.Ordinal);
            Assert.EndsWith("[Bluray-1080p].mkv", cutFile.File.RelativePath, StringComparison.Ordinal);
            Assert.Equal(3, Directory.GetFiles(folder).Length);
            Assert.All(new[] { mainFile, uhdFile, cutFile }, v => Assert.Equal(folder, v.Path));

            // One shot: grabbed once, monitoring switched off at import (§3.3). The managed versions keep watching.
            Assert.False(cutFile.Monitored);
            Assert.True(mainFile.Monitored);
            Assert.True(uhdFile.Monitored);
            Assert.Equal(3, (await HistoryAsync(client, movie.Id, HistoryEventType.Imported)).Count);
            Assert.Empty((await RefreshAsync(client)).Items);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task A_torrent_added_by_hand_is_adopted_and_matched_by_title_or_left_to_the_manual_import()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "adopted"));
        await ConfigureAsync(client, _bin);

        var movie = await AddMovieAsync(client, 157336, hd, root.Id); // Interstellar
        var version = movie.Versions.Single();
        var downloadClient = await AddMockClientAsync(client, "Mock adopt");
        var state = factory.Services.GetRequiredService<MockDownloadClientState>();
        const string matched = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1";
        const string unknown = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2";
        try
        {
            // Completed in the client, never grabbed: adopted, matched by its title, imported.
            state.Add(matched, "Interstellar.2014.1080p.BluRay.x264-SPARKS", downloadClient.Category, 10L * 1024 * 1024 * 1024, paused: false);
            state.Complete(matched);
            var queue = await RefreshAsync(client);
            var row = Assert.Single(queue.Items, i => i.DownloadId == matched);
            Assert.True(row.Tracked);
            Assert.Equal(GrabState.ImportPending, row.State);
            Assert.Contains(row.StatusMessages, m => m.Title == "Added by hand");

            var imported = await WaitForFileAsync(client, movie.Id, version.Id);
            Assert.Equal("Interstellar (2014) [Bluray-1080p].mkv", imported.File!.RelativePath);
            var entry = Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Imported));
            Assert.Null(entry.IndexerName);
            Assert.Equal("Mock adopt", entry.Data["downloadClient"]);

            // Nothing in the library matches: import failed, the manual import says why and takes the user's choice.
            state.Add(unknown, "Some.Unknown.Film.2011.1080p.BluRay.x264-GRP", downloadClient.Category, 8L * 1024 * 1024 * 1024, paused: false);
            state.Complete(unknown);
            var failed = await WaitForQueueRowAsync(client, r => r.DownloadId == unknown && r.State == GrabState.ImportFailed, "unknown title");
            Assert.Contains(failed.StatusMessages, m => m.Title == ImportService.FailedMessageTitle && m.Messages[0].Contains("No movie in the library matches", StringComparison.Ordinal));

            var listing = (await client.GetFromJsonAsync<ManualImportResponse>($"/api/v1/manualimport?grabId={failed.GrabId}", Json))!;
            var file = listing.Files.Single(f => !f.IsSample);
            Assert.Null(file.MovieId);
            Assert.NotNull(file.MatchReason);
            Assert.Empty(file.Versions);
            Assert.Equal("Some Unknown Film", file.ParsedTitle);

            // The user knows better: it goes to a one-shot version of Interstellar, with a quality picked by hand so the names do not collide.
            var extra = await AddVersionAsync(client, movie.Id, new AddVersionRequest("extra", VersionMode.OneShot, hd));
            var response = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(file.Path, extra.Id, Bigarr.Parsing.Qualities.Quality.WEBDL1080p.Id, failed.GrabId)]), Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = Assert.Single((await response.Content.ReadFromJsonAsync<List<ManualImportResultResponse>>(Json))!);
            Assert.True(result.Imported, result.Reason);

            var refreshed = await GetMovieAsync(client, movie.Id);
            var extraVersion = refreshed.Versions.Single(v => v.Id == extra.Id);
            Assert.Equal("Interstellar (2014) [WEBDL-1080p].mkv", extraVersion.File!.RelativePath);
            Assert.False(extraVersion.Monitored);
            Assert.Empty((await RefreshAsync(client)).Items);
        }
        finally
        {
            state.Remove(matched, deleteFiles: true);
            state.Remove(unknown, deleteFiles: true);
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    private static async Task ConfigureAsync(HttpClient client, string bin)
    {
        var response = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, bin, 7), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
