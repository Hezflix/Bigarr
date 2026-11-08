using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// Shadow mode: a pick is what Bigarr would have grabbed, so grab decisions take a version's latest pick as the file it
/// would have become. The same release seen again elsewhere is not picked a second time, and a Radarr grab made after
/// the pick is judged against it, so the comparison pairs Radarr's grab with the pick that counted.
/// </summary>
public sealed class ShadowPickAsFileTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Interstellar = 157336;

    [Fact]
    public async Task Later_releases_and_radarrs_grab_are_judged_against_the_latest_pick()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-pick-as-file-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Interstellar", Year = 2014, TmdbId = Interstellar });
        var first = await AddMockIndexerAsync(client, "Mock first");
        IndexerResponse? second = null;

        try
        {
            // The follow brings the film in, without a file.
            var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
                "Radarr", radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var follow = (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
            await SyncAsync(client, follow.Id);

            // The RSS pass picks the best of the feed.
            Assert.Equal(1, (await RssAsync(client)).Picked);
            var pick = await PickAsync();
            Assert.NotNull(pick.Quality);

            // The same releases from another indexer: judged against the pick, none is picked again.
            second = await AddMockIndexerAsync(client, "Mock second");
            Assert.Equal(0, (await RssAsync(client)).Picked);
            var again = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases?matched=true", Json))!
                .Where(r => r.IndexerId == second.Id && r.Movie is not null && r.Title == pick.ReleaseTitle).ToList();
            var sameRelease = Assert.Single(again);
            Assert.Null(sameRelease.AttributedVersionId);
            Assert.Contains(Assert.Single(sameRelease.Versions).Rejections, r => r.Reason.Contains($"picked {pick.Quality}", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(pick.Id, (await PickAsync()).Id);

            // Radarr grabs that same release afterwards: Bigarr had it already, so it would not take it again.
            radarr.History.Add(new FakeRadarr.Line(501, 1, "grabbed", DateTime.UtcNow, pick.ReleaseTitle, pick.QualityId ?? 7, pick.Quality!, "INTERSTELLAR01", new()
            {
                ["indexer"] = "Mock first",
                ["guid"] = pick.ReleaseGuid,
                ["size"] = pick.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["releaseSource"] = "Rss",
            }));
            await SyncAsync(client, follow.Id);
            using (var scope = factory.Services.CreateScope())
            {
                var grab = await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().RadarrGrabs.AsNoTracking().SingleAsync(g => g.TmdbId == Interstellar);
                var verdict = grab.BigarrVerdict!;
                Assert.False(verdict.Approved);
                Assert.True(verdict.CurrentIsPick);
                Assert.Equal(pick.Quality, verdict.CurrentQuality);
                Assert.Contains(verdict.Rejections, r => r.Contains($"picked {pick.Quality}", StringComparison.OrdinalIgnoreCase));
            }

            // Paired with the pick that counted: the same release, Bigarr first.
            var compare = (await client.GetFromJsonAsync<CompareResponse>($"/api/v1/shadow/compare?days=0&tmdbId={Interstellar}", Json))!;
            var row = Assert.Single(compare.Rows);
            Assert.Equal(CompareOutcome.Same, row.Outcome);
            Assert.Equal(pick.Id, row.Bigarr!.Id);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{first.Id}");
            if (second is not null)
            {
                await client.DeleteAsync($"/api/v1/indexers/{second.Id}");
            }

            TryDelete(library);
        }
    }

    private async Task<ShadowPick> PickAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().ShadowPicks.AsNoTracking().SingleAsync(p => p.TmdbId == Interstellar);
    }

    private static async Task<RssSyncResponse> RssAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/rss/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RssSyncResponse>(Json))!;
    }

    private static async Task<RadarrFollowPass> SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/radarrs/{id}/sync", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<RadarrFollowPass>(body, Json)!;
    }
}
