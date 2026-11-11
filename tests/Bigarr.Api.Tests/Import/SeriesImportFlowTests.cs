using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Downloads.Mock;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Import;

/// <summary>
/// S5's acceptance on the mock client: a season pack grabbed, every episode imported and renamed without breaking the
/// seed; a pack importing only the episodes it improves; a better single replacing one episode; a multi-episode file;
/// a pack partly imported, finished by hand; a scan's unmatched file assigned in place; rename; delete through the bin;
/// anime absolute and daily naming.
/// </summary>
public sealed class SeriesImportFlowTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>, IDisposable
{
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-series-bin-").FullName;

    public void Dispose() => TryDelete(_bin);

    [Fact]
    public async Task A_season_pack_is_imported_episode_by_episode_and_upgrades_only_what_it_improves()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var downloadClient = await MockClientAsync(client);
        var version = series.Versions.Single();
        var folder = Path.Combine(version.RootFolderPath, version.FolderName);
        Assert.Equal("Breaking Bad (2008)", version.FolderName);

        try
        {
            var season = await SeriesSearchTests.SearchAsync(client, series.Id, "season=1");

            // 1. A single WEB-DL of S01E03, then a Bluray PROPER of S01E05 (crafted: the user grabbed it by hand).
            var e03 = season.Releases.Single(r => r.Title == "Breaking.Bad.S01E03.1080p.WEB-DL.DDP5.1.H.264-NTb");
            await GrabAndCompleteAsync(client, SeriesSetup.Grab(e03));
            await WaitForFilesAsync(client, series.Id, f => f.Any(x => x.Episodes == "S01E03"));
            var e05Id = (await SeriesSetup.EpisodeAsync(client, series.Id, 1, 5)).Id;
            await GrabAndCompleteAsync(client, Crafted("Breaking.Bad.S01E05.PROPER.1080p.BluRay.x264-DEMAND", version.Id, e05Id));
            await WaitForFilesAsync(client, series.Id, f => f.Any(x => x.Episodes == "S01E05"));

            // 2. The Bluray pack: seven files, a sample left behind. E05's proper is better than the pack's: skipped;
            //    E03's WEB-DL is upgraded; the other five are new.
            var pack = season.Releases.Single(r => r.Title == "Breaking.Bad.S01.1080p.BluRay.x264-DEMAND");
            var grab = await GrabAsync(client, SeriesSetup.Grab(pack));
            var queued = (await RefreshAsync(client)).Items.Single(i => i.GrabId == grab.GrabId);
            Assert.True(queued.Series);
            Assert.Equal(series.Id, queued.SeriesId);
            Assert.Equal("Breaking Bad", queued.SeriesTitle);
            Assert.Equal(7, queued.Episodes.Count);
            Assert.Equal(version.Id, queued.VersionId);
            Assert.True(queued.Episodes.Single(e => e.EpisodeNumber == 3).HasFile);

            await CompleteAsync(client, grab.DownloadId!);
            await RefreshAsync(client);
            var files = await WaitForFilesAsync(client, series.Id, f => f.Count(x => x.SeasonNumber == 1) == 7 && f.Single(x => x.Episodes == "S01E03").Quality == "Bluray-1080p");

            Assert.Equal("Season 1/Breaking Bad - S01E01 - Pilot Bluray-1080p.mkv", files.Single(f => f.Episodes == "S01E01").RelativePath);
            Assert.Equal("Season 1/Breaking Bad - S01E03 - ...And the Bag's in the River Bluray-1080p.mkv", files.Single(f => f.Episodes == "S01E03").RelativePath);
            var e05 = files.Single(f => f.Episodes == "S01E05");
            Assert.Equal("Season 1/Breaking Bad - S01E05 - Gray Matter Bluray-1080p Proper.mkv", e05.RelativePath);
            Assert.Equal(2, e05.RevisionVersion);
            Assert.All(files, f => Assert.True(File.Exists(Path.Combine(folder, f.RelativePath)), f.RelativePath));

            // The subtitles came along, named after their episode; the replaced WEB-DL went to the recycle bin.
            Assert.True(File.Exists(Path.Combine(folder, "Season 1", "Breaking Bad - S01E01 - Pilot Bluray-1080p.en.srt")));
            Assert.True(File.Exists(Path.Combine(_bin, "Breaking Bad (2008)", "Season 1", "Breaking Bad - S01E03 - ...And the Bag's in the River WEBDL-1080p.mkv")));

            // Hardlinked: the torrent keeps seeding the very same bytes.
            var state = factory.Services.GetRequiredService<MockDownloadClientState>();
            var torrent = state.All.Single(t => t.Hash == grab.DownloadId);
            var seed = Path.Combine(state.ContentPath(torrent), "Breaking.Bad.S01E02.1080p.BluRay.x264-DEMAND.mkv");
            Assert.True(File.Exists(seed), "the seed must keep its files");
            var library = Path.Combine(folder, files.Single(f => f.Episodes == "S01E02").RelativePath);
            var before = new FileInfo(library).Length;
            await File.AppendAllTextAsync(seed, "x");
            Assert.Equal(before + 1, new FileInfo(library).Length);

            // Journal: imported per new episode, upgraded for E03 with the previous quality, nothing for the skipped E05.
            var history = await SeriesHistoryAsync(client, series.Id);
            var fromPack = history.Where(h => h.SourceTitle?.StartsWith("Breaking.Bad.S01E", StringComparison.Ordinal) == true && h.Data.GetValueOrDefault("downloadId") == grab.DownloadId).ToList();
            Assert.Equal(5, fromPack.Count(h => h.EventType == HistoryEventType.Imported));
            var upgrade = Assert.Single(fromPack, h => h.EventType == HistoryEventType.Upgraded);
            Assert.Equal("S01E03", upgrade.Data["episode"]);
            Assert.Equal("WEBDL-1080p", upgrade.Data["previousQuality"]);
            Assert.Equal("Hardlinked", upgrade.Data["transfer"]);
            Assert.Equal(7, history.Count(h => h.EventType == HistoryEventType.Grabbed && h.SourceTitle == pack.Title));
            Assert.DoesNotContain((await RefreshAsync(client)).Items, i => i.GrabId == grab.GrabId); // imported: off the queue

            // 3. A better single replaces one episode.
            var e02Id = (await SeriesSetup.EpisodeAsync(client, series.Id, 1, 2)).Id;
            await GrabAndCompleteAsync(client, Crafted("Breaking.Bad.S01E02.REPACK.1080p.BluRay.x264-DEMAND", version.Id, e02Id));
            files = await WaitForFilesAsync(client, series.Id, f => f.Single(x => x.Episodes == "S01E02").RevisionVersion == 2);
            Assert.Equal("Season 1/Breaking Bad - S01E02 - Cat's in the Bag... Bluray-1080p Proper.mkv", files.Single(f => f.Episodes == "S01E02").RelativePath);
            Assert.Equal(7, files.Count(f => f.SeasonNumber == 1));
            Assert.Equal(1, files.Single(f => f.Episodes == "S01E01").RevisionVersion);

            // 4. A multi-episode file: one file for two episodes, named by the multi-episode style.
            var s2e1 = (await SeriesSetup.EpisodeAsync(client, series.Id, 2, 1)).Id;
            var s2e2 = (await SeriesSetup.EpisodeAsync(client, series.Id, 2, 2)).Id;
            await GrabAndCompleteAsync(client, Crafted("Breaking.Bad.S02E01E02.1080p.BluRay.x264-DEMAND", version.Id, s2e1, s2e2));
            files = await WaitForFilesAsync(client, series.Id, f => f.Any(x => x.SeasonNumber == 2));
            var multi = Assert.Single(files, f => f.SeasonNumber == 2);
            Assert.Equal("Season 2/Breaking Bad - S02E01-E02 - Seven Thirty-Seven + Grilled Bluray-1080p.mkv", multi.RelativePath);
            Assert.Equal([s2e1, s2e2], multi.EpisodeIds.Order().ToArray());

            // 5. A pack with one file nobody can place: the rest goes in, the grab waits for a hand.
            var s2e3 = (await SeriesSetup.EpisodeAsync(client, series.Id, 2, 3)).Id;
            var s2e4 = (await SeriesSetup.EpisodeAsync(client, series.Id, 2, 4)).Id;
            var partial = await GrabAsync(client, Crafted("Breaking.Bad.S02.720p.WEB-DL.x264-PARTIAL", version.Id, s2e3, s2e4) with { Size = 1024 });
            await RefreshAsync(client);
            await CompleteAsync(client, partial.DownloadId!);
            var partialTorrent = state.All.Single(t => t.Hash == partial.DownloadId);
            state.Materialize(partialTorrent);
            var stray = Path.Combine(state.ContentPath(partialTorrent), "Bonus.Feature.mkv");
            await File.WriteAllBytesAsync(stray, new byte[2048]);
            await RefreshAsync(client);
            var failed = await WaitForQueueRowAsync(client, r => r.GrabId == partial.GrabId && r.State == GrabState.ImportFailed, "pack partly imported");
            var message = Assert.Single(failed.StatusMessages, m => m.Title == ImportService.FailedMessageTitle);
            Assert.Equal("2 of 3 files imported; 1 need a manual import.", message.Messages[0]);
            Assert.Contains(message.Messages, m => m.StartsWith("Bonus.Feature.mkv: Not recognised", StringComparison.Ordinal) && m.EndsWith("Import it by hand to choose its episodes.", StringComparison.Ordinal));
            files = await WaitForFilesAsync(client, series.Id, f => f.Any(x => x.Episodes == "S02E04"));
            Assert.Contains(files, f => f.Episodes == "S02E03" && f.Quality == "WEBDL-720p");

            // The manual import lists the queue item's files with the mapper's proposal; the stray one has none.
            var listing = (await client.GetFromJsonAsync<SeriesManualImportResponse>($"/api/v1/manualimport/series?grabId={partial.GrabId}", Json))!;
            var bonus = listing.Files.Single(f => f.Name == "Bonus.Feature.mkv");
            Assert.Equal(series.Id, bonus.SeriesId);
            Assert.Empty(bonus.Episodes);
            Assert.NotNull(bonus.MatchReason);
            var proposed = listing.Files.Single(f => f.Name.Contains("S02E03", StringComparison.Ordinal));
            Assert.Equal(s2e3, Assert.Single(proposed.Episodes).Id);
            Assert.True(proposed.Episodes[0].HasFile);

            var s2e5 = (await SeriesSetup.EpisodeAsync(client, series.Id, 2, 5)).Id;
            var forced = await client.PostAsJsonAsync("/api/v1/manualimport/series", new SeriesManualImportRequest([new SeriesManualImportItemRequest(bonus.Path, version.Id, [s2e5], GrabId: partial.GrabId)]), Json);
            Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
            var result = Assert.Single((await forced.Content.ReadFromJsonAsync<List<SeriesManualImportResultResponse>>(Json))!);
            Assert.True(result.Imported, result.Reason);
            Assert.EndsWith("Season 2/Breaking Bad - S02E05 - Breakage WEBDL-720p.mkv", result.DestinationPath, StringComparison.Ordinal);
            Assert.DoesNotContain((await RefreshAsync(client)).Items, i => i.GrabId == partial.GrabId); // done by hand: imported

            // 6. A file dropped in the folder by hand: the scan lists it unmatched, assigning it places it (and renames it).
            var dropped = Path.Combine(folder, "mystery cut.mkv");
            await File.WriteAllBytesAsync(dropped, new byte[4096]);
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/series/{series.Id}/rescan", null)).StatusCode);
            var unmatched = await WaitUntilAsync(async () =>
                (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Unmatched.FirstOrDefault(u => u.RelativePath == "mystery cut.mkv"), "the unmatched file");
            var s2e6 = (await SeriesSetup.EpisodeAsync(client, series.Id, 2, 6)).Id;
            var assigned = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/unmatched/{unmatched.Id}/assign", new AssignUnmatchedRequest([s2e6], Rename: true, QualityId: 4), Json);
            Assert.True(assigned.StatusCode == HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
            var placed = (await assigned.Content.ReadFromJsonAsync<EpisodeFileDetailResponse>(Json))!;
            Assert.Equal("Season 2/Breaking Bad - S02E06 - Peekaboo HDTV-720p.mkv", placed.RelativePath);
            Assert.False(File.Exists(dropped));
            Assert.True(File.Exists(placed.Path));

            // Assigning onto an episode that has a file is refused unless replaced.
            await File.WriteAllBytesAsync(Path.Combine(folder, "again.mkv"), new byte[10]);
            await client.PostAsync($"/api/v1/series/{series.Id}/rescan", null);
            var again = await WaitUntilAsync(async () =>
                (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Unmatched.FirstOrDefault(u => u.RelativePath == "again.mkv"), "the second unmatched file");
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/unmatched/{again.Id}/assign", new AssignUnmatchedRequest([s2e6]), Json)).StatusCode);

            // 7. Rename: a new template, previewed per season, applied; extras follow.
            await NamingAsync(client, n => n with { StandardEpisodeTemplate = "{Series.Title}.S{season:00}E{episode:00}.{Quality.Full}" });
            var preview = (await client.GetFromJsonAsync<List<SeriesRenamePreviewResponse>>($"/api/v1/series/{series.Id}/rename?seasonNumber=1", Json))!;
            Assert.Equal(7, preview.Count);
            var p1 = preview.Single(p => p.Episodes.SequenceEqual(["S01E01"]));
            Assert.True(p1.Changed);
            Assert.Equal("Season 1/Breaking.Bad.S01E01.Bluray-1080p.mkv", p1.NewRelativePath);
            Assert.Equal("Season 1/Breaking.Bad.S01E01.Bluray-1080p.en.srt", Assert.Single(p1.Extras).NewRelativePath);
            var renamed = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/rename", new SeriesRenameRequest(series.Id, SeasonNumber: 1), Json);
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            Assert.All((await renamed.Content.ReadFromJsonAsync<List<SeriesRenameResultResponse>>(Json))!, r => Assert.True(r.Renamed, r.Error));
            Assert.True(File.Exists(Path.Combine(folder, "Season 1", "Breaking.Bad.S01E01.Bluray-1080p.en.srt")));
            Assert.Equal(7, (await SeriesHistoryAsync(client, series.Id)).Count(h => h.EventType == HistoryEventType.Renamed && h.Data["episode"].StartsWith("S01", StringComparison.Ordinal)));
            await NamingAsync(client, n => n with { StandardEpisodeTemplate = MediaManagementConfig.DefaultStandardEpisodeTemplate });

            // 8. Delete an episode file: to the recycle bin with its subtitle, the episode is missing again.
            files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Files;
            var doomed = files.Single(f => f.Episodes == "S01E01");
            var info = (await client.GetFromJsonAsync<EpisodeFileDetailResponse>($"/api/v1/series/{series.Id}/episodefiles/{doomed.Id}", Json))!;
            Assert.Single(info.Extras);
            Assert.Equal("Pilot", Assert.Single(info.Episodes).Title);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/series/{series.Id}/episodefiles/{doomed.Id}")).StatusCode);
            Assert.True(File.Exists(Path.Combine(_bin, "Breaking Bad (2008)", "Season 1", "Breaking.Bad.S01E01.Bluray-1080p.mkv")));
            Assert.True(File.Exists(Path.Combine(_bin, "Breaking Bad (2008)", "Season 1", "Breaking.Bad.S01E01.Bluray-1080p.en.srt")));
            var pilot = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 1);
            Assert.False(pilot.Versions.Single().HasFile);
            Assert.Contains(await SeriesHistoryAsync(client, series.Id), h => h.EventType == HistoryEventType.FileDeleted && h.Data.GetValueOrDefault("episode") == "S01E01");
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    [Fact]
    public async Task Anime_files_take_the_absolute_number_and_daily_files_the_air_date()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        await SeriesSetup.MockIndexerAsync(client);
        var downloadClient = await MockClientAsync(client);
        await NamingAsync(client, n => n with { AnimeEpisodeTemplate = "{Series TitleYear} - S{season:00}E{episode:00} - {absolute:000} - {Episode Title} [{Quality Full}]{-Release Group}" });
        try
        {
            var frieren = await SeriesSetup.AddAsync(client, SeriesSearchTests.Frieren);
            var s2e05 = await SeriesSetup.EpisodeAsync(client, frieren.Id, 2, 5);
            var anime = await SeriesSearchTests.SearchAsync(client, frieren.Id, $"episodeId={s2e05.Id}");
            var release = anime.Releases.First(r => r.Title.StartsWith("[SubsPlease]", StringComparison.Ordinal) && r.Title.Contains($"- {s2e05.AbsoluteEpisodeNumber} (1080p)", StringComparison.Ordinal));
            // SubsPlease names no source: Sonarr's parser reads HDTV, and so does the file name.
            await GrabAndCompleteAsync(client, SeriesSetup.Grab(release));
            var animeFile = Assert.Single(await WaitForFilesAsync(client, frieren.Id, f => f.Count > 0));
            Assert.Equal("Season 2/Frieren - Beyond Journey's End (2023) - S02E05 - 033 - Logistics in the Northern Plateau [HDTV-1080p]-SubsPlease.mkv", animeFile.RelativePath);

            var daily = await SeriesSetup.AddAsync(client, SeriesSearchTests.DailyShow);
            var day = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{daily.Id}/episodes", Json))!.Single(e => e.AirDate == new DateOnly(2026, 10, 27));
            var byDate = await SeriesSearchTests.SearchAsync(client, daily.Id, $"episodeId={day.Id}");
            await GrabAndCompleteAsync(client, SeriesSetup.Grab(byDate.Releases.Single(r => r.Title == "The.Daily.Show.2026.10.27.1080p.WEB.h264-EDITH")));
            var dailyFile = Assert.Single(await WaitForFilesAsync(client, daily.Id, f => f.Count > 0));
            Assert.Equal("Season 31/The Daily Show - 2026-10-27 - Morgana Robinson WEBDL-1080p.mkv", dailyFile.RelativePath);
        }
        finally
        {
            await NamingAsync(client, n => n with { AnimeEpisodeTemplate = MediaManagementConfig.DefaultAnimeEpisodeTemplate });
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    [Fact]
    public async Task A_torrent_added_by_hand_to_the_series_category_is_adopted_and_imported()
    {
        var client = await ClientAsync(factory);
        await ConfigureAsync(client, _bin);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var downloadClient = await MockClientAsync(client);
        var state = factory.Services.GetRequiredService<MockDownloadClientState>();
        const string hash = "abababababababababababababababababab0001";
        try
        {
            state.Add(hash, "Breaking.Bad.S03E01.1080p.BluRay.x264-DEMAND", downloadClient.SeriesCategory, 2L * 1024 * 1024 * 1024, paused: false);
            var untracked = (await RefreshAsync(client)).Items.Single(i => i.DownloadId == hash);
            Assert.True(untracked.Series);
            Assert.False(untracked.Tracked);

            state.Complete(hash);
            await RefreshAsync(client);
            var files = await WaitForFilesAsync(client, series.Id, f => f.Any(x => x.Episodes == "S03E01"));
            Assert.StartsWith("Season 3/Breaking Bad - S03E01 - ", files.Single(f => f.Episodes == "S03E01").RelativePath, StringComparison.Ordinal);
        }
        finally
        {
            state.Remove(hash);
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    [Fact]
    public async Task Series_naming_is_validated_and_previewed_and_new_versions_use_the_folder_template()
    {
        var client = await ClientAsync(factory);
        var current = (await client.GetFromJsonAsync<SeriesNamingResponse>("/api/v1/mediamanagement/series-naming", Json))!;
        Assert.Equal(MediaManagementConfig.DefaultStandardEpisodeTemplate, current.StandardEpisodeTemplate);
        Assert.Equal(MultiEpisodeStyle.PrefixedRange, current.MultiEpisodeStyle);
        Assert.Equal("The Series Title's! - S01E01-E02 - Episode Title HDTV-720p Proper.mkv", current.Preview.MultiEpisode);

        var bad = await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", Request(current) with { StandardEpisodeTemplate = "{Series Title} {Quality Full}" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("must contain {season} and {episode}", await bad.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var preview = (await (await client.PostAsJsonAsync("/api/v1/mediamanagement/series-naming/preview", new SeriesNamingPreviewRequest(SeriesFolderTemplate: "{Series Title} {tvdb-{TvdbId}} [{Version}]", StandardEpisodeTemplate: "{Series Title} {season}x{episode:00}", MultiEpisodeStyle: MultiEpisodeStyle.Extend), Json)).Content.ReadFromJsonAsync<SeriesNamingPreviewResponse>(Json))!;
        Assert.Equal("The Series Title's! {tvdb-1520211} [1080p]", preview.SeriesFolder);
        Assert.Equal("The Series Title's! 1x01-02.mkv", preview.MultiEpisode);
        Assert.Contains(preview.Errors["standardEpisode"], e => e.Contains("{Quality Full}", StringComparison.Ordinal));

        // Added before the template changes: the series keeps its folder, only the new version takes the template.
        var series = await SeriesSetup.AddAsync(client, 81189);
        await NamingAsync(client, n => n with { SeriesFolderTemplate = "{Series Title} {tvdb-{TvdbId}} [{Version}]" });
        try
        {
            var added = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions", new AddSeriesVersionRequest("folder-test", series.Versions.Single().QualityProfileId), Json);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var version = (await added.Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!;
            Assert.Equal("Breaking Bad {tvdb-81189} [folder-test]", version.FolderName);

            // Deleting the version with its files: its folder (its own) goes to the recycle bin.
            await ConfigureAsync(client, _bin);
            var own = Path.Combine(version.RootFolderPath, version.FolderName);
            Directory.CreateDirectory(Path.Combine(own, "Season 1"));
            await File.WriteAllBytesAsync(Path.Combine(own, "Season 1", "x.mkv"), new byte[8]);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/series/{series.Id}/versions/{version.Id}?deleteFiles=true")).StatusCode);
            Assert.False(Directory.Exists(own));
            Assert.True(File.Exists(Path.Combine(_bin, version.FolderName, "Season 1", "x.mkv")));
        }
        finally
        {
            await NamingAsync(client, n => n with { SeriesFolderTemplate = MediaManagementConfig.DefaultSeriesFolderTemplate });
        }
    }

    // ---- helpers --------------------------------------------------------------------------------------------------------

    private static SeriesNamingRequest Request(SeriesNamingResponse n) => new(
        n.RenameEpisodes, n.SeriesFolderTemplate, n.SeasonFolderTemplate, n.SpecialsFolderName, n.StandardEpisodeTemplate, n.DailyEpisodeTemplate, n.AnimeEpisodeTemplate, n.MultiEpisodeStyle);

    private static async Task NamingAsync(HttpClient client, Func<SeriesNamingRequest, SeriesNamingRequest> change)
    {
        var current = (await client.GetFromJsonAsync<SeriesNamingResponse>("/api/v1/mediamanagement/series-naming", Json))!;
        var response = await client.PutAsJsonAsync("/api/v1/mediamanagement/series-naming", change(Request(current)), Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task ConfigureAsync(HttpClient client, string bin)
    {
        var response = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, bin, 7), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<DownloadClientResponse> MockClientAsync(HttpClient client)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest($"Mock series {id}", DownloadClientImplementation.Mock, Category: $"films-{id}", SeriesCategory: $"tv-{id}"), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
    }

    /// <summary>A release the user grabs by hand (the interactive grab does not judge it again).</summary>
    private static GrabRequest Crafted(string title, int versionId, params int[] episodeIds)
    {
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes("crafted/" + title)));
        return new GrabRequest(versionId, 0, "crafted/" + title, title, InfoHash: hash, Size: 2L * 1024 * 1024 * 1024, Indexer: "by hand", EpisodeIds: [.. episodeIds]);
    }

    private static async Task<GrabResponse> GrabAsync(HttpClient client, GrabRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/grab", request, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
    }

    private static async Task<GrabResponse> GrabAndCompleteAsync(HttpClient client, GrabRequest request)
    {
        var grab = await GrabAsync(client, request);
        Assert.NotNull(grab.DownloadId);
        await RefreshAsync(client);
        await CompleteAsync(client, grab.DownloadId!);
        await RefreshAsync(client);
        return grab;
    }

    private static async Task<List<EpisodeFileResponse>> WaitForFilesAsync(HttpClient client, int seriesId, Func<List<EpisodeFileResponse>, bool> until)
    {
        var deadline = DateTime.UtcNow + ImportTimeout;
        while (true)
        {
            var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{seriesId}/files", Json))!.Files;
            if (until(files))
            {
                return files;
            }

            if (DateTime.UtcNow > deadline)
            {
                var queue = await RefreshAsync(client);
                throw new TimeoutException($"Files of series {seriesId}: {string.Join(" | ", files.Select(f => $"{f.Episodes} {f.Quality} {f.RelativePath}"))}. Queue: {string.Join(" | ", queue.Items.Select(i => $"{i.Title} {i.State} [{string.Join("; ", i.StatusMessages.Select(m => m.Title + ": " + string.Join(" ", m.Messages)))}]"))}");
            }

            await Task.Delay(100);
        }
    }

    private static Task<List<HistoryEventResponse>> SeriesHistoryAsync(HttpClient client, int seriesId) =>
        client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?seriesId={seriesId}&limit=1000", Json)!;
}
