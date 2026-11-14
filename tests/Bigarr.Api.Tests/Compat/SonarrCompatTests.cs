using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Compat.Sonarr;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Compat;

/// <summary>
/// The Sonarr v3 surface (blueprint §7b) on the fixture catalogue: one Seerr with a standard and a 4K Sonarr entry (two
/// keys, two views) requesting and tracking seasons; Bazarr reading series, episodes, files and grabs; Prowlarr pushing
/// indexers as to a Sonarr. Shapes are checked on the raw JSON, key by key, as those clients read them.
/// </summary>
public sealed class SonarrCompatTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>, IDisposable
{
    private const int DemonSlayer = 348545;
    private const int Firefly = 78874;
    private const string Api = SonarrCompat.RoutePrefix;

    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-sonarr-bin-").FullName;

    public void Dispose() => Import.ImportTestHelpers.TryDelete(_bin);

    [Fact]
    public async Task The_surface_answers_as_a_sonarr_4_under_its_url_base()
    {
        var session = await SessionAsync(factory);
        var root = await SeriesRootAsync(session);
        var (seerr, _) = await KeyClientAsync(factory, session, "seerr-status", "default");

        var status = await seerr.GetAsync($"{Api}/system/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(SonarrCompat.ApplicationVersion, status.Headers.GetValues(RadarrCompat.VersionHeader).Single());
        var body = await status.ReadJsonAsync();
        Assert.Equal("4.99.0.0", body.GetProperty("version").GetString());
        Assert.Equal("/sonarr", body.GetProperty("urlBase").GetString());
        Assert.Equal("Sonarr", body.GetProperty("appName").GetString());
        Assert.True(new Version(body.GetProperty("version").GetString()!) >= new Version(4, 0, 9, 2421)); // Bazarr: episode files embedded, /signalr/messages

        // Bazarr's legacy probe gets a 404 (no HTML page), and a session cookie never opens the surface.
        var legacy = await seerr.GetAsync("/sonarr/api/system/status");
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
        Assert.Equal(SonarrCompat.ApplicationVersion, legacy.Headers.GetValues(RadarrCompat.VersionHeader).Single());
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync($"{Api}/system/status")).StatusCode);

        // The Radarr surface keeps its own version.
        var radarr = await seerr.GetAsync("/api/v3/system/status");
        Assert.Equal(RadarrCompat.ApplicationVersion, (await radarr.ReadJsonAsync()).GetProperty("version").GetString());

