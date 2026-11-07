using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// A film TMDB only knows in cinemas, long ago: Radarr counts it as released 90 days after its cinema release, and so
/// does Bigarr, so the grab Radarr makes is one Bigarr would make too, and the film is wanted on both sides.
/// </summary>
public sealed class RadarrAvailabilityTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Playtime = 10227;

    [Fact]
    public async Task A_film_with_only_a_cinema_date_is_available_like_in_radarr()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-cinema-only-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Playtime", Year = 1967, TmdbId = Playtime, CinemaOnly = true });

        try
        {
            var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
                "Radarr", radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var follow = (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
            Assert.Contains("1 film(s) added", (await SyncAsync(client, follow.Id)).Summary, StringComparison.Ordinal);

            // Wanted in Bigarr, as it is in Radarr.
            var wanted = (await client.GetFromJsonAsync<WantedPageResponse>("/api/v1/wanted/?filter=missing", Json))!;
            Assert.Contains(wanted.Items, i => i.MovieTitle == "Playtime");

            // Radarr's RSS grab: Bigarr's engine approves it instead of waiting for a release date TMDB will never have.
            radarr.History.Add(new FakeRadarr.Line(401, 1, "grabbed", DateTime.UtcNow, "Playtime.1967.1080p.BluRay.x264-AMIABLE", 7, "Bluray-1080p", "PLAYTIME01", new()
            {
                ["indexer"] = "Some Indexer (Prowlarr)",
                ["guid"] = "https://indexer.invalid/details/401",
                ["size"] = "9702117171",
                ["releaseSource"] = "Rss",
                ["customFormatScore"] = "0",
            }));
            Assert.Contains("1 new grab(s)", (await SyncAsync(client, follow.Id)).Summary, StringComparison.Ordinal);

            using var scope = factory.Services.CreateScope();
            var grab = await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().RadarrGrabs.AsNoTracking().SingleAsync(g => g.TmdbId == Playtime);
            Assert.True(grab.BigarrVerdict!.Approved, string.Join(" · ", grab.BigarrVerdict.Rejections));
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
