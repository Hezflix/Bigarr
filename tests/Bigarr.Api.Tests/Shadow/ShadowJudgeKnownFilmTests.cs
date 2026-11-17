using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// Radarr's history line names the film it grabbed for. Bigarr's engine judges that release for that film: a release
/// title in another language than the film's title in Bigarr (and no id from the indexer) is not "another film".
/// </summary>
public sealed class ShadowJudgeKnownFilmTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Andriivka = 1_290_001;
    private const int Other = 1_290_002;

    [Fact]
    public async Task Radarrs_grab_is_judged_for_the_film_radarr_names_whatever_the_title_language()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-known-film-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();

        // The film's title in the metadata language; the release carries the English one and no id.
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "2000 mètres d'Andriïvka", Year = 2025, TmdbId = Andriivka });
        radarr.Films.Add(new FakeRadarr.Film { Id = 2, Title = "Heat", Year = 1995, TmdbId = Other });

        try
        {
            var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
                "Radarr", radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var follow = (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
            Assert.Contains("2 film(s) added", (await SyncAsync(client, follow.Id)).Summary, StringComparison.Ordinal);

            radarr.History.Add(new FakeRadarr.Line(501, 1, "grabbed", DateTime.UtcNow, "2000.Meters.to.Andriivka.2025.1080p.WEB-DL.DDP5.1.H.264-GRP", 3, "WEBDL-1080p", "ANDRIIVKA01", new()
            {
                ["indexer"] = "Some Indexer (Prowlarr)",
                ["guid"] = "https://indexer.invalid/details/501",
                ["size"] = "6702117171",
                ["releaseSource"] = "InteractiveSearch",
            }));

            // An id in the title still settles it: this one says it is another film.
            radarr.History.Add(new FakeRadarr.Line(502, 2, "grabbed", DateTime.UtcNow, "Heat.1995.1080p.BluRay.x264-GRP.[tmdb-1290001]", 7, "Bluray-1080p", "HEAT01", new()
            {
                ["indexer"] = "Some Indexer (Prowlarr)",
                ["guid"] = "https://indexer.invalid/details/502",
                ["size"] = "9702117171",
            }));
            Assert.Contains("2 new grab(s)", (await SyncAsync(client, follow.Id)).Summary, StringComparison.Ordinal);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var grab = await db.RadarrGrabs.AsNoTracking().SingleAsync(g => g.TmdbId == Andriivka);
            Assert.Null(grab.BigarrVerdict!.Unmatched);
            Assert.True(grab.BigarrVerdict.Approved, string.Join(" · ", grab.BigarrVerdict.Rejections));
            Assert.Equal("WEBDL-1080p", grab.BigarrVerdict.Quality);

            var contradicted = await db.RadarrGrabs.AsNoTracking().SingleAsync(g => g.TmdbId == Other);
            Assert.Equal($"Release is TMDB {Andriivka}, not {Other}", contradicted.BigarrVerdict!.Unmatched);
        }
        finally
        {
            TryDelete(library);
        }
    }

    private static async Task<RadarrFollowPass> SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/radarrs/{id}/sync", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<RadarrFollowPass>(body, Json)!;
    }
}
