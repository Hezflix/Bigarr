using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Blocklist;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Notifications;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Notifications;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Automation;

/// <summary>A clock the test moves forward: an episode airs between two RSS passes.</summary>
public sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class SeriesAutomationApiFactory : BigarrApiFactory
{
    /// <summary>Monday 2026-11-09, 23:30 UTC: tonight's Daily Show (23:00 New York) has not aired yet.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 9, 23, 30, 0, TimeSpan.Zero);

    public SeriesAutomationApiFactory()
        : this(new AdjustableClock(Start))
    {
    }

    private SeriesAutomationApiFactory(AdjustableClock clock)
        : base(null, new Dictionary<string, string> { ["Bigarr:Notifications:SweepSeconds"] = "1" }, deleteOnDispose: true, time: clock)
    {
        Clock = clock;
    }

    public AdjustableClock Clock { get; }
}

/// <summary>
/// S7's acceptance on the mock indexer and the mock download client: a monitored daily show downloads tonight's episode
/// end to end through RSS once it has aired (grab, import, Sonarr-format webhooks, the Wanted and Calendar views follow),
/// and the next day's download, failing in the client, is blocklisted for its episode and replaced by a search.
/// </summary>
public sealed class SeriesAutomationTests(SeriesAutomationApiFactory factory) : IClassFixture<SeriesAutomationApiFactory>, IDisposable
{
    private const int DailyShow = 71256;

    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-automation-bin-").FullName;

    public void Dispose() => TryDelete(_bin);