        // Profiles and root folders a series version can take, under both spellings Seerr and Bazarr use.
        var profiles = await (await seerr.GetAsync($"{Api}/qualityProfile")).ReadJsonAsync();
        var names = profiles.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains(QualitySeeder.SeriesHdProfileName, names);
        Assert.Contains(QualitySeeder.SeriesUhdProfileName, names);
        Assert.DoesNotContain(QualitySeeder.HdProfileName, names);
        Assert.Equal(names, (await (await seerr.GetAsync($"{Api}/qualityprofile")).ReadJsonAsync()).EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        var roots = await (await seerr.GetAsync($"{Api}/rootfolder")).ReadJsonAsync();
        Assert.Equal([root.Path], roots.EnumerateArray().Select(r => r.GetProperty("path").GetString()));
        Assert.DoesNotContain(root.Path, (await (await seerr.GetAsync("/api/v3/rootfolder")).ReadJsonAsync()).EnumerateArray().Select(r => r.GetProperty("path").GetString()));

        // Seerr's per-user tags.
        var tag = await seerr.PostAsJsonAsync($"{Api}/tag", new { label = "1-alice" });
        Assert.Equal(HttpStatusCode.Created, tag.StatusCode);
        var tagId = (await tag.ReadJsonAsync()).GetProperty("id").GetInt32();
        var renamed = await seerr.PutAsJsonAsync($"{Api}/tag/{tagId}", new { id = tagId, label = "1-alice-renamed" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Contains((await (await seerr.GetAsync($"{Api}/tag")).ReadJsonAsync()).EnumerateArray(), t => t.GetProperty("label").GetString() == "1-alice-renamed");

        // Unknown parameters are ignored; an empty library is an empty list (Seerr then skips its orphan cleanup).
        var films = await (await seerr.GetAsync("/api/v3/movie?tmdbId=999999")).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Array, films.ValueKind); // §7b.6 point 15: Seerr's Radarr orphan check is served
        Assert.Empty(films.EnumerateArray());
        var (empty, _) = await KeyClientAsync(factory, session, "seerr-empty", "version:nothing-here");
        Assert.Empty((await (await empty.GetAsync($"{Api}/series?includeSeasonImages=false")).ReadJsonAsync()).EnumerateArray());
    }

    [Fact]
    public async Task One_seerr_with_a_standard_and_a_4k_entry_requests_and_tracks_seasons_of_one_show()
    {
        var session = await SessionAsync(factory);
        var root = await SeriesRootAsync(session);
        var (hdProfile, uhdProfile) = await SeriesProfilesAsync(session);
        await DeleteShowAsync(session, DemonSlayer);
        var (seerrHd, _) = await KeyClientAsync(factory, session, "seerr-sonarr", "version:1080p");
        var (seerr4k, _) = await KeyClientAsync(factory, session, "seerr-sonarr-4k", "version:4k");
        var (plain, _) = await KeyClientAsync(factory, session, "seerr-sonarr-default", "default");
        var (all, _) = await KeyClientAsync(factory, session, "bazarr-sonarr-all", "all");

        // 1. Existence test: not in the library, so no id, with the season list Seerr builds its POST from.
        var lookup = Assert.Single((await (await seerrHd.GetAsync($"{Api}/series/lookup?term=tvdb:{DemonSlayer}")).ReadJsonAsync()).EnumerateArray());
        Assert.False(lookup.TryGetProperty("id", out _));
        Assert.Equal(DemonSlayer, lookup.GetProperty("tvdbId").GetInt32());
        Assert.Equal(DemonSlayer.ToString(), lookup.GetProperty("titleSlug").GetString());
        Assert.Equal([0, 1, 2, 3, 4, 5], lookup.GetProperty("seasons").EnumerateArray().Select(s => s.GetProperty("seasonNumber").GetInt32()));
        Assert.Equal(JsonValueKind.Array, lookup.GetProperty("tags").ValueKind);
        Assert.Equal("anime", lookup.GetProperty("seriesType").GetString());

        // 2. Seerr's POST, field for field: season 1 requested, everything else unmonitored, no addOptions.monitor.
        var added = await seerrHd.PostAsJsonAsync($"{Api}/series", SeerrAdd(DemonSlayer, "Demon Slayer", hdProfile, root.Path, requested: [1]));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var hd = await added.ReadJsonAsync();
        var hdId = hd.GetProperty("id").GetInt32();
        Assert.True(hdId > 0);
        Assert.Equal(DemonSlayer.ToString(), hd.GetProperty("titleSlug").GetString());
        Assert.Equal("anime", hd.GetProperty("seriesType").GetString());
        Assert.Equal(Seasons(hd), new Dictionary<int, bool> { [0] = false, [1] = true, [2] = false, [3] = false, [4] = false, [5] = false });
        var episodes = await EpisodesAsync(seerrHd, hdId);
        Assert.All(episodes.Where(e => e.Season == 1), e => Assert.True(e.Monitored));
        Assert.All(episodes.Where(e => e.Season != 1), e => Assert.False(e.Monitored));

        // Seerr's availability reading: every episode of the season counted, aired or not; no file yet.
        var season1 = hd.GetProperty("seasons").EnumerateArray().Single(s => s.GetProperty("seasonNumber").GetInt32() == 1).GetProperty("statistics");
        Assert.Equal(episodes.Count(e => e.Season == 1), season1.GetProperty("totalEpisodeCount").GetInt32());
        Assert.Equal(0, season1.GetProperty("episodeFileCount").GetInt32());
        Assert.Equal(0, hd.GetProperty("statistics").GetProperty("episodeFileCount").GetInt32());

        // The 1080p entry now finds it; the 4K entry does not, and POSTs, which creates the 4K version of the same show.
        Assert.Equal(hdId, Assert.Single((await (await seerrHd.GetAsync($"{Api}/series/lookup?term=tvdb:{DemonSlayer}")).ReadJsonAsync()).EnumerateArray()).GetProperty("id").GetInt32());
        Assert.False(Assert.Single((await (await seerr4k.GetAsync($"{Api}/series/lookup?term=tvdb:{DemonSlayer}")).ReadJsonAsync()).EnumerateArray()).TryGetProperty("id", out _));
        var added4k = await seerr4k.PostAsJsonAsync($"{Api}/series", SeerrAdd(DemonSlayer, "Demon Slayer", uhdProfile, root.Path, requested: [2]));
        Assert.Equal(HttpStatusCode.Created, added4k.StatusCode);
        var uhdId = (await added4k.ReadJsonAsync()).GetProperty("id").GetInt32();
        Assert.NotEqual(hdId, uhdId);

        // Each view sees its own line; ids outside the view are 404; episode ids are shared, their flags are per version.
        Assert.Equal([hdId], Ids(await (await seerrHd.GetAsync($"{Api}/series?tvdbId={DemonSlayer}")).ReadJsonAsync()));
        Assert.Equal([uhdId], Ids(await (await seerr4k.GetAsync($"{Api}/series?tvdbId={DemonSlayer}")).ReadJsonAsync()));
        Assert.Equal([hdId], Ids(await (await plain.GetAsync($"{Api}/series?tvdbId={DemonSlayer}")).ReadJsonAsync()));
        Assert.Equal([hdId, uhdId], Ids(await (await all.GetAsync($"{Api}/series?tvdbId={DemonSlayer}")).ReadJsonAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await seerr4k.GetAsync($"{Api}/series/{hdId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seerrHd.GetAsync($"{Api}/episode?seriesId={uhdId}")).StatusCode);
        var episodes4k = await EpisodesAsync(seerr4k, uhdId);
        Assert.Equal(episodes.Select(e => e.Id), episodes4k.Select(e => e.Id));
        Assert.All(episodes4k.Where(e => e.Season == 2), e => Assert.True(e.Monitored));
        Assert.All(episodes4k.Where(e => e.Season == 1), e => Assert.False(e.Monitored));

        // Adding again is Sonarr's "already added"; an all key designates no version and writes nothing.
        var again = await seerrHd.PostAsJsonAsync($"{Api}/series", SeerrAdd(DemonSlayer, "Demon Slayer", hdProfile, root.Path, requested: [1]));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("This series has already been added", (await again.ReadJsonAsync())[0].GetProperty("errorMessage").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await all.PostAsJsonAsync($"{Api}/series", SeerrAdd(Firefly, "Firefly", hdProfile, root.Path, requested: [1]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await all.PutAsJsonAsync($"{Api}/episode/monitor", new { episodeIds = new[] { episodes[0].Id }, monitored = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await all.PostAsJsonAsync($"{Api}/command", new { name = "MissingEpisodeSearch", seriesId = hdId })).StatusCode);

        // 3. Re-request of season 3 by the 1080p entry: lookup resource back with the season flipped and the user's tag
        //    added, then the episodes Seerr found unmonitored, then a series-wide missing search.
        var tagId = (await (await seerrHd.PostAsJsonAsync($"{Api}/tag", new { label = "2-bob" })).ReadJsonAsync()).GetProperty("id").GetInt32();
        var resource = Assert.Single((await (await seerrHd.GetAsync($"{Api}/series/lookup?term=tvdb:{DemonSlayer}")).ReadJsonAsync()).EnumerateArray());
        var put = await seerrHd.PutAsJsonAsync($"{Api}/series", Reshaped(resource, season: 3, tagId));
        Assert.Equal(HttpStatusCode.Accepted, put.StatusCode);
        var putBack = await put.ReadJsonAsync();
        Assert.Equal(hdId, putBack.GetProperty("id").GetInt32());
        Assert.True(putBack.GetProperty("monitored").GetBoolean());
        Assert.Contains(tagId, putBack.GetProperty("tags").EnumerateArray().Select(t => t.GetInt32()));
        Assert.True(Seasons(putBack)[3]);
        Assert.True(Seasons(putBack)[1]);
        episodes = await EpisodesAsync(seerrHd, hdId);
        Assert.All(episodes.Where(e => e.Season == 3), e => Assert.True(e.Monitored));

        var special = episodes.First(e => e.Season == 0);
        var monitor = await seerrHd.PutAsJsonAsync($"{Api}/episode/monitor", new { episodeIds = new[] { special.Id }, monitored = true });
        Assert.Equal(HttpStatusCode.Accepted, monitor.StatusCode);
        Assert.True((await EpisodesAsync(seerrHd, hdId)).Single(e => e.Id == special.Id).Monitored);
        Assert.False((await EpisodesAsync(seerr4k, uhdId)).Single(e => e.Id == special.Id).Monitored);

        var command = await seerrHd.PostAsJsonAsync($"{Api}/command", new { name = "MissingEpisodeSearch", seriesId = hdId });
        Assert.Equal(HttpStatusCode.Created, command.StatusCode);
        var commandBody = await command.ReadJsonAsync();
        Assert.Equal("MissingEpisodeSearch", commandBody.GetProperty("name").GetString());
        Assert.Equal(hdId, commandBody.GetProperty("body").GetProperty("seriesId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await seerrHd.GetAsync($"{Api}/command/{commandBody.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await seerrHd.PostAsJsonAsync($"{Api}/command", new { name = "RefreshMonitoredDownloads" })).StatusCode);

        // A command Bigarr has no use for is accepted, not a 404.
        var unknown = await seerrHd.PostAsJsonAsync($"{Api}/command", new { name = "RenameFiles", seriesId = hdId, files = new[] { 1 } });
        Assert.Equal(HttpStatusCode.Created, unknown.StatusCode);
        Assert.Equal("completed", (await unknown.ReadJsonAsync()).GetProperty("status").GetString());

        // The download tracker's first read: nothing downloading for either entry, in the paged envelope.
        var queue = await (await seerr4k.GetAsync($"{Api}/queue?includeEpisode=true")).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Array, queue.GetProperty("records").ValueKind);
        Assert.Equal(0, queue.GetProperty("totalRecords").GetInt32());

        // 4. Deleting through the 1080p entry (Seerr always sends deleteFiles=true): the default version goes, the 4K one
        //    takes over as the default, so the default key's line changes id; deleting the last version takes the show.
        Assert.Equal(HttpStatusCode.OK, (await seerrHd.DeleteAsync($"{Api}/series/{hdId}?deleteFiles=true&addImportExclusion=false")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seerrHd.GetAsync($"{Api}/series/{hdId}")).StatusCode);
        Assert.Equal([uhdId], Ids(await (await plain.GetAsync($"{Api}/series?tvdbId={DemonSlayer}")).ReadJsonAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await seerrHd.DeleteAsync($"{Api}/series/{hdId}?deleteFiles=true&addImportExclusion=false")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await seerr4k.DeleteAsync($"{Api}/series/{uhdId}?deleteFiles=true&addImportExclusion=false")).StatusCode);
        Assert.Empty((await (await all.GetAsync($"{Api}/series?tvdbId={DemonSlayer}")).ReadJsonAsync()).EnumerateArray());
    }

    [Fact]
    public async Task Bazarr_reads_episodes_with_their_files_and_seerr_tracks_a_season_pack_one_record_per_episode()
    {
        var client = await Import.ImportTestHelpers.ClientAsync(factory);
        var put = await client.PutAsJsonAsync("/api/v1/mediamanagement", new MediaManagementRequest(true, MediaManagementConfig.DefaultFileNameTemplate, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, _bin, 7), Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var id = Guid.NewGuid().ToString("N")[..8];
        var downloadClient = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest($"Mock sonarr {id}", DownloadClientImplementation.Mock, Category: $"films-{id}", SeriesCategory: $"tv-{id}"), Json);
        Assert.Equal(HttpStatusCode.Created, downloadClient.StatusCode);
        var clientId = (await downloadClient.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!.Id;
        var version = series.Versions.Single();
        var (bazarr, _) = await KeyClientAsync(factory, client, "bazarr-sonarr", "default");
        var (all, _) = await KeyClientAsync(factory, client, "bazarr-sonarr-every", "all");
        var (other, _) = await KeyClientAsync(factory, client, "bazarr-sonarr-4k", "version:4k");

        try
        {
            var season = await SeriesSearchTests.SearchAsync(client, series.Id, "season=1");
            var pack = season.Releases.Single(r => r.Title == "Breaking.Bad.S01.1080p.BluRay.x264-DEMAND");
            var grab = await client.PostAsJsonAsync("/api/v1/grab", SeriesSetup.Grab(pack) with { InfoUrl = pack.InfoUrl }, Json);
            Assert.Equal(HttpStatusCode.Created, grab.StatusCode);
            var grabbed = (await grab.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
            await Import.ImportTestHelpers.RefreshAsync(client);

            // Seerr's download tracker: the pack is one record per episode, sharing the download; seriesId is the version.
            var queue = await (await bazarr.GetAsync($"{Api}/queue?includeEpisode=true")).ReadJsonAsync();
            var records = queue.GetProperty("records").EnumerateArray().Where(r => r.GetProperty("downloadId").GetString() == grabbed.DownloadId).ToList();
            Assert.Equal(7, records.Count);
            Assert.Equal(records.Count, queue.GetProperty("totalRecords").GetInt32());
            Assert.Equal(7, records.Select(r => r.GetProperty("id").GetInt32()).Distinct().Count());
            Assert.All(records, r =>
            {
                Assert.Equal(version.Id, r.GetProperty("seriesId").GetInt32());
                Assert.Equal(pack.Title, r.GetProperty("title").GetString());
                Assert.Equal(pack.Size, r.GetProperty("size").GetInt64());
                Assert.Equal(1, r.GetProperty("episode").GetProperty("seasonNumber").GetInt32());
                Assert.Equal("downloading", r.GetProperty("trackedDownloadState").GetString());
                Assert.Equal("ok", r.GetProperty("trackedDownloadStatus").GetString());
                Assert.Equal("torrent", r.GetProperty("protocol").GetString());
                Assert.Equal("Bluray-1080p", r.GetProperty("quality").GetProperty("quality").GetProperty("name").GetString());
                Assert.True(r.TryGetProperty("sizeleft", out _));
                Assert.True(r.TryGetProperty("downloadClient", out _));
            });
            Assert.Equal(Enumerable.Range(1, 7), records.Select(r => r.GetProperty("episode").GetProperty("episodeNumber").GetInt32()).Order());
            Assert.Equal(records.Select(r => r.GetProperty("id").GetInt32()).Order(), (await (await bazarr.GetAsync($"{Api}/queue")).ReadJsonAsync()).GetProperty("records").EnumerateArray().Where(r => r.GetProperty("downloadId").GetString() == grabbed.DownloadId).Select(r => r.GetProperty("id").GetInt32()).Order());
            Assert.DoesNotContain((await (await other.GetAsync($"{Api}/queue")).ReadJsonAsync()).GetProperty("records").EnumerateArray(), r => r.GetProperty("downloadId").GetString() == grabbed.DownloadId);

            await Import.ImportTestHelpers.CompleteAsync(client, grabbed.DownloadId!);
            await Import.ImportTestHelpers.RefreshAsync(client);
            await Import.ImportTestHelpers.WaitUntilAsync(async () =>
            {
                var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Files;
                return files.Count(f => f.SeasonNumber == 1) == 7 ? files : null;
            }, "the pack imported");
            await Import.ImportTestHelpers.WaitOffQueueAsync(client, grabbed.GrabId!.Value);

            // Bazarr's series sync: every key it indexes is there; the season is complete for Seerr.
            var row = await (await bazarr.GetAsync($"{Api}/series/{version.Id}")).ReadJsonAsync();
            foreach (var key in new[] { "id", "title", "sortTitle", "year", "seriesType", "monitored", "path", "tvdbId", "alternateTitles", "images", "tags" })
            {
                Assert.True(row.TryGetProperty(key, out _), key);
            }

            var seriesPath = row.GetProperty("path").GetString()!;
            Assert.Equal(Path.Combine(version.RootFolderPath, version.FolderName), seriesPath);
            Assert.StartsWith(root(row), seriesPath, StringComparison.Ordinal); // Bazarr keeps root folders that prefix a series path
            var s1 = row.GetProperty("seasons").EnumerateArray().Single(s => s.GetProperty("seasonNumber").GetInt32() == 1).GetProperty("statistics");
            Assert.Equal(7, s1.GetProperty("episodeFileCount").GetInt32());
            Assert.Equal(7, s1.GetProperty("totalEpisodeCount").GetInt32());
            Assert.True(s1.GetProperty("sizeOnDisk").GetInt64() > 0);
            Assert.Equal(7, row.GetProperty("statistics").GetProperty("episodeFileCount").GetInt32());
            Assert.Equal("/sonarr/MediaCover/" + version.Id + "/poster.jpg", row.GetProperty("images")[0].GetProperty("url").GetString());
            Assert.Equal("poster", row.GetProperty("images")[0].GetProperty("coverType").GetString());

            // Episodes: the file embedded only when asked, with what Bazarr's parser reads.
            var bare = await (await bazarr.GetAsync($"{Api}/episode?seriesId={version.Id}")).ReadJsonAsync();
            Assert.All(bare.EnumerateArray(), e => Assert.False(e.TryGetProperty("episodeFile", out _)));
            var withFiles = await (await bazarr.GetAsync($"{Api}/episode?seriesId={version.Id}&includeEpisodeFile=true")).ReadJsonAsync();
            var pilot = withFiles.EnumerateArray().Single(e => e.GetProperty("seasonNumber").GetInt32() == 1 && e.GetProperty("episodeNumber").GetInt32() == 1);
            Assert.True(pilot.GetProperty("hasFile").GetBoolean());
            Assert.Equal("Pilot", pilot.GetProperty("title").GetString());
            var file = pilot.GetProperty("episodeFile");
            var fileId = file.GetProperty("id").GetInt32();
            Assert.Equal(fileId, pilot.GetProperty("episodeFileId").GetInt32());
            Assert.Equal(Path.Combine(seriesPath, file.GetProperty("relativePath").GetString()!), file.GetProperty("path").GetString());
            Assert.True(file.GetProperty("size").GetInt64() > 0);
            Assert.Equal("Bluray-1080p", file.GetProperty("quality").GetProperty("quality").GetProperty("name").GetString());
            Assert.Equal(1080, file.GetProperty("quality").GetProperty("quality").GetProperty("resolution").GetInt32());
            Assert.Equal(JsonValueKind.Array, file.GetProperty("languages").ValueKind);
            Assert.Equal(version.Id, file.GetProperty("seriesId").GetInt32());
            Assert.False(withFiles.EnumerateArray().Single(e => e.GetProperty("seasonNumber").GetInt32() == 2 && e.GetProperty("episodeNumber").GetInt32() == 1).GetProperty("hasFile").GetBoolean());

            // Single syncs on SignalR events: /episode/{id} embeds the file, /episodefile/{id} answers for the key's view only.
            var pilotId = pilot.GetProperty("id").GetInt32();
            var single = await (await bazarr.GetAsync($"{Api}/episode/{pilotId}")).ReadJsonAsync();
            Assert.Equal(fileId, single.GetProperty("episodeFile").GetProperty("id").GetInt32());
            Assert.Equal(fileId, (await (await bazarr.GetAsync($"{Api}/episodeFile/{fileId}")).ReadJsonAsync()).GetProperty("id").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Api}/episodefile/{fileId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Api}/episode/{pilotId}")).StatusCode);
            Assert.Equal(fileId, (await (await all.GetAsync($"{Api}/episode/{pilotId}")).ReadJsonAsync()).GetProperty("episodeFileId").GetInt32());
            Assert.Equal(7, (await (await bazarr.GetAsync($"{Api}/episodefile?seriesId={version.Id}")).ReadJsonAsync()).EnumerateArray().Count(f => f.GetProperty("seasonNumber").GetInt32() == 1));

            // Bazarr's release refiner: the latest grab of the episode, with the release's page.
            var history = await (await bazarr.GetAsync($"{Api}/history?eventType=1&episodeId={pilotId}")).ReadJsonAsync();
            var entry = Assert.Single(history.GetProperty("records").EnumerateArray());
            Assert.Equal("grabbed", entry.GetProperty("eventType").GetString());
            Assert.Equal(pilotId, entry.GetProperty("episodeId").GetInt32());
            Assert.Equal(version.Id, entry.GetProperty("seriesId").GetInt32());
            Assert.Equal(pack.Title, entry.GetProperty("sourceTitle").GetString());
            Assert.Equal(pack.InfoUrl, entry.GetProperty("data").GetProperty("nzbInfoUrl").GetString());
            Assert.Equal(0, (await (await other.GetAsync($"{Api}/history?eventType=1&episodeId={pilotId}")).ReadJsonAsync()).GetProperty("totalRecords").GetInt32());
            var imports = await (await bazarr.GetAsync($"{Api}/history?eventType=3&seriesId={version.Id}&pageSize=50")).ReadJsonAsync();
            Assert.Equal(7, imports.GetProperty("totalRecords").GetInt32());

            // Bazarr asks for a rescan after each subtitle it writes.
            var rescan = await bazarr.PostAsJsonAsync($"{Api}/command", new { name = "RescanSeries", seriesId = version.Id });
            Assert.Equal(HttpStatusCode.Created, rescan.StatusCode);
            Assert.Equal("RescanSeries", (await rescan.ReadJsonAsync()).GetProperty("name").GetString());

            // Covers: served from the cache under the URL base and the API path; a key is required.
            var cache = Path.Combine(factory.ConfigDirectory, "MediaCover", "series", series.Id.ToString(), "poster-500.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            await File.WriteAllBytesAsync(cache, [0xFF, 0xD8, 0xFF]);
            var cover = await bazarr.GetAsync($"/sonarr/MediaCover/{version.Id}/poster-500.jpg");
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.Equal("image/jpeg", cover.Content.Headers.ContentType!.MediaType);
            Assert.Equal(HttpStatusCode.OK, (await bazarr.GetAsync($"{Api}/MediaCover/{version.Id}/poster-500.jpg")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateAnonymousClient().GetAsync($"/sonarr/MediaCover/{version.Id}/poster-500.jpg")).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{clientId}");
        }

        string root(JsonElement series) => series.GetProperty("rootFolderPath").GetString()!;
    }

    [Fact]
    public async Task A_show_without_a_tvdb_id_is_left_out()
    {
        var session = await SessionAsync(factory);
        var root = await SeriesRootAsync(session);
        var (hdProfile, _) = await SeriesProfilesAsync(session);
        await DeleteShowAsync(session, Firefly);
        var (seerr, _) = await KeyClientAsync(factory, session, "seerr-tvdbless", "default");
        var added = await seerr.PostAsJsonAsync($"{Api}/series", SeerrAdd(Firefly, "Firefly", hdProfile, root.Path, requested: [1]));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var versionId = (await added.ReadJsonAsync()).GetProperty("id").GetInt32();
        Assert.Contains(versionId, Ids(await (await seerr.GetAsync($"{Api}/series")).ReadJsonAsync()));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            await db.Series.Where(s => s.TvdbId == Firefly).ExecuteUpdateAsync(s => s.SetProperty(x => x.TvdbId, (int?)null));
        }

        Assert.DoesNotContain(versionId, Ids(await (await seerr.GetAsync($"{Api}/series")).ReadJsonAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await seerr.GetAsync($"{Api}/series/{versionId}")).StatusCode);
    }

    [Fact]
    public async Task Prowlarr_pushes_indexers_as_to_a_sonarr_and_one_row_serves_both_apps()
    {
        var session = await SessionAsync(factory);
        var (prowlarr, _) = await KeyClientAsync(factory, session, "prowlarr-sonarr", "default", header: true);

        var schemaResponse = await prowlarr.GetAsync($"{Api}/indexer/schema");
        Assert.Equal(SonarrCompat.ApplicationVersion, schemaResponse.Headers.GetValues(RadarrCompat.VersionHeader).Single());
        var schema = await schemaResponse.ReadJsonAsync();
        foreach (var implementation in new[] { "Torznab", "Newznab" })
        {
            var fields = schema.EnumerateArray().Single(s => s.GetProperty("implementation").GetString() == implementation).GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);
            Assert.All(new[] { "baseUrl", "apiPath", "apiKey", "categories", "animeCategories", "animeStandardFormatSearch", "additionalParameters" }, name => Assert.True(fields.ContainsKey(name), name));
            Assert.Equal([5070], fields["animeCategories"].GetProperty("value").EnumerateArray().Select(c => c.GetInt32()));
            Assert.Contains(5030, fields["categories"].GetProperty("value").EnumerateArray().Select(c => c.GetInt32()));
            Assert.Equal(implementation == "Torznab", fields.ContainsKey("seedCriteria.seasonPackSeedTime"));
        }

        // The same Prowlarr indexer pushed to the Radarr app, then to the Sonarr app: one row, each app's categories kept.
        var radarr = await prowlarr.PostAsJsonAsync("/api/v3/indexer", Pushed(prowlarrId: 42, ("categories", new[] { 2000, 2040 })));
        Assert.Equal(HttpStatusCode.Created, radarr.StatusCode);
        var id = (await radarr.ReadJsonAsync()).GetProperty("id").GetInt32();
        var sonarr = await prowlarr.PostAsJsonAsync($"{Api}/indexer", Pushed(prowlarrId: 42, ("categories", new[] { 5000, 5040 }), ("animeCategories", new[] { 5070 })));
        Assert.Equal(HttpStatusCode.Created, sonarr.StatusCode);
        Assert.Equal(id, (await sonarr.ReadJsonAsync()).GetProperty("id").GetInt32());

        var stored = (await session.GetFromJsonAsync<IndexerResponse>($"/api/v1/indexers/{id}", Json))!;
        Assert.Equal([2000, 2040], stored.Categories);
        Assert.Equal([5000, 5040], stored.TvCategories);
        Assert.Equal([5070], stored.AnimeCategories);

        // Each app reads back its own categories (Prowlarr compares them to decide whether to re-PUT), anime fields echoed.
        var asSonarr = Fields(await (await prowlarr.GetAsync($"{Api}/indexer/{id}")).ReadJsonAsync());
        Assert.Equal([5000, 5040], asSonarr["categories"].GetProperty("value").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Equal([5070], asSonarr["animeCategories"].GetProperty("value").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Equal("http://prowlarr:9696/42/", asSonarr["baseUrl"].GetProperty("value").GetString());
        Assert.True(asSonarr.ContainsKey("seedCriteria.seasonPackSeedTime"));
        Assert.False(asSonarr["animeStandardFormatSearch"].GetProperty("value").GetBoolean());
        var asRadarr = Fields(await (await prowlarr.GetAsync($"/api/v3/indexer/{id}")).ReadJsonAsync());
        Assert.Equal([2000, 2040], asRadarr["categories"].GetProperty("value").EnumerateArray().Select(c => c.GetInt32()));
        Assert.False(asRadarr.ContainsKey("animeCategories"));

        // A later Sonarr sync updates the series categories only; a Radarr one the film ones only.
        Assert.Equal(HttpStatusCode.OK, (await prowlarr.PutAsJsonAsync($"{Api}/indexer/{id}", Pushed(prowlarrId: 42, ("categories", new[] { 5000 }), ("animeCategories", Array.Empty<int>())))).StatusCode);
        stored = (await session.GetFromJsonAsync<IndexerResponse>($"/api/v1/indexers/{id}", Json))!;
        Assert.Equal([2000, 2040], stored.Categories);
        Assert.Equal([5000], stored.TvCategories);
        Assert.Empty(stored.AnimeCategories!);

        // The application test: unreachable here, so a 400, with the version header Prowlarr checks (≥ 3.0.5.0).
        var test = await prowlarr.PostAsJsonAsync($"{Api}/indexer/test", new { id = 0, name = "Test", implementation = "Newznab", configContract = "NewznabSettings", fields = new object[] { new { name = "baseUrl", value = "http://127.0.0.1:1/0/" }, new { name = "apiPath", value = "/api" }, new { name = "apiKey", value = "k" }, new { name = "categories", value = new[] { 5000 } } } });
        Assert.Equal(HttpStatusCode.BadRequest, test.StatusCode);
        Assert.True(new Version(test.Headers.GetValues(RadarrCompat.VersionHeader).Single()) >= new Version(3, 0, 5, 0));

        Assert.Equal(HttpStatusCode.OK, (await prowlarr.DeleteAsync($"{Api}/indexer/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await prowlarr.GetAsync($"{Api}/indexer/{id}")).StatusCode);
    }

    /// <summary>§7b.6 point 12: a show with nothing but a title still gives every key Bazarr indexes, empty rather than absent.</summary>
    [Fact]
    public void The_series_mapper_is_total()
    {
        var root = new RootFolder { Id = 1, Path = "/library/tv", Kind = MediaKind.Series };
        var show = new Bigarr.Domain.Tv.Series { Id = 1, Title = "Bare", SortTitle = "bare", TvdbId = 1 };
        var version = new Bigarr.Domain.Tv.SeriesVersion { Id = 5, Name = "1080p", FolderName = "Bare", Series = show, RootFolder = root, QualityProfileId = 1, IsDefault = true };
        show.Versions.Add(version);

        var json = JsonSerializer.SerializeToElement(SonarrMapper.ToSeries(new SonarrRow(show, version), new Dictionary<(int, int), SeasonFigures>()), RadarrCompat.Json);
        foreach (var key in new[] { "id", "title", "sortTitle", "year", "seriesType", "monitored", "path", "tvdbId", "alternateTitles", "images", "tags", "seasons", "statistics", "titleSlug" })
        {
            Assert.True(json.TryGetProperty(key, out var value), key);
            Assert.NotEqual(JsonValueKind.Null, value.ValueKind);
        }

        Assert.Equal(JsonValueKind.Array, json.GetProperty("alternateTitles").ValueKind);
        Assert.Equal(0, json.GetProperty("year").GetInt32());
        Assert.Equal("standard", json.GetProperty("seriesType").GetString());
        Assert.Equal("/library/tv/Bare", json.GetProperty("path").GetString());

        // An episode file without a known quality still has the name Bazarr splits.
        var file = new Bigarr.Domain.Tv.EpisodeFile { Id = 9, VersionId = 5, RelativePath = "Season 1/Bare - S01E01.mkv", SeasonNumber = 1, Size = 30_000 };
        var fileJson = JsonSerializer.SerializeToElement(SonarrMapper.ToEpisodeFile(file, null, "/library/tv/Bare"), RadarrCompat.Json);
        Assert.Equal("Unknown", fileJson.GetProperty("quality").GetProperty("quality").GetProperty("name").GetString());
        Assert.Equal("/library/tv/Bare/Season 1/Bare - S01E01.mkv", fileJson.GetProperty("path").GetString());
        Assert.Equal(30_000, fileJson.GetProperty("size").GetInt64());
        Assert.Equal(9, fileJson.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Queue_record_ids_are_stable_and_distinct_per_episode()
    {
        Assert.Equal(SonarrQueueEndpoints.RecordId(12, 345), SonarrQueueEndpoints.RecordId(12, 345));
        var ids = Enumerable.Range(1, 200).SelectMany(g => Enumerable.Range(1, 50).Select(e => SonarrQueueEndpoints.RecordId(g, e))).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, i => Assert.True(i >= 0));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    /// <summary>The body Seerr posts (sonarr.ts addSeries), field for field: every season unmonitored but the requested ones.</summary>
    private static object SeerrAdd(int tvdbId, string title, int profileId, string rootFolderPath, int[] requested) => new
    {
        tvdbId,
        title,
        qualityProfileId = profileId,
        seasons = Enumerable.Range(0, 6).Select(n => new { seasonNumber = n, monitored = requested.Contains(n) }).ToArray(),
        tags = Array.Empty<int>(),
        seasonFolder = true,
        monitored = true,
        monitorNewItems = "all",
        rootFolderPath,
        seriesType = tvdbId == DemonSlayer ? "anime" : "standard",
        addOptions = new { ignoreEpisodesWithFiles = true, searchForMissingEpisodes = false },
    };

    /// <summary>Seerr's PUT: the lookup resource with <c>monitored</c>, the season and the user's tag switched on.</summary>
    private static Dictionary<string, object?> Reshaped(JsonElement resource, int season, int tagId)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(resource.GetRawText())!;
        body["monitored"] = true;
        body["tags"] = resource.GetProperty("tags").EnumerateArray().Select(t => t.GetInt32()).Append(tagId).Distinct().ToArray();
        body["seasons"] = resource.GetProperty("seasons").EnumerateArray().Select(s => new
        {
            seasonNumber = s.GetProperty("seasonNumber").GetInt32(),
            monitored = s.GetProperty("seasonNumber").GetInt32() == season || s.GetProperty("monitored").GetBoolean(),
        }).ToArray();
        return body;
    }

    private static object Pushed(int prowlarrId, params (string Name, int[] Value)[] categories) => new
    {
        id = 0,
        name = "Nyaa.si (Prowlarr)",
        implementation = "Torznab",
        configContract = "TorznabSettings",
        enableRss = true,
        enableAutomaticSearch = true,
        enableInteractiveSearch = true,
        priority = 25,
        tags = Array.Empty<int>(),
        fields = new object[]
        {
            new { name = "baseUrl", value = $"http://prowlarr:9696/{prowlarrId}/" },
            new { name = "apiPath", value = "/api" },
            new { name = "apiKey", value = "prowlarr-key" },
            new { name = "minimumSeeders", value = 1 },
        }.Concat(categories.Select(c => (object)new { name = c.Name, value = c.Value })).ToArray(),
    };

    private static Dictionary<string, JsonElement> Fields(JsonElement indexer) =>
        indexer.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);

    private static List<int> Ids(JsonElement list) => list.EnumerateArray().Select(s => s.GetProperty("id").GetInt32()).ToList();

    private static Dictionary<int, bool> Seasons(JsonElement series) =>
        series.GetProperty("seasons").EnumerateArray().ToDictionary(s => s.GetProperty("seasonNumber").GetInt32(), s => s.GetProperty("monitored").GetBoolean());

    private static async Task<List<(int Id, int Season, bool Monitored)>> EpisodesAsync(HttpClient client, int seriesId)
    {
        var response = await client.GetAsync($"{Api}/episode?seriesId={seriesId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).EnumerateArray()
            .Select(e => (e.GetProperty("id").GetInt32(), e.GetProperty("seasonNumber").GetInt32(), e.GetProperty("monitored").GetBoolean()))
            .ToList();
    }

    private static async Task<RootFolderResponse> SeriesRootAsync(HttpClient session)
    {
        var roots = (await session.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!.Where(r => r.Kind == MediaKind.Series).ToList();
        if (roots.Count > 0)
        {
            return roots[0];
        }

        var dir = Directory.CreateTempSubdirectory("bigarr-sonarr-compat-").FullName;
        var created = await session.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir, MediaKind.Series));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }

    private static async Task<(int Hd, int Uhd)> SeriesProfilesAsync(HttpClient session)
    {
        var profiles = (await session.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        return (profiles.Single(p => p.Name == QualitySeeder.SeriesHdProfileName).Id, profiles.Single(p => p.Name == QualitySeeder.SeriesUhdProfileName).Id);
    }

    private static async Task DeleteShowAsync(HttpClient session, int tvdbId)
    {
        foreach (var show in (await session.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!.Where(s => s.TvdbId == tvdbId))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/v1/series/{show.Id}")).StatusCode);
        }
    }
}
