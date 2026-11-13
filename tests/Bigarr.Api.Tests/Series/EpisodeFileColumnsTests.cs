using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;

namespace Bigarr.Api.Tests.Series;

/// <summary>What the season tables' columns read from an episode file: custom formats, their score, flags and the quality's resolution.</summary>
public sealed class EpisodeFileColumnsTests(SeriesScanApiFactory factory) : IClassFixture<SeriesScanApiFactory>, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-tv-columns-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Episode_files_carry_their_custom_formats_scored_by_the_version_profile()
    {
        var season = Directory.CreateDirectory(Path.Combine(_root, "Breaking Bad (2008)", "Season 01")).FullName;
        File.WriteAllBytes(Path.Combine(season, "Breaking.Bad.S01E01.1080p.BluRay.x264-GRP.mkv"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(season, "Breaking.Bad.S01E02.720p.HDTV.x264-GRP.mkv"), new byte[1024]);

        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var spec = new CustomFormatSpecificationDto("BluRay", "ReleaseTitleSpecification", false, true, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(@"\bBlu-?Ray\b") });
        var format = await client.PostAsJsonAsync(
            "/api/v1/customformats",
            new CustomFormatRequest("Demo BluRay", false, [spec], profiles.Where(p => p.Kind != Bigarr.Domain.Quality.ProfileKind.Movies).Select(p => new CustomFormatScoreRequest(p.Id, 40)).ToList()),
            Json);
        Assert.Equal(HttpStatusCode.Created, format.StatusCode);

        var root = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(_root, MediaKind.Series), Json);
        Assert.Equal(HttpStatusCode.Created, root.StatusCode);
        var rootId = (await root.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!.Id;
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{rootId}/scan", null)).StatusCode);
        await WaitAsync(client, $"/api/v1/rootfolders/{rootId}/scan");

        var series = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!.Single(s => s.TvdbId == 81189);
        var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Files;

        var bluray = files.Single(f => f.Episodes == "S01E01");
        Assert.Equal(["Demo BluRay"], bluray.CustomFormats);
        Assert.Equal(40, bluray.CustomFormatScore);
        Assert.Equal(1080, bluray.QualityResolution);
        Assert.Equal(IndexerFlags.None, bluray.IndexerFlags);

        var hdtv = files.Single(f => f.Episodes == "S01E02");
        Assert.Empty(hdtv.CustomFormats!);
        Assert.Equal(0, hdtv.CustomFormatScore);
        Assert.Equal(720, hdtv.QualityResolution);
    }

    private static async Task WaitAsync(HttpClient client, string statusUrl)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = (await client.GetFromJsonAsync<ScanStatusResponse>(statusUrl, Json))!;
            if (status.State is ScanState.Completed or ScanState.Failed)
            {
                Assert.Null(status.Error);
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"{statusUrl} did not finish.");
    }
}