    [Fact]
    public async Task A_monitored_series_downloads_a_newly_aired_episode_through_rss_and_a_failed_download_is_blocklisted_and_replaced()
    {
        await using var sink = await WebhookSink.StartAsync();
        var client = await ClientAsync(factory);
        var configured = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, _bin, 7), Json);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);

        // The show, monitored for the episodes to come; the mock indexer and client; a Sonarr-format webhook.
        var series = await AddAsync(client, DailyShow, MonitoringOption.Future);
        var version = series.Versions.Single();
        await SeriesSetup.MockIndexerAsync(client);
        var id = Guid.NewGuid().ToString("N")[..8];
        var downloadClient = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest($"Mock {id}", DownloadClientImplementation.Mock, Category: $"films-{id}", SeriesCategory: $"tv-{id}"), Json);
        Assert.Equal(HttpStatusCode.Created, downloadClient.StatusCode);
        var hook = await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Sonarr-format sink", sink.Url, Format: WebhookFormat.Sonarr), Json);
        Assert.True(hook.StatusCode == HttpStatusCode.Created, await hook.Content.ReadAsStringAsync());

        var tonight = await SeriesSetup.EpisodeAsync(client, series.Id, 31, 127);
        Assert.Equal(new DateTime(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc), tonight.AirDateUtc); // 23:00 New York
        Assert.False(tonight.HasAired);
        Assert.True(tonight.Versions.Single().Monitored);
        var calendar = await CalendarAsync(client, "2026-11-09", "2026-11-12");
        Assert.Equal(CalendarStatus.Unaired, calendar.Single(c => c.EpisodeId == tonight.Id).Status);

        // 1. Before it airs: yesterday's release in the feed names no episode of the show, nothing is grabbed.
        var before = await RssAsync(client);
        Assert.Contains("0 grabbed", before.Summary, StringComparison.Ordinal);

        // 2. The next morning the feed carries it: RSS grabs it for the version, with no one clicking.
        factory.Clock.Now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var morning = await RssAsync(client);
        Assert.Contains("1 grabbed", morning.Summary, StringComparison.Ordinal);
        var row = Assert.Single((await RefreshAsync(client)).Items, i => i.SeriesId == series.Id);
        Assert.Equal("The.Daily.Show.2026.11.09.1080p.WEB.h264-EDITH", row.Title);
        Assert.Equal(127, Assert.Single(row.Episodes).EpisodeNumber);
        var grabbed = await HistoryAsync(client, series.Id);
        Assert.Equal("rss", Assert.Single(grabbed, h => h.EventType == HistoryEventType.Grabbed).Data["trigger"]);

        // Sonarr's Grab payload: the series is the version, the episode in TVDB numbers.
        var grabHook = await sink.WaitForAsync(r => r.EventType == "episodeGrabbed");
        using (var doc = JsonDocument.Parse(grabHook.Body))
        {
            var root = doc.RootElement;
            Assert.Equal("Grab", root.GetProperty("eventType").GetString());
            Assert.Equal(version.Id, root.GetProperty("series").GetProperty("id").GetInt32());
            Assert.Equal("The Daily Show", root.GetProperty("series").GetProperty("title").GetString());
            Assert.Equal("daily", root.GetProperty("series").GetProperty("type").GetString());
            Assert.Equal(DailyShow, root.GetProperty("series").GetProperty("tvdbId").GetInt32());
            var episode = root.GetProperty("episodes")[0];
            Assert.Equal(31, episode.GetProperty("seasonNumber").GetInt32());
            Assert.Equal(127, episode.GetProperty("episodeNumber").GetInt32());
            Assert.Equal("2026-11-09", episode.GetProperty("airDate").GetString());
            Assert.Equal(row.Title, root.GetProperty("release").GetProperty("releaseTitle").GetString());
            Assert.Equal(row.DownloadId, root.GetProperty("downloadId").GetString());
        }

        // 3. The download completes: imported, renamed, journaled; Sonarr's Download payload follows.
        await CompleteAsync(client, row.DownloadId);
        await RefreshAsync(client);
        var files = await WaitForFilesAsync(client, series.Id, f => f.Any(x => x.EpisodeIds.Contains(tonight.Id)));
        await WaitOffQueueAsync(client, row.GrabId!.Value); // the grab ends after its files are saved
        var file = files.Single(f => f.EpisodeIds.Contains(tonight.Id));
        Assert.Equal("WEBDL-1080p", file.Quality);
        var importHook = await sink.WaitForAsync(r => r.EventType == "episodeImported");
        using (var doc = JsonDocument.Parse(importHook.Body))
        {
            Assert.Equal("Download", doc.RootElement.GetProperty("eventType").GetString());
            Assert.False(doc.RootElement.GetProperty("isUpgrade").GetBoolean());
            Assert.Equal(file.Path, doc.RootElement.GetProperty("episodeFile").GetProperty("path").GetString());
            Assert.Equal("singleEpisode", doc.RootElement.GetProperty("release").GetProperty("releaseType").GetString());
        }

        // The Wanted view: a WEB-DL below the profile's Bluray cutoff is cutoff unmet, no longer missing.
        var cutoff = await WantedAsync(client, "cutoffUnmet", series.Id);
        Assert.Equal(WantedFilter.CutoffUnmet, Assert.Single(cutoff.Items, i => i.EpisodeId == tonight.Id).Kind);
        Assert.DoesNotContain((await WantedAsync(client, "missing", series.Id)).Items, i => i.EpisodeId == tonight.Id);

        // 4. The day after: the next episode is grabbed by RSS, then fails in the client.
        factory.Clock.Now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var next = await SeriesSetup.EpisodeAsync(client, series.Id, 31, 128);
        Assert.Contains("1 grabbed", (await RssAsync(client)).Summary, StringComparison.Ordinal);
        var failing = Assert.Single((await RefreshAsync(client)).Items, i => i.SeriesId == series.Id && i.Episodes.Any(e => e.EpisodeId == next.Id));
        Assert.Equal("The.Daily.Show.2026.11.10.1080p.WEB.h264-EDITH", failing.Title);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/downloadclients/mock/{failing.DownloadId}/fail?message=Tracker%20unregistered", null)).StatusCode);
        await RefreshAsync(client);

        // Blocklisted for the episode of this version, journaled, and a replacement searched (Sonarr's redownload).
        var blocklist = (await client.GetFromJsonAsync<BlocklistPageResponse>($"/api/v1/blocklist?seriesId={series.Id}", Json))!;
        var entry = Assert.Single(blocklist.Items);
        Assert.Equal(failing.Title, entry.SourceTitle);
        Assert.Equal("S31E128", entry.Episode);
        Assert.Equal("The Daily Show", entry.SeriesTitle);
        Assert.Equal(version.Name, entry.VersionName);
        var history = await HistoryAsync(client, series.Id);
        Assert.Contains(history, h => h.EventType == HistoryEventType.Blocklisted && h.Data["episode"] == "S31E128");
        var failed = Assert.Single(history, h => h.EventType == HistoryEventType.DownloadFailed);
        Assert.Equal("true", failed.Data["searchQueued"]);
        Assert.Equal("true", failed.Data["blocklisted"]);

        // The replacement: the blocklisted release is skipped, the 720p one taken for the same episode.
        var replacement = await WaitForQueueRowAsync(client, r => r.SeriesId == series.Id && r.State != GrabState.DownloadFailed && r.Episodes.Any(e => e.EpisodeId == next.Id), "replacement grab");
        Assert.Equal("The.Daily.Show.2026.11.10.720p.WEB.h264-EDITH", replacement.Title);
        var redo = Assert.Single(await HistoryAsync(client, series.Id), h => h.EventType == HistoryEventType.Grabbed && h.SourceTitle == replacement.Title);
        Assert.Equal("search:failed", redo.Data["trigger"]);

        // The calendar now reads: tonight's episode downloaded, the next one downloading.
        calendar = await CalendarAsync(client, "2026-11-09", "2026-11-12");
        Assert.Equal(CalendarStatus.Downloaded, calendar.Single(c => c.EpisodeId == tonight.Id).Status);
        Assert.Equal(CalendarStatus.Downloading, calendar.Single(c => c.EpisodeId == next.Id).Status);
        var ics = await client.GetStringAsync("/api/v1/calendar/bigarr.ics");
        Assert.Contains("SUMMARY:The Daily Show - 2026-11-09 - TBA", ics, StringComparison.Ordinal);
        Assert.Contains($"UID:bigarr-episode-{tonight.Id}", ics, StringComparison.Ordinal);
    }

    private static async Task<SeriesResponse> AddAsync(HttpClient client, int tvdbId, MonitoringOption monitor)
    {
        var profiles = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Quality.QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var hd = profiles.Single(p => p.Name == Bigarr.Api.Features.Quality.QualitySeeder.SeriesHdProfileName);
        var dir = Directory.CreateTempSubdirectory("bigarr-automation-").FullName;
        var root = await client.PostAsJsonAsync("/api/v1/rootfolders", new Bigarr.Api.Features.Library.AddRootFolderRequest(dir, MediaKind.Series));
        Assert.Equal(HttpStatusCode.Created, root.StatusCode);
        var rootId = (await root.Content.ReadFromJsonAsync<Bigarr.Api.Features.Library.RootFolderResponse>(Json))!.Id;
        var response = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(hd.Id, rootId, TvdbId: tvdbId, Monitor: monitor));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesResponse>(Json))!;
    }

    private static async Task<TaskRunResponse> RssAsync(HttpClient client)
    {
        var run = await client.PostAsync($"/api/v1/system/tasks/{RssSyncJob.JobName}/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var outcome = (await run.Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
        Assert.True(outcome.Success, outcome.Error);
        return outcome;
    }

    private static Task<List<CalendarEpisodeResponse>> CalendarAsync(HttpClient client, string start, string end) =>
        client.GetFromJsonAsync<List<CalendarEpisodeResponse>>($"/api/v1/calendar?start={start}&end={end}", Json)!;

    private static Task<SeriesWantedPageResponse> WantedAsync(HttpClient client, string filter, int seriesId) =>
        client.GetFromJsonAsync<SeriesWantedPageResponse>($"/api/v1/series/wanted?filter={filter}&seriesId={seriesId}", Json)!;

    private static Task<List<HistoryEventResponse>> HistoryAsync(HttpClient client, int seriesId) =>
        client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?seriesId={seriesId}&limit=1000", Json)!;

    private static async Task<List<EpisodeFileResponse>> WaitForFilesAsync(HttpClient client, int seriesId, Func<List<EpisodeFileResponse>, bool> until) =>
        await WaitUntilAsync(async () =>
        {
            var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{seriesId}/files", Json))!.Files;
            return until(files) ? files : null;
        }, $"files of series {seriesId}");
}
