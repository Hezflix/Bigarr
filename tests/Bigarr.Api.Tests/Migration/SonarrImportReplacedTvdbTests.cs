using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// TVDB deleted a show and recreated it under a new id; Bigarr's refresh took the new id, Sonarr still lists the old one.
/// The import finds the show by its TMDB id (else its IMDb id) and updates it, instead of adding a second one that the
/// unique TMDB id refuses.
/// </summary>
public sealed class SonarrImportReplacedTvdbTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private const int NewTvdbId = 475214;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_show_whose_tvdb_id_changed_in_bigarr_is_updated_not_added_again(bool byTmdb)
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await SonarrImportTests.SeriesProfilesAsync(client);
        var tv = await SonarrImportTests.SeriesRootAsync(client, "bigarr-sonarr-replaced-");

        await using var sonarr = await FakeSonarr.StartAsync();
        var oldTvdbId = byTmdb ? 229011 : 229012;
        var show = new FakeSonarr.Show
        {
            Id = 1,
            Title = byTmdb ? "Midnight Diner" : "Midnight Diner Tokyo",
            Year = 2009,
            TvdbId = oldTvdbId,
            TmdbId = byTmdb ? 45923 : 0,
            ImdbId = byTmdb ? "tt1494191" : "tt5917876",
        };
        show.Seasons[1] = true;
        show.Episodes.Add(new FakeSonarr.Ep { Id = 1001, Season = 1, Number = 1, Title = "Red Wiener", AirDate = new(2009, 10, 9) });
        sonarr.Series.Add(show);
        var request = new SonarrImportRequest(sonarr.Url, FakeSonarr.Key, "1080p", new() { [1] = hd }, new() { [FakeSonarr.RootPath] = tv.Id }, ImportProfileQualities: false);

        var first = await SonarrImportTests.ImportAsync(client, request);
        Assert.Equal(1, first.SeriesAdded);

        // Bigarr's refresh followed TVDB to the new id; Sonarr did not.
        int seriesId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var series = await db.Series.SingleAsync(s => s.TvdbId == oldTvdbId);
            series.TvdbId = NewTvdbId + (byTmdb ? 0 : 1);
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var preview = (await (await client.PostAsJsonAsync("/api/v1/migration/sonarr/preview", new SonarrImportPreviewRequest(sonarr.Url, FakeSonarr.Key), Json)).Content.ReadFromJsonAsync<SonarrImportPreviewResponse>(Json))!;
        Assert.Equal(1, preview.AlreadyInLibrary);

        var again = await SonarrImportTests.ImportAsync(client, request);
        Assert.True(again.Failed == 0, string.Join(" | ", again.Issues.Select(i => i.Reason)));
        Assert.Equal(0, again.SeriesAdded);
        Assert.Equal(0, again.VersionsAdded);
        Assert.Equal(1, again.VersionsUpdated);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var series = await db.Series.AsNoTracking().Include(s => s.Versions).Include(s => s.Episodes).SingleAsync(s => s.Id == seriesId);
            Assert.Equal(NewTvdbId + (byTmdb ? 0 : 1), series.TvdbId);
            Assert.Single(series.Versions);
            Assert.Single(series.Episodes);
            Assert.False(await db.Series.AnyAsync(s => s.TvdbId == oldTvdbId));
        }
    }
}
