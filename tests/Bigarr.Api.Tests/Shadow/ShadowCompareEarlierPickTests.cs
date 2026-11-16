using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// Bigarr picked a film, Radarr did nothing; days later Radarr grabbed it. Compare shows one row, Bigarr first, even when
/// the period asked starts after Bigarr's pick.
/// </summary>
public sealed class ShadowCompareEarlierPickTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Film = 1_290_101;

    [Fact]
    public async Task A_pick_days_before_radarrs_grab_shows_as_bigarr_first()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-compare-earlier-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Heat", Year = 1995, TmdbId = Film });

        try
        {
            var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
                "Radarr", radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            var follow = (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
            await SyncAsync(client, follow.Id);

            var now = DateTime.UtcNow;
            await PickAsync("Heat.1995.1080p.WEB-DL.DDP5.1.H.264-NTb", "g-heat-web", now.AddDays(-3));
            radarr.History.Add(new FakeRadarr.Line(601, 1, "grabbed", now, "Heat.1995.1080p.BluRay.x264-SPARKS", 7, "Bluray-1080p", "DL601", new()
            {
                ["indexer"] = "Some Indexer (Prowlarr)",
                ["guid"] = "g-heat-bluray",
                ["size"] = "8589934592",
            }));
            await SyncAsync(client, follow.Id);

            foreach (var days in new[] { 0, 1, 7 })
            {
                var compare = (await client.GetFromJsonAsync<CompareResponse>($"/api/v1/shadow/compare?days={days}", Json))!;
                var row = Assert.Single(compare.Rows);
                Assert.Equal(CompareOutcome.Different, row.Outcome);
                Assert.Equal("Heat.1995.1080p.WEB-DL.DDP5.1.H.264-NTb", row.Bigarr!.Title);
                Assert.InRange(row.BigarrLeadSeconds!.Value, (long)TimeSpan.FromDays(3).TotalSeconds - 60, (long)TimeSpan.FromDays(3).TotalSeconds + 60);
                Assert.Equal(new CompareCounts(0, 1, 0, 0), compare.Summary);
            }
        }
        finally
        {
            TryDelete(library);
        }
    }

    private static async Task SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/radarrs/{id}/sync", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task PickAsync(string title, string guid, DateTime at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var version = await db.MovieVersions.AsNoTracking().Include(v => v.Movie).SingleAsync(v => v.Movie.TmdbId == Film && v.Name == "1080p");
        db.ShadowPicks.Add(new ShadowPick
        {
            VersionId = version.Id,
            MovieId = version.MovieId,
            TmdbId = Film,
            MovieTitle = version.Movie.Title,
            VersionName = version.Name,
            Trigger = "rss",
            PickedAt = at,
            LastPickedAt = at,
            ReleaseTitle = title,
            ReleaseGuid = guid,
        });
        await db.SaveChangesAsync();
    }
}
