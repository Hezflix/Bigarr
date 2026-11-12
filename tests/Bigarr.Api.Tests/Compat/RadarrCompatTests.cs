using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Tests.Auth;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Compat;

/// <summary>
/// The M7 acceptance: one Seerr with two Radarr entries on the same Bigarr, one key per version, adds
/// the same film twice and follows two versions without noticing; Bazarr's <c>all</c> key lists both lines.
/// </summary>
public sealed class RadarrCompatTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task The_compat_api_is_key_only_and_announces_a_recent_radarr()
    {
        var session = await SessionAsync(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/v3/system/status")).StatusCode); // a cookie never opens /api/v3
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateAnonymousClient().GetAsync("/api/v3/system/status")).StatusCode);

        var (byQuery, key) = await KeyClientAsync(factory, session, "status-query", "default");
        var status = await byQuery.GetAsync("/api/v3/system/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(RadarrCompat.ApplicationVersion, status.Headers.GetValues(RadarrCompat.VersionHeader).Single());
        var body = await status.ReadJsonAsync();
        Assert.Equal(RadarrCompat.ApplicationVersion, body.GetProperty("version").GetString());
        Assert.Equal(string.Empty, body.GetProperty("urlBase").GetString());
        Assert.Equal("Radarr", body.GetProperty("appName").GetString());
        Assert.Equal("Bigarr", body.GetProperty("instanceName").GetString());
        Assert.True(Version.Parse(RadarrCompat.ApplicationVersion) >= new Version(4, 0, 4, 0)); // Prowlarr's floor

        // Bazarr probes the Radarr 0.x path first and switches to v3 on a 404; Prowlarr sends the key as a header.
        Assert.Equal(HttpStatusCode.NotFound, (await byQuery.GetAsync("/api/system/status")).StatusCode);
        var byHeader = factory.CreateAnonymousClient();
        byHeader.DefaultRequestHeaders.Add("X-Api-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await byHeader.GetAsync("/api/v3/system/status")).StatusCode);
    }

    [Fact]
    public async Task One_seerr_with_two_radarr_entries_follows_two_versions_of_the_same_film()
    {
        var session = await SessionAsync(factory);
        var (hd, uhd, root) = await SetupAsync(session);
        var (seerrHd, _) = await KeyClientAsync(factory, session, "seerr-1080p", "version:1080p");
        var (seerr4k, _) = await KeyClientAsync(factory, session, "seerr-4k", "version:4k");
        var (bazarr, _) = await KeyClientAsync(factory, session, "bazarr", "all");
        var (plain, _) = await KeyClientAsync(factory, session, "plain", "default");

        // Seerr's existence test: nothing in the library yet, id must be 0 (falsy) so it POSTs.
        var lookup = await (await seerrHd.GetAsync("/api/v3/movie/lookup?term=tmdb:603")).ReadJsonAsync();
        var hit = Assert.Single(lookup.EnumerateArray());
        Assert.Equal(0, hit.GetProperty("id").GetInt32());
        Assert.False(hit.GetProperty("hasFile").GetBoolean());
        Assert.Equal("The Matrix", hit.GetProperty("title").GetString());
        Assert.Equal("603", hit.GetProperty("titleSlug").GetString());

        // The 1080p Seerr adds the film: a new Movie with a default version named after the key's view.
        var added = await seerrHd.PostAsJsonAsync("/api/v3/movie", SeerrAdd(603, "The Matrix", 1999, hd, root.Path));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var hdRow = await added.ReadJsonAsync();
        var hdId = hdRow.GetProperty("id").GetInt32();
        Assert.True(hdId > 0);
        Assert.True(hdRow.GetProperty("monitored").GetBoolean());
        Assert.Equal(603, hdRow.GetProperty("tmdbId").GetInt32());
        Assert.Equal(hd, hdRow.GetProperty("qualityProfileId").GetInt32());
        Assert.Equal(Path.Combine(root.Path, "The Matrix (1999)"), hdRow.GetProperty("path").GetString());
        Assert.False(hdRow.TryGetProperty("movieFile", out _), "no file: the key must be absent, not null (Bazarr checks 'movieFile' in movie)");

        // The 4K Seerr looks the film up: it exists, but not for this key.
        var lookup4k = await (await seerr4k.GetAsync("/api/v3/movie/lookup?term=tmdb:603")).ReadJsonAsync();
        Assert.Equal(0, Assert.Single(lookup4k.EnumerateArray()).GetProperty("id").GetInt32());

        // So it POSTs too: same film, a second managed version, a different id.
        var added4k = await seerr4k.PostAsJsonAsync("/api/v3/movie", SeerrAdd(603, "The Matrix", 1999, uhd, root.Path));
        Assert.Equal(HttpStatusCode.Created, added4k.StatusCode);
        var fourKId = (await added4k.ReadJsonAsync()).GetProperty("id").GetInt32();
        Assert.NotEqual(hdId, fourKId);

        var movie = (await session.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 603);
        Assert.Equal(["1080p", "4k"], movie.Versions.Select(v => v.Name).Order());
        Assert.Equal(hdId, movie.Versions.Single(v => v.IsDefault).Id);
        Assert.Equal(uhd, movie.Versions.Single(v => v.Name == "4k").QualityProfileId);

        // Each Seerr now finds its own id, and only its own line.
        Assert.Equal(hdId, Assert.Single((await (await seerrHd.GetAsync("/api/v3/movie/lookup?term=tmdb:603")).ReadJsonAsync()).EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Equal(fourKId, Assert.Single((await (await seerr4k.GetAsync("/api/v3/movie/lookup?term=tmdb:603")).ReadJsonAsync()).EnumerateArray()).GetProperty("id").GetInt32());
        // Other films of the library may have a 1080p too: Radarr's tmdbId filter narrows the list to this one.
        Assert.Equal([hdId], Ids(await (await seerrHd.GetAsync("/api/v3/movie?tmdbId=603")).ReadJsonAsync()));
        Assert.Equal([fourKId], Ids(await (await seerr4k.GetAsync("/api/v3/movie?tmdbId=603")).ReadJsonAsync()));
        Assert.Equal([hdId], Ids(await (await plain.GetAsync("/api/v3/movie?tmdbId=603")).ReadJsonAsync()));
        Assert.Contains(fourKId, Ids(await (await seerr4k.GetAsync("/api/v3/movie")).ReadJsonAsync()));
        Assert.DoesNotContain(hdId, Ids(await (await seerr4k.GetAsync("/api/v3/movie")).ReadJsonAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await seerrHd.GetAsync($"/api/v3/movie/{fourKId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seerr4k.GetAsync($"/api/v3/movie/{hdId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await seerr4k.GetAsync($"/api/v3/movie/{fourKId}")).StatusCode);

        // Bazarr sees both lines of the same tmdbId, with their own ids and the same folder.
        var all = await (await bazarr.GetAsync("/api/v3/movie")).ReadJsonAsync();
        var rows = all.EnumerateArray().Where(r => r.GetProperty("tmdbId").GetInt32() == 603).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { hdId, fourKId }.Order(), rows.Select(r => r.GetProperty("id").GetInt32()).Order());
        Assert.All(rows, r => Assert.Equal("603", r.GetProperty("titleSlug").GetString()));

        // Adding again through a key that already sees its version fails like Radarr; the all key designates no version.
        var again = await seerrHd.PostAsJsonAsync("/api/v3/movie", SeerrAdd(603, "The Matrix", 1999, hd, root.Path));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("already been added", (await again.ReadJsonAsync())[0].GetProperty("errorMessage").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await bazarr.PostAsJsonAsync("/api/v3/movie", SeerrAdd(603, "The Matrix", 1999, hd, root.Path))).StatusCode);
        var badRoot = await seerr4k.PostAsJsonAsync("/api/v3/movie", SeerrAdd(604, "The Matrix Reloaded", 2003, uhd, "/nowhere"));
        Assert.Equal(HttpStatusCode.BadRequest, badRoot.StatusCode);
        Assert.Equal("RootFolderPath", (await badRoot.ReadJsonAsync())[0].GetProperty("propertyName").GetString());

        // Seerr re-monitors an unmonitored version with PUT /movie and reads monitored back.
        var unmonitor = await session.PutAsJsonAsync($"/api/v1/movies/{movie.Id}/versions/{fourKId}", new UpdateVersionRequest(Monitored: false));
        Assert.Equal(HttpStatusCode.OK, unmonitor.StatusCode);
        Assert.False((await (await seerr4k.GetAsync($"/api/v3/movie/{fourKId}")).ReadJsonAsync()).GetProperty("monitored").GetBoolean());
        var put = await seerr4k.PutAsJsonAsync("/api/v3/movie", new { id = fourKId, title = "The Matrix", qualityProfileId = uhd, profileId = uhd, titleSlug = "603", minimumAvailability = "released", tmdbId = 603, year = 1999, rootFolderPath = root.Path, monitored = true, tags = Array.Empty<int>(), addOptions = new { searchForMovie = false } });
        Assert.Equal(HttpStatusCode.Accepted, put.StatusCode);
        Assert.True((await put.ReadJsonAsync()).GetProperty("monitored").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await seerrHd.PutAsJsonAsync($"/api/v3/movie/{fourKId}", new { id = fourKId, monitored = true })).StatusCode);

        // Commands: search ids are version ids, filtered by the view; unknown parameters and commands behave like Radarr.
        var search = await seerr4k.PostAsJsonAsync("/api/v3/command", new { name = "MoviesSearch", movieIds = new[] { fourKId } });
        Assert.Equal(HttpStatusCode.Created, search.StatusCode);
        var command = await search.ReadJsonAsync();
        Assert.Equal("MoviesSearch", command.GetProperty("name").GetString());
        Assert.Equal("queued", command.GetProperty("status").GetString());
        Assert.Contains("1 version", command.GetProperty("message").GetString());

        // A client that polls the command until it is done gets there once the search has run.
        var commandId = command.GetProperty("id").GetInt32();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var polled = await (await seerr4k.GetAsync($"/api/v3/command/{commandId}")).ReadJsonAsync();
        while (polled.GetProperty("status").GetString() != "completed" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            polled = await (await seerr4k.GetAsync($"/api/v3/command/{commandId}")).ReadJsonAsync();
        }

        Assert.Equal("completed", polled.GetProperty("status").GetString());
        Assert.Equal("successful", polled.GetProperty("result").GetString());
        Assert.NotEqual(JsonValueKind.Null, polled.GetProperty("ended").ValueKind);
        var searchOther = await (await seerrHd.PostAsJsonAsync("/api/v3/command", new { name = "MoviesSearch", movieIds = new[] { fourKId } })).ReadJsonAsync();
        Assert.Contains("0 version", searchOther.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, (await seerr4k.PostAsJsonAsync("/api/v3/command", new { name = "RefreshMonitoredDownloads" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bazarr.PostAsJsonAsync("/api/v3/command", new { name = "RescanMovie", movieId = hdId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await seerr4k.PostAsJsonAsync("/api/v3/command", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await seerr4k.GetAsync($"/api/v3/command/{command.GetProperty("id").GetInt32()}")).StatusCode);

        // Queue and history: paged envelopes, Sonarr's includeEpisode ignored.
        var queue = await (await seerr4k.GetAsync("/api/v3/queue?includeEpisode=true&page=1&pageSize=20")).ReadJsonAsync();
        Assert.Equal(1, queue.GetProperty("page").GetInt32());
        Assert.Equal(0, queue.GetProperty("totalRecords").GetInt32());
        Assert.Empty(queue.GetProperty("records").EnumerateArray());
        var history = await (await bazarr.GetAsync($"/api/v3/history?eventType=1&movieIds={hdId}")).ReadJsonAsync();
        Assert.Equal(0, history.GetProperty("totalRecords").GetInt32());
        Assert.True(history.TryGetProperty("records", out _));

        // Each Seerr deletes its own line; the last one takes the film with it. 404 afterwards, which Seerr tolerates.
        Assert.Equal(HttpStatusCode.NotFound, (await seerrHd.DeleteAsync($"/api/v3/movie/{fourKId}?deleteFiles=true&addImportExclusion=false")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await seerr4k.DeleteAsync($"/api/v3/movie/{fourKId}?deleteFiles=true&addImportExclusion=false")).StatusCode);
        var afterFirst = (await session.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movie.Id}", Json))!;
        Assert.Equal("1080p", Assert.Single(afterFirst.Versions).Name);
        Assert.Equal(HttpStatusCode.OK, (await seerrHd.DeleteAsync($"/api/v3/movie/{hdId}?deleteFiles=true&addImportExclusion=false")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync($"/api/v1/movies/{movie.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seerrHd.DeleteAsync($"/api/v3/movie/{hdId}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_default_line_hands_over_to_the_oldest_managed_sibling()
    {
        var session = await SessionAsync(factory);
        var (hd, uhd, root) = await SetupAsync(session);
        var (plain, _) = await KeyClientAsync(factory, session, "plain-default", "default");
        var (seerr4k, _) = await KeyClientAsync(factory, session, "seerr-4k-handover", "version:4k");

        var hdId = (await (await plain.PostAsJsonAsync("/api/v3/movie", SeerrAdd(27205, "Inception", 2010, hd, root.Path))).ReadJsonAsync()).GetProperty("id").GetInt32();
        var fourKId = (await (await seerr4k.PostAsJsonAsync("/api/v3/movie", SeerrAdd(27205, "Inception", 2010, uhd, root.Path))).ReadJsonAsync()).GetProperty("id").GetInt32();

        // The default key deletes "the film": its default line goes, the 4K becomes the default and the film stays.
        Assert.Equal(HttpStatusCode.OK, (await plain.DeleteAsync($"/api/v3/movie/{hdId}")).StatusCode);
        Assert.Equal([fourKId], Ids(await (await plain.GetAsync("/api/v3/movie?tmdbId=27205")).ReadJsonAsync()));
        var movie = (await session.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 27205);
        Assert.True(movie.Versions.Single().IsDefault);
        Assert.Equal("4k", movie.Versions.Single().Name);
    }

    [Fact]
    public async Task Profiles_root_folders_tags_and_filesystem_have_radarrs_shape()
    {
        var session = await SessionAsync(factory);
        var (hd, _, root) = await SetupAsync(session);
        var (key, _) = await KeyClientAsync(factory, session, "shapes", "default");

        // Seerr spells it qualityProfile, Bazarr qualityprofile: same route.
        var camel = await (await key.GetAsync("/api/v3/qualityProfile")).ReadJsonAsync();
        var lower = await (await key.GetAsync("/api/v3/qualityprofile")).ReadJsonAsync();
        Assert.Equal(camel.GetArrayLength(), lower.GetArrayLength());
        var profile = camel.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == hd);
        Assert.Equal("1080p", profile.GetProperty("name").GetString());
        Assert.Equal("English", profile.GetProperty("language").GetProperty("name").GetString());
        Assert.True(profile.GetProperty("items").GetArrayLength() > 5);
        Assert.Equal(HttpStatusCode.OK, (await key.GetAsync($"/api/v3/qualityprofile/{hd}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await key.GetAsync("/api/v3/qualityprofile/9999")).StatusCode);

        var roots = await (await key.GetAsync("/api/v3/rootfolder")).ReadJsonAsync();
        var folder = roots.EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == root.Id);
        Assert.Equal(root.Path, folder.GetProperty("path").GetString());
        Assert.True(folder.GetProperty("accessible").GetBoolean());

        // Tags: Seerr creates one per user and expects the existing one back on a repeat.
        var created = await key.PostAsJsonAsync("/api/v3/tag", new { label = "Seerr-User-1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tag = await created.ReadJsonAsync();
        var tagId = tag.GetProperty("id").GetInt32();
        Assert.Equal("seerr-user-1", tag.GetProperty("label").GetString());
        Assert.Equal(tagId, (await (await key.PostAsJsonAsync("/api/v3/tag", new { label = "seerr-user-1" })).ReadJsonAsync()).GetProperty("id").GetInt32());
        Assert.Contains((await (await key.GetAsync("/api/v3/tag")).ReadJsonAsync()).EnumerateArray(), t => t.GetProperty("id").GetInt32() == tagId);
        Assert.Equal("renamed", (await (await key.PutAsJsonAsync($"/api/v3/tag/{tagId}", new { id = tagId, label = "Renamed" })).ReadJsonAsync()).GetProperty("label").GetString());

        // A tagged add lands on the version, and the tag shows in both APIs.
        var added = await (await key.PostAsJsonAsync("/api/v3/movie", new { title = "Parasite", qualityProfileId = hd, titleSlug = "496243", minimumAvailability = "announced", tmdbId = 496243, year = 2019, rootFolderPath = root.Path, monitored = true, tags = new[] { tagId }, addOptions = new { searchForMovie = false } })).ReadJsonAsync();
        Assert.Equal([tagId], added.GetProperty("tags").EnumerateArray().Select(t => t.GetInt32()));
        Assert.Equal("announced", added.GetProperty("minimumAvailability").GetString());
        var version = (await session.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{(await session.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 496243).Id}", Json))!.Versions.Single();
        Assert.Equal([tagId], version.Tags);

        Assert.Equal(HttpStatusCode.OK, (await key.DeleteAsync($"/api/v3/tag/{tagId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await key.GetAsync($"/api/v3/tag/{tagId}")).StatusCode);

        // Bazarr's folder browser.
        Directory.CreateDirectory(Path.Combine(root.Path, "Browse Me"));
        var listing = await (await key.GetAsync($"/api/v3/filesystem?path={Uri.EscapeDataString(root.Path)}&allowFoldersWithoutTrailingSlashes=true")).ReadJsonAsync();
        Assert.Contains(listing.GetProperty("directories").EnumerateArray(), d => d.GetProperty("name").GetString() == "Browse Me" && d.GetProperty("type").GetString() == "folder");
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), listing.GetProperty("parent").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await key.GetAsync("/api/v3/filesystem?path=/definitely/not/here")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await key.GetAsync("/api/v3/filesystem")).StatusCode);
    }

    private static IEnumerable<int> Ids(JsonElement array) => array.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).Order();
}
