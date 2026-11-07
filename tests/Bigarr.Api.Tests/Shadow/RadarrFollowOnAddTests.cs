using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// A film added to Radarr (by Seerr, typically) after the follow started: Bigarr judges Radarr's grab against the film as
/// it was before the download, runs its own search on add, and pairs the two; Radarr's webhook wakes that pass at once.
/// </summary>
public sealed class RadarrFollowOnAddTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Alien = 348;
    private const int Inception = 27205;
    private const int Coco = 354912;

    [Fact]
    public async Task A_film_added_to_radarr_is_judged_as_it_was_and_searched_like_bigarr_would_on_add()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-follow-add-").FullName;
        var root = await AddRootAsync(client, library);
        var indexer = await AddMockIndexerAsync(client, "Mock on add");
        await using var radarr = await FakeRadarr.StartAsync();

        // Radarr's back catalogue: brought in by the follow, never searched.
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Alien", Year = 1979, TmdbId = Alien, Added = DateTime.UtcNow.AddYears(-2) });

        try
        {
            var follow = await FollowAsync(client, radarr, hd, root.Id);
            Assert.Contains("1 film(s) added", (await SyncAsync(client, follow.Id)).Summary, StringComparison.Ordinal);
            Assert.False(await PicksAsync(Alien));

            // Seerr adds Inception: Radarr's search on add grabs a release and imports it before Bigarr's next pass.
            var now = DateTime.UtcNow;
            var inception = new FakeRadarr.Film { Id = 2, Title = "Inception", Year = 2010, TmdbId = Inception, Added = now, File = new(20, "Inception.2010.1080p.BluRay.x264-SPARKS.mkv", 7, "Bluray-1080p", 3072, "SPARKS") };
            radarr.Films.Add(inception);
            Write(library, inception);
            radarr.History.Add(new FakeRadarr.Line(301, 2, "grabbed", now.AddSeconds(5), "Inception.2010.1080p.BluRay.x264-SPARKS", 7, "Bluray-1080p", "INCEPTION01", new()
            {
                ["indexer"] = "Mock on add",
                ["guid"] = "https://indexer.invalid/details/301",
                ["size"] = "11702117171",
                ["releaseSource"] = "Search",
                ["customFormatScore"] = "0",
            }));
            radarr.History.Add(new FakeRadarr.Line(302, 2, "downloadFolderImported", now.AddMinutes(4), "Inception.2010.1080p.BluRay.x264-SPARKS", 7, "Bluray-1080p", "INCEPTION01", new() { ["fileId"] = "20" }));

            var pass = await SyncAsync(client, follow.Id);
            Assert.Contains("1 film(s) added", pass.Summary, StringComparison.Ordinal);
            Assert.Contains("1 film(s) searched on add", pass.Summary, StringComparison.Ordinal);
            Assert.Equal(1, pass.SearchedOnAdd);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();

                // Judged against the film Bigarr now has, as it was before Radarr's download: no file then.
                var grab = await db.RadarrGrabs.AsNoTracking().SingleAsync(g => g.TmdbId == Inception);
                Assert.Equal(RadarrGrabState.Imported, grab.State);
                Assert.Null(grab.BigarrVerdict!.Unmatched);
                Assert.True(grab.BigarrVerdict.Approved, string.Join(" · ", grab.BigarrVerdict.Rejections));
                Assert.Null(grab.BigarrVerdict.CurrentQuality);

                // Bigarr's own search on add, recorded as a pick; then the file Radarr imported followed.
                var pick = await db.ShadowPicks.AsNoTracking().SingleAsync(p => p.TmdbId == Inception);
                Assert.Equal("search:added", pick.Trigger);
                var version = await db.MovieVersions.AsNoTracking().Include(v => v.File).SingleAsync(v => v.Movie.TmdbId == Inception && v.Name == "1080p");
                Assert.Equal("Inception.2010.1080p.BluRay.x264-SPARKS.mkv", version.File!.RelativePath);
                Assert.False(await db.Grabs.AnyAsync());
            }

            // The film page's comparison: Radarr's grab and Bigarr's search on add, side by side.
            var compare = (await client.GetFromJsonAsync<CompareResponse>($"/api/v1/shadow/compare?days=0&tmdbId={Inception}", Json))!;
            var row = Assert.Single(compare.Rows);
            Assert.Contains(row.Outcome, new[] { CompareOutcome.Same, CompareOutcome.Different });
            Assert.Equal("search:added", row.Bigarr!.Trigger);
            Assert.Equal(1, compare.Summary.Total);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            TryDelete(library);
        }
    }

    [Fact]
    public async Task Radarrs_webhook_wakes_a_pass_and_its_test_event_only_says_hello()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-follow-webhook-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Coco", Year = 2017, TmdbId = Coco });

        try
        {
            var follow = await FollowAsync(client, radarr, hd, root.Id);
            var url = $"/api/v1/shadow/radarrs/{follow.Id}/webhook";

            // Radarr's webhook carries a Bigarr API key in its URL; without one it is refused.
            var anonymous = factory.CreateAnonymousClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(url, Body("Test"))).StatusCode);
            var created = await client.PostAsJsonAsync("/api/v1/apikeys", new CreateApiKeyRequest("Radarr webhook", "default"), Json);
            var key = (await created.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>(Json))!.Key;

            // Radarr's Test button: noted, no pass.
            Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsync($"{url}?apikey={key}", Body("Test"))).StatusCode);
            await Task.Delay(300);
            var tested = await GetAsync(client, follow.Id);
            Assert.Equal("Test", tested.LastWebhookEvent);
            Assert.NotNull(tested.LastWebhookAt);
            Assert.Null(tested.LastSyncAt);

            // A film added to Radarr: the pass runs within seconds, without waiting for the schedule.
            Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsync($"{url}?apikey={key}", Body("MovieAdded"))).StatusCode);
            var synced = await WaitUntilAsync(async () => (await GetAsync(client, follow.Id)) is { LastSyncAt: not null } f ? f : null, "the pass the webhook asked for");
            Assert.Equal("MovieAdded", synced.LastWebhookEvent);
            Assert.Contains("1 film(s) added", synced.LastSummary, StringComparison.Ordinal);

            // A body that is not Radarr's still wakes it; an unknown Radarr is a 404.
            Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsync($"{url}?apikey={key}", new StringContent("not json", Encoding.UTF8, "text/plain"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsync($"/api/v1/shadow/radarrs/9999/webhook?apikey={key}", Body("Grab"))).StatusCode);
        }
        finally
        {
            TryDelete(library);
        }
    }

    private static StringContent Body(string eventType) =>
        new($$"""{"eventType":"{{eventType}}","movie":{"id":1,"title":"Whatever","tmdbId":1},"instanceName":"Radarr"}""", Encoding.UTF8, "application/json");

    private static async Task<FollowedRadarrResponse> FollowAsync(HttpClient client, FakeRadarr radarr, int profileId, int rootId)
    {
        var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
            $"Radarr {Guid.NewGuid():N}"[..20], radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = profileId }, new() { [FakeRadarr.RootPath] = rootId }), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
    }

    private static async Task<FollowedRadarrResponse> GetAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<FollowedRadarrResponse>($"/api/v1/shadow/radarrs/{id}", Json))!;

    private static async Task<RadarrFollowPass> SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/radarrs/{id}/sync", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return System.Text.Json.JsonSerializer.Deserialize<RadarrFollowPass>(body, Json)!;
    }

    private async Task<bool> PicksAsync(int tmdbId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().ShadowPicks.AnyAsync(p => p.TmdbId == tmdbId);
    }

    private static void Write(string library, FakeRadarr.Film film)
    {
        var folder = Directory.CreateDirectory(Path.Combine(library, film.Folder)).FullName;
        File.WriteAllBytes(Path.Combine(folder, film.File!.RelativePath), new byte[film.File.Size]);
    }
}
