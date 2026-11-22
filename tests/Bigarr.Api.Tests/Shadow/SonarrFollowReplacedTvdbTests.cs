using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Migration;
using Bigarr.Api.Tests.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// TVDB deleted a show and recreated it under a new id; Bigarr's refresh took the new id, the followed Sonarr kept the
/// old one. Sonarr's grabs of the show are still judged against it, linked to it and shown on its Compare page.
/// </summary>
public sealed class SonarrFollowReplacedTvdbTests(SonarrShadowApiFactory factory) : IClassFixture<SonarrShadowApiFactory>
{
    private const int OldTvdbId = 229011;
    private const int NewTvdbId = 475214;
    private static readonly DateTime Now = SeriesEndpointsTests.Now.UtcDateTime;

    [Fact]
    public async Task A_grab_of_a_show_whose_tvdb_id_changed_in_bigarr_is_judged_linked_and_compared()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await SonarrImportTests.SeriesProfilesAsync(client);
        var tv = await SonarrImportTests.SeriesRootAsync(client, "bigarr-follow-replaced-");
        await using var sonarr = await FakeSonarr.StartAsync();

        var show = new FakeSonarr.Show { Id = 5, Title = "Midnight Diner", Year = 2009, TvdbId = OldTvdbId, TmdbId = 45923, ImdbId = "tt1494191" };
        show.Seasons[1] = true;
        for (var n = 1; n <= 3; n++)
        {
            show.Episodes.Add(new FakeSonarr.Ep { Id = 5000 + n, Season = 1, Number = n, Title = $"Episode {n}", AirDate = new DateOnly(2009, 10, 9).AddDays(7 * (n - 1)) });
        }

        sonarr.Series.Add(show);
        var created = await client.PostAsJsonAsync("/api/v1/shadow/sonarrs", new FollowedSonarrRequest(
            "Sonarr replaced", sonarr.Url, FakeSonarr.Key, "1080p", new() { [1] = hd }, new() { [FakeSonarr.RootPath] = tv.Id }), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var follow = (await created.Content.ReadFromJsonAsync<FollowedSonarrResponse>(Json))!;
        Assert.Contains("1 series added", (await SyncAsync(client, follow.Id)).Summary, StringComparison.Ordinal);

        // Bigarr's refresh followed TVDB to the new id; Sonarr did not.
        int seriesId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var series = await db.Series.SingleAsync(s => s.TvdbId == OldTvdbId);
            series.TvdbId = NewTvdbId;
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        sonarr.History.Add(new(400, show.Id, sonarr.Episode(show, 1, 2).Id, "grabbed", Now.AddMinutes(5), "Midnight.Diner.S01E02.1080p.WEB-DL.AAC2.0.H.264-GRP", 3, "WEBDL-1080p", "MIDNIGHT02",
            new() { ["indexer"] = "Mock", ["releaseType"] = "SingleEpisode", ["tvdbId"] = OldTvdbId.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        var pass = await SyncAsync(client, follow.Id);
        Assert.DoesNotContain("failed", pass.Summary, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Equal(1, await db.Series.CountAsync(s => s.Title == "Midnight Diner"));
            var grab = await db.SonarrGrabs.AsNoTracking().Include(g => g.Episodes).SingleAsync(g => g.SonarrHistoryId == 400);
            Assert.Equal(OldTvdbId, grab.TvdbId);
            Assert.Equal(seriesId, grab.SeriesId);
            Assert.NotNull(grab.VersionId);
            Assert.NotNull(Assert.Single(grab.Episodes).EpisodeId);
            Assert.NotNull(grab.BigarrVerdict);
            Assert.Null(grab.BigarrVerdict.Unmatched);
        }

        // The show's own Compare page asks by Bigarr's TVDB id and finds Sonarr's grab under it.
        var compare = (await client.GetFromJsonAsync<SeriesCompareResponse>($"/api/v1/shadow/compare/series?tvdbId={NewTvdbId}", Json))!;
        var row = Assert.Single(compare.Rows);
        Assert.Equal(NewTvdbId, row.TvdbId);
        Assert.Equal(seriesId, row.SeriesId);
        Assert.Single(row.Sonarr);
    }

    private static async Task<SonarrFollowPass> SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/sonarrs/{id}/sync", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SonarrFollowPass>(Json))!;
    }
}
