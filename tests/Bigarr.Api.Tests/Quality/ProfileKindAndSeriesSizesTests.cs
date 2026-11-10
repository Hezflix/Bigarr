using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Library;
using Bigarr.Domain.Quality;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Quality;

/// <summary>
/// Quality profiles have a kind (Films, Series, Both): versions only take a profile of their kind, a profile in use keeps a
/// kind its versions fit, the Radarr API lists the film ones, the imports map onto their own kind. Quality definitions carry
/// separate series sizes, seeded from Sonarr's defaults and editable on their own.
/// </summary>
public sealed class ProfileKindAndSeriesSizesTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    [Fact]
    public async Task Versions_only_take_a_profile_of_their_kind_and_a_used_profile_keeps_a_fitting_kind()
    {
        var client = await ClientAsync(factory);
        var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var filmHd = profiles.Single(p => p.Name == QualitySeeder.HdProfileName);
        var seriesHd = profiles.Single(p => p.Name == QualitySeeder.SeriesHdProfileName);
        Assert.Equal(ProfileKind.Movies, filmHd.Kind);
        Assert.Equal(ProfileKind.Series, seriesHd.Kind);
        Assert.Equal(ProfileKind.Series, profiles.Single(p => p.Name == QualitySeeder.SeriesUhdProfileName).Kind);

        var seriesRoot = await RootAsync(client, MediaKind.Series);
        var filmRoot = await RootAsync(client, MediaKind.Movies);

        // A series with a film profile, a film with a series profile: refused with the reason.
        var wrongSeries = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(filmHd.Id, seriesRoot.Id, TvdbId: 81189), Json);
        Assert.Equal(HttpStatusCode.BadRequest, wrongSeries.StatusCode);
        Assert.Contains("is a film profile", await wrongSeries.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var wrongFilm = await client.PostAsJsonAsync("/api/v1/movies", new { tmdbId = 603, qualityProfileId = seriesHd.Id, rootFolderId = filmRoot.Id }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, wrongFilm.StatusCode);
        Assert.Contains("is a series profile", await wrongFilm.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A Both profile fits either.
        var schema = (await client.GetFromJsonAsync<QualityProfileResponse>("/api/v1/qualityprofiles/schema", Json))!;
        var items = schema.Items.Select(i => i with { Allowed = i.QualityIds.Contains(7) }).ToList();
        var bothResponse = await client.PostAsJsonAsync("/api/v1/qualityprofiles", new QualityProfileRequest("Shared", items, 7, Kind: ProfileKind.Both), Json);
        Assert.Equal(HttpStatusCode.Created, bothResponse.StatusCode);
        var both = (await bothResponse.Content.ReadFromJsonAsync<QualityProfileResponse>(Json))!;
        Assert.Equal(ProfileKind.Both, both.Kind);
        var series = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(both.Id, seriesRoot.Id, TvdbId: 81189), Json);
        Assert.Equal(HttpStatusCode.Created, series.StatusCode);

        // In use by a series version, it cannot become Films; it can become Series.
        var toFilms = await client.PutAsJsonAsync($"/api/v1/qualityprofiles/{both.Id}", new QualityProfileRequest("Shared", items, 7, Kind: ProfileKind.Movies), Json);
        Assert.Equal(HttpStatusCode.BadRequest, toFilms.StatusCode);
        Assert.Contains("series version(s) use 'Shared'", await toFilms.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var toSeries = await client.PutAsJsonAsync($"/api/v1/qualityprofiles/{both.Id}", new QualityProfileRequest("Shared", items, 7, Kind: ProfileKind.Series), Json);
        Assert.Equal(HttpStatusCode.OK, toSeries.StatusCode);
        var shared = (await toSeries.Content.ReadFromJsonAsync<QualityProfileResponse>(Json))!;
        Assert.Equal(1, shared.SeriesVersionCount);

        // And it cannot be deleted while a series version uses it.
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/qualityprofiles/{both.Id}")).StatusCode);

        // The Radarr API lists the profiles a film version can take, and hides the others.
        var (radarr, _) = await CompatTestHelpers.KeyClientAsync(factory, client, "kind-check", "all");
        var listed = JsonDocument.Parse(await radarr.GetStringAsync("/api/v3/qualityprofile")).RootElement.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains(QualitySeeder.HdProfileName, listed);
        Assert.DoesNotContain(QualitySeeder.SeriesHdProfileName, listed);
        Assert.DoesNotContain("Shared", listed);
        Assert.Equal(HttpStatusCode.NotFound, (await radarr.GetAsync($"/api/v3/qualityprofile/{seriesHd.Id}")).StatusCode);

        // The Radarr import refuses a series profile in its mapping.
        var radarrImport = await client.PostAsJsonAsync("/api/v1/migration/radarr/import", new RadarrImportRequest("http://radarr.invalid", "key", "1080p", new() { [1] = seriesHd.Id }, new() { ["/movies"] = filmRoot.Id }), Json);
        Assert.Equal(HttpStatusCode.BadRequest, radarrImport.StatusCode);
        Assert.Contains("is a series profile", await radarrImport.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Series_sizes_are_seeded_from_sonarr_and_edited_apart_from_the_film_ones()
    {
        var client = await ClientAsync(factory);
        var definitions = (await client.GetFromJsonAsync<List<QualityDefinitionResponse>>("/api/v1/qualitydefinitions", Json))!;
        var bluray = definitions.Single(d => d.Id == 7);
        Assert.Equal((4d, 95d, 155d), (bluray.SeriesMinSize!.Value, bluray.SeriesPreferredSize!.Value, bluray.SeriesMaxSize!.Value));
        var remux = definitions.Single(d => d.Id == 30);
        Assert.Equal(35, remux.SeriesMinSize);
        Assert.Null(remux.SeriesMaxSize);

        var updated = await client.PutAsJsonAsync("/api/v1/qualitydefinitions/7", new QualityDefinitionUpdateRequest(bluray.Title, 6, 90, 120, MediaKind.Series), Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = (await updated.Content.ReadFromJsonAsync<QualityDefinitionResponse>(Json))!;
        Assert.Equal((6d, 90d, 120d), (after.SeriesMinSize!.Value, after.SeriesPreferredSize!.Value, after.SeriesMaxSize!.Value));
        Assert.Equal((bluray.MinSize, bluray.PreferredSize, bluray.MaxSize), (after.MinSize, after.PreferredSize, after.MaxSize));

        var invalid = await client.PutAsJsonAsync("/api/v1/qualitydefinitions/7", new QualityDefinitionUpdateRequest(null, 50, null, 10, MediaKind.Series), Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public void Sonarr_defaults_cover_every_quality()
    {
        Assert.Equal(new SonarrDefaultSizes.Sizes(4, 95, 155), SonarrDefaultSizes.For(7));
        Assert.Equal(new SonarrDefaultSizes.Sizes(35, 95, null), SonarrDefaultSizes.For(31));
        // CAM, telesync, BR-DISK…: Sonarr has none, its Unknown limits apply.
        Assert.Equal(SonarrDefaultSizes.For(0), SonarrDefaultSizes.For(25));
        Assert.Equal(SonarrDefaultSizes.For(0), SonarrDefaultSizes.For(22));
    }

    private static async Task<RootFolderResponse> RootAsync(HttpClient client, MediaKind kind)
    {
        var path = Directory.CreateTempSubdirectory($"bigarr-kind-{kind}-").FullName;
        var response = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path, kind), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }
}
