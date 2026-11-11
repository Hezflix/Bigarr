using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// S6's acceptance on the fixtures: Breaking Bad switched to its TMDB DVD group (season 5 split in two) — preview, apply
/// with real files (a hardlinked seed keeps its bytes, subtitles follow), undo restores every path; a special the group
/// lacks stays in place and is fixed by hand; the search falls back on the order and holds what it cannot confirm.
/// </summary>
public sealed class SeriesOrderFlowTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>, IDisposable
{
    private const string DvdGroup = "69f50757054263b7bc87e32a";

    private readonly string _seeds = Directory.CreateTempSubdirectory("bigarr-order-seed-").FullName;

    public void Dispose() => TryDelete(_seeds);

    [Fact]
    public async Task Switching_to_a_tmdb_group_previews_applies_and_undoes_the_renames_on_disk()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.Single();
        var folder = Path.Combine(version.RootFolderPath, version.FolderName);
        var e08 = await FileAsync(series.Id, version.Id, folder, 5, 8, "Season 5/Breaking Bad - S05E08 - Gliding Over All Bluray-1080p.mkv");
        var e09 = await FileAsync(series.Id, version.Id, folder, 5, 9, "Season 5/Breaking Bad - S05E09 - Blood Money Bluray-1080p.mkv", subtitle: true);
        var special = await FileAsync(series.Id, version.Id, folder, 0, 1, "Specials/Breaking Bad - S00E01 - Good Cop Bad Cop Bluray-1080p.mkv");

        // The torrent's copy, hardlinked to the library file (as an import leaves it).
        var seed = Path.Combine(_seeds, "Breaking.Bad.S05E09.1080p.BluRay.x264-GRP.mkv");
        Link(Path.Combine(folder, e09), seed);

        var orders = (await client.GetFromJsonAsync<SeriesOrdersResponse>($"/api/v1/series/{series.Id}/orders", Json))!;
        Assert.Equal(EpisodeOrder.Tvdb, orders.Current.Ordering);
        var dvd = Assert.Single(orders.Groups);
        Assert.Equal((DvdGroup, "DVD / PVOD", EpisodeGroupType.Dvd), (dvd.Id, dvd.Name, dvd.Type));

        // 1. Preview: nothing moves yet.
        var request = new OrderChoiceRequest(EpisodeOrder.TmdbEpisodeGroup, DvdGroup);
        var preview = await PostAsync<OrderPreviewResponse>(client, $"/api/v1/series/{series.Id}/orders/preview", request);
        Assert.Equal("DVD / PVOD", preview.To.EpisodeGroupName);
        Assert.Equal(8, preview.EpisodesMoving); // TVDB S05E09–E16 become S06E01–E08
        Assert.Contains(preview.EpisodeChanges, c => c.Tvdb == "S05E09" && c.From == "S05E09" && c.To == "S06E01");
        Assert.Equal(preview.Episodes - 62, preview.EpisodesNotInOrder); // the specials
        Assert.Empty(preview.OrderOnly);
        Assert.Equal((3, 1, 1, 1, 0), (preview.Files, preview.FilesMoving, preview.FilesUnchanged, preview.FilesUnmatched, preview.FilesConflicting));
        Assert.False(preview.LargeChange);
        var move = preview.FileChanges.Single(f => f.Status == OrderFileStatus.Move);
        Assert.Equal((e09, "Season 6/Breaking Bad - S06E01 - Blood Money Bluray-1080p.mkv"), (move.From, move.To));
        var flagged = preview.FileChanges.Single(f => f.Status == OrderFileStatus.Unmatched);
        Assert.Equal(special, flagged.From);
        Assert.Contains("S00E01", flagged.Detail, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, e09)));
        Assert.Equal(EpisodeOrder.Tvdb, (await GetSeriesAsync(client, series.Id)).Ordering);

        // 2. Apply: the file moves to its DVD season, its subtitles with it; the seed keeps the same bytes (same inode).
        var applied = await PostAsync<OrderApplyResponse>(client, $"/api/v1/series/{series.Id}/orders/apply", request);
        Assert.True(applied.Change.CanUndo);
        Assert.Equal((1, 1), (applied.Change.FilesMoved, applied.Change.UnmatchedFiles));
        var moved = Path.Combine(folder, "Season 6/Breaking Bad - S06E01 - Blood Money Bluray-1080p.mkv");
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(Path.Combine(folder, e09)));
        Assert.True(File.Exists(Path.Combine(folder, "Season 6/Breaking Bad - S06E01 - Blood Money Bluray-1080p.en.srt")));
        Assert.True(File.Exists(Path.Combine(folder, e08)));
        Assert.True(File.Exists(Path.Combine(folder, special)));
        await File.AppendAllTextAsync(seed, "x");
        Assert.Equal(new FileInfo(seed).Length, new FileInfo(moved).Length);

        var after = await GetSeriesAsync(client, series.Id);
        Assert.Equal((EpisodeOrder.TmdbEpisodeGroup, DvdGroup, EpisodeGroupType.Dvd), (after.Ordering, after.EpisodeGroupId, after.EpisodeGroupType));
        var episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        var tvdb9 = episodes.Single(e => e.SeasonNumber == 5 && e.EpisodeNumber == 9);
        Assert.Equal((6, 1, 6, 1), (tvdb9.DisplaySeasonNumber, tvdb9.DisplayEpisodeNumber, tvdb9.OrderSeasonNumber, tvdb9.OrderEpisodeNumber));
        Assert.True(episodes.Single(e => e.SeasonNumber == 0 && e.EpisodeNumber == 1).NotInOrder);
        Assert.False(tvdb9.NotInOrder);

        // The files list knows the new path; the journal has the rename.
        var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Files;
        Assert.Contains(files, f => f.RelativePath == "Season 6/Breaking Bad - S06E01 - Blood Money Bluray-1080p.mkv");
        var history = (await client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?seriesId={series.Id}&limit=100", Json))!;
        Assert.Contains(history, h => h.EventType == HistoryEventType.Renamed && h.Data.GetValueOrDefault("to")?.EndsWith("S06E01 - Blood Money Bluray-1080p.mkv", StringComparison.Ordinal) == true);

        // A refresh re-reads both numberings: the order's numbers stay current, nothing moves on disk.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/series/{series.Id}/refresh", null)).StatusCode);
        var refreshed = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!.Single(e => e.Id == tvdb9.Id);
        Assert.Equal((6, 1, 5, 9), (refreshed.OrderSeasonNumber, refreshed.OrderEpisodeNumber, refreshed.SeasonNumber, refreshed.EpisodeNumber));
        Assert.True(File.Exists(moved));

        // Applying the same order again is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/orders/apply", request, Json)).StatusCode);

        // 3. Undo: every path back, the subtitle too, TVDB order and numbers restored; once only.
        var undone = await PostAsync<OrderUndoResponse>(client, $"/api/v1/series/{series.Id}/orders/undo", null);
        Assert.Equal(1, undone.FilesRestored);
        Assert.False(undone.Change.CanUndo);
        Assert.True(File.Exists(Path.Combine(folder, e09)));
        Assert.True(File.Exists(Path.Combine(folder, Path.ChangeExtension(e09, ".en.srt"))));
        Assert.False(Directory.Exists(Path.Combine(folder, "Season 6")));
        await File.AppendAllTextAsync(seed, "y");
        Assert.Equal(new FileInfo(seed).Length, new FileInfo(Path.Combine(folder, e09)).Length);
        var back = await GetSeriesAsync(client, series.Id);
        Assert.Equal(EpisodeOrder.Tvdb, back.Ordering);
        Assert.Null(back.EpisodeGroupId);
        episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        Assert.All(episodes, e => Assert.Null(e.OrderSeasonNumber));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/series/{series.Id}/orders/undo", null)).StatusCode);
        var last = (await client.GetFromJsonAsync<SeriesOrdersResponse>($"/api/v1/series/{series.Id}/orders", Json))!.LastChange!;
        Assert.NotNull(last.UndoneAt);

        // 4. Again in the DVD order: the special's file stays flagged; the manual fix gives it the episodes the user picks
        //    (here TVDB S05E10, the DVD S06E02) and renames it after them.
        await PostAsync<OrderApplyResponse>(client, $"/api/v1/series/{series.Id}/orders/apply", request);
        var specialFile = files.Single(f => f.RelativePath == special);
        var e10 = (await SeriesSetup.EpisodeAsync(client, series.Id, 5, 10)).Id;
        var fixedFile = await PostAsync<OrderFileResult>(client, $"/api/v1/series/{series.Id}/episodefiles/{specialFile.Id}/reassign", new ReassignEpisodeFileRequest([e10]));
        Assert.True(fixedFile.Moved, fixedFile.Error);
        Assert.Equal("Season 6/Breaking Bad - S06E02 - Buried Bluray-1080p.mkv", fixedFile.To);
        Assert.True(File.Exists(Path.Combine(folder, fixedFile.To)));
        episodes = (await client.GetFromJsonAsync<List<EpisodeResponse>>($"/api/v1/series/{series.Id}/episodes", Json))!;
        Assert.True(episodes.Single(e => e.Id == e10).Versions.Single().HasFile);
        Assert.False(episodes.Single(e => e.SeasonNumber == 0 && e.EpisodeNumber == 1).Versions.Single().HasFile);
        await PostAsync<OrderUndoResponse>(client, $"/api/v1/series/{series.Id}/orders/undo", null);
    }

    [Fact]
    public async Task The_search_falls_back_on_the_order_when_tvdb_finds_nothing_acceptable_and_holds_an_unconfirmed_hit()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var version = series.Versions.Single();
        var request = new OrderChoiceRequest(EpisodeOrder.TmdbEpisodeGroup, DvdGroup);
        await PostAsync<OrderApplyResponse>(client, $"/api/v1/series/{series.Id}/orders/apply", request);
        var e14 = await SeriesSetup.EpisodeAsync(client, series.Id, 5, 14); // DVD S06E06

        try
        {
            // With TVDB's releases acceptable, no fallback.
            var plain = await SeriesSearchTests.SearchAsync(client, series.Id, $"episodeId={e14.Id}");
            Assert.False(plain.OrderFallback, string.Join("\n", plain.Releases.Select(r => r.Title + ": " + string.Join(" | ", r.Rejections.Concat(r.Versions.SelectMany(v => v.Rejections)).Select(x => x.Reason)))));
            var tvdbHit = plain.Releases.First(r => r.Title == "Breaking.Bad.S05E14.1080p.WEB-DL.DDP5.1.H.264-NTb");
            Assert.Equal(EpisodeNumbering.Tvdb, tvdbHit.Numbering);
            Assert.True(tvdbHit.Versions.Single().Approved);

            // A release group nobody uses: TVDB numbering yields nothing acceptable, the order's numbers are asked too.
            await ConstraintsAsync(client, series.Id, version.Id, new Bigarr.Api.Features.Movies.ConstraintsDto(null, [], [], "NOPE"));
            var fallback = await SeriesSearchTests.SearchAsync(client, series.Id, $"episodeId={e14.Id}");
            Assert.True(fallback.OrderFallback);
            var byOrder = fallback.Releases.First(r => r.Title == "Breaking.Bad.S06E06.1080p.WEB-DL.DDP5.1.H.264-NTb");
            Assert.Equal(EpisodeNumbering.Order, byOrder.Numbering);
            Assert.True(byOrder.Held);
            Assert.Equal(e14.Id, byOrder.Episodes.Single().Id);
            Assert.Contains(byOrder.Versions.Single().Rejections, r => r.Reason == Bigarr.Api.Infrastructure.Matching.SeriesReleaseJudge.HeldMessage);

            // The TVDB releases are still judged on TVDB numbers (their episode is S05E14 either way).
            var stillTvdb = fallback.Releases.First(r => r.Title == "Breaking.Bad.S05E14.1080p.WEB-DL.DDP5.1.H.264-NTb");
            Assert.Equal(EpisodeNumbering.Tvdb, stillTvdb.Numbering);
            Assert.False(stillTvdb.Held);

            // A season of the order on screen: its episodes asked by their TVDB seasons first.
            var season = await SeriesSearchTests.SearchAsync(client, series.Id, "season=6&orderSeason=true");
            Assert.Equal("Season 6", season.Label);
            var tvdbPack = season.Releases.Single(r => r.Title == "Breaking.Bad.S05.1080p.BluRay.x264-DEMAND");
            Assert.Equal(16, tvdbPack.Episodes.Count);
        }
        finally
        {
            await ConstraintsAsync(client, series.Id, version.Id, null);
            await PostAsync<OrderUndoResponse>(client, $"/api/v1/series/{series.Id}/orders/undo", null);
        }
    }

    [Fact]
    public async Task The_sonarr_follow_judges_sonarrs_numbers_on_tvdb_whatever_the_order_shown()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await PostAsync<OrderApplyResponse>(client, $"/api/v1/series/{series.Id}/orders/apply", new OrderChoiceRequest(EpisodeOrder.TmdbEpisodeGroup, DvdGroup));

        try
        {
            using var scope = factory.Services.CreateScope();
            var judge = scope.ServiceProvider.GetRequiredService<SeriesShadowJudge>();
            var version = series.Versions.Single().Name;

            // Sonarr's S05E10 is TVDB's, shown as S06E02 here: the follow still reads it as S05E10.
            var tvdb = await judge.JudgeAsync(SeriesSearchTests.BreakingBad, version, Release("Breaking.Bad.S05E10.1080p.WEB-DL.DDP5.1.H.264-NTb"), SeriesEndpointsTests.Now.UtcDateTime, CancellationToken.None);
            Assert.Null(tvdb.Unmatched);
            Assert.Equal("S05E10", tvdb.Episodes);

            // The order's numbers mean nothing to Sonarr: no fallback in the follow.
            var order = await judge.JudgeAsync(SeriesSearchTests.BreakingBad, version, Release("Breaking.Bad.S06E02.1080p.WEB-DL.DDP5.1.H.264-NTb"), SeriesEndpointsTests.Now.UtcDateTime, CancellationToken.None);
            Assert.NotNull(order.Unmatched);
        }
        finally
        {
            await PostAsync<OrderUndoResponse>(client, $"/api/v1/series/{series.Id}/orders/undo", null);
        }
    }

    [Fact]
    public async Task A_grab_records_the_numbering_its_release_matched_on()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.Single();
        var e12 = await SeriesSetup.EpisodeAsync(client, series.Id, 5, 12); // DVD S06E04, its own episode: the search test keeps S05E14 free
        var created = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest("Mock order", Bigarr.Domain.Downloads.DownloadClientImplementation.Mock, Category: "films-order", SeriesCategory: "tv-order"), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var downloadClient = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;

        try
        {
            var request = new GrabRequest(version.Id, 0, "crafted/order", "Breaking.Bad.S06E04.1080p.WEB-DL.DDP5.1.H.264-NTb", InfoHash: "0123456789abcdef0123456789abcdef01234567", Size: 1024, Indexer: "by hand", EpisodeIds: [e12.Id], Numbering: EpisodeNumbering.Order);
            var response = await client.PostAsJsonAsync("/api/v1/grab", request, Json);
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var grab = (await response.Content.ReadFromJsonAsync<GrabResponse>(Json))!;

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Equal(EpisodeNumbering.Order, (await db.Grabs.SingleAsync(g => g.Id == grab.GrabId)).Numbering);
            var line = await db.History.SingleAsync(h => h.EventType == HistoryEventType.Grabbed && h.SourceTitle == request.Title);
            Assert.Equal("order", line.Data["numbering"]);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    /// <summary>A real file in the version folder, recorded as the episode's file (as an import would).</summary>
    private async Task<string> FileAsync(int seriesId, int versionId, string folder, int season, int number, string relativePath, bool subtitle = false)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[4096]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == versionId && t.Episode.SeriesId == seriesId && t.Episode.SeasonNumber == season && t.Episode.EpisodeNumber == number);
        var file = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = relativePath,
            SeasonNumber = season,
            Size = 4096,
            QualityId = ParserQuality.Bluray1080p.Id,
            OriginalReleaseTitle = $"Breaking.Bad.S{season:00}E{number:00}.1080p.BluRay.x264-GRP",
            ReleaseGroup = "GRP",
        };
        if (subtitle)
        {
            var srt = Path.ChangeExtension(relativePath, ".en.srt");
            await File.WriteAllTextAsync(Path.Combine(folder, srt), "1\n00:00:01,000 --> 00:00:02,000\nDemo\n");
            file.Extras.Add(new EpisodeExtraFile { RelativePath = srt, Extension = ".srt", Kind = Bigarr.Domain.Movies.ExtraFileKind.Subtitle, Language = "en" });
        }

        target.EpisodeFile = file;
        await db.SaveChangesAsync();
        return relativePath;
    }

    private static void Link(string existing, string link)
    {
        // A hardlink, as the import makes them (link(2) through the shell keeps the test free of P/Invoke).
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ln", [existing, link]) { RedirectStandardError = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static ReleaseInfo Release(string title) => new() { Guid = "sonarr/" + title, Title = title, IndexerName = "Sonarr", Size = 2L * 1024 * 1024 * 1024 };

    private static async Task ConstraintsAsync(HttpClient client, int seriesId, int versionId, Bigarr.Api.Features.Movies.ConstraintsDto? constraints)
    {
        var body = constraints is null ? new UpdateSeriesVersionRequest(ClearConstraints: true) : new UpdateSeriesVersionRequest(Constraints: constraints);
        var response = await client.PutAsJsonAsync($"/api/v1/series/{seriesId}/versions/{versionId}", body, Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<SeriesResponse> GetSeriesAsync(HttpClient client, int seriesId) =>
        (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{seriesId}", Json))!;

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body)
    {
        var response = body is null ? await client.PostAsync(url, null) : await client.PostAsJsonAsync(url, body, Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}

/// <summary>In shadow mode the pick records the numbering its release matched on, as a grab does.</summary>
public sealed class SeriesOrderShadowTests(SeriesShadowApiFactory factory) : IClassFixture<SeriesShadowApiFactory>
{
    [Fact]
    public async Task A_shadow_pick_records_the_numbering()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var e12 = await SeriesSetup.EpisodeAsync(client, series.Id, 5, 12);
        var request = new GrabRequest(series.Versions.Single().Id, 0, "crafted/order-pick", "Breaking.Bad.S06E04.1080p.WEB-DL.DDP5.1.H.264-NTb", InfoHash: "89abcdef0123456789abcdef0123456789abcdef", Size: 1024, Indexer: "by hand", EpisodeIds: [e12.Id], Numbering: EpisodeNumbering.Order);
        var response = await client.PostAsJsonAsync("/api/v1/grab", request, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var pick = await db.SeriesShadowPicks.SingleAsync(p => p.ReleaseGuid == "crafted/order-pick");
        Assert.Equal(EpisodeNumbering.Order, pick.Numbering);
    }
}
