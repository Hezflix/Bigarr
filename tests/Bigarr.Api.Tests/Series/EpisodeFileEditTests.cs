using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// Sonarr's "edit file" on an episode file, the twin of the film's: release group, quality and languages, custom formats
/// following at once, one history line per episode. A hardlink of a film's file follows the film's file instead.
/// </summary>
public sealed class EpisodeFileEditTests(SeriesScanApiFactory factory) : IClassFixture<SeriesScanApiFactory>, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-tv-edit-").FullName;

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
    public async Task An_episode_file_is_edited_its_formats_follow_and_a_film_link_follows_the_film()
    {
        var season = Directory.CreateDirectory(Path.Combine(_root, "Breaking Bad (2008)", "Season 01")).FullName;
        File.WriteAllBytes(Path.Combine(season, "Breaking.Bad.S01E01.1080p.BluRay.x264-OLDGRP.mkv"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(season, "Breaking.Bad.S01E02-E03.720p.HDTV.x264-OLDGRP.mkv"), new byte[1024]);

        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var spec = new CustomFormatSpecificationDto("Group", "ReleaseGroupSpecification", false, true, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("^(NEWGRP)$") });
        var format = await client.PostAsJsonAsync("/api/v1/customformats", new CustomFormatRequest("Demo group NEWGRP", false, [spec], profiles.Select(p => new CustomFormatScoreRequest(p.Id, 30)).ToList()), Json);
        Assert.Equal(HttpStatusCode.Created, format.StatusCode);

        var root = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(_root, MediaKind.Series), Json);
        Assert.Equal(HttpStatusCode.Created, root.StatusCode);
        var rootId = (await root.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!.Id;
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{rootId}/scan", null)).StatusCode);
        await WaitAsync(client, $"/api/v1/rootfolders/{rootId}/scan");

        var series = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!.Single(s => s.TvdbId == 81189);
        var files = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Files;
        var double_ = files.Single(f => f.Episodes == "S01E02-E03");
        Assert.Equal("OLDGRP", double_.ReleaseGroup);
        Assert.DoesNotContain("Demo group NEWGRP", double_.CustomFormats!);

        var url = $"/api/v1/series/{series.Id}/episodefiles/{double_.Id}";
        var edited = await client.PutAsJsonAsync(url, new UpdateEpisodeFileRequest(ReleaseGroup: "NEWGRP", Languages: ["japanese"]), Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var file = (await edited.Content.ReadFromJsonAsync<EpisodeFileResponse>(Json))!;
        Assert.Equal("NEWGRP", file.ReleaseGroup);
        Assert.Equal(["Japanese"], file.Languages);
        Assert.Contains("Demo group NEWGRP", file.CustomFormats!);
        Assert.Equal(double_.CustomFormatScore + 30, file.CustomFormatScore);
        Assert.Equal(double_.RelativePath, file.RelativePath);

        // One line per episode the file holds.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var lines = await db.History.AsNoTracking().Where(h => h.EventType == HistoryEventType.FileEdited && h.SeriesId == series.Id).ToListAsync();
            Assert.Equal(2, lines.Count);
            Assert.All(lines, l => Assert.Equal("OLDGRP → NEWGRP", l.Data["releaseGroup"]));
            Assert.Equal(["S01E02", "S01E03"], lines.Select(l => l.Data["episode"]).Order());
        }

        var cleared = await client.PutAsJsonAsync(url, new UpdateEpisodeFileRequest(ReleaseGroup: ""), Json);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<EpisodeFileResponse>(Json))!.ReleaseGroup);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/episodefiles/999999", new UpdateEpisodeFileRequest(ReleaseGroup: "X"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/series/999999/episodefiles/{double_.Id}", new UpdateEpisodeFileRequest(ReleaseGroup: "X"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url, new UpdateEpisodeFileRequest(QualityId: 0), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url, new UpdateEpisodeFileRequest(Languages: ["Klingon"]), Json)).StatusCode);

        // The other file becomes a hardlink of a film's file: it is the film's to describe, and follows the film's edit.
        var single = files.Single(f => f.Episodes == "S01E01");
        int movieId;
        int versionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var (movie, version) = await TestData.SeedMovieAsync(db, 990301);
            var filmFile = TestData.NewFile(version, "Invented.Film.2020.1080p.BluRay.x264-OLDGRP.mkv");
            filmFile.ReleaseGroup = "OLDGRP";
            db.MediaFiles.Add(filmFile);
            await db.SaveChangesAsync();
            var episodeFile = await db.EpisodeFiles.SingleAsync(f => f.Id == single.Id);
            episodeFile.LinkedFromPath = "/data/movies/Invented Film (2020)/Invented.Film.2020.1080p.BluRay.x264-OLDGRP.mkv";
            episodeFile.LinkedMediaFileId = filmFile.Id;
            await db.SaveChangesAsync();
            movieId = movie.Id;
            versionId = version.Id;
        }

        var refused = await client.PutAsJsonAsync($"/api/v1/series/{series.Id}/episodefiles/{single.Id}", new UpdateEpisodeFileRequest(ReleaseGroup: "NEWGRP"), Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var film = await client.PutAsJsonAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file", new UpdateMediaFileRequest(ReleaseGroup: "NEWGRP", Languages: ["German"]), Json);
        Assert.Equal(HttpStatusCode.OK, film.StatusCode);
        var linked = (await client.GetFromJsonAsync<SeriesFilesResponse>($"/api/v1/series/{series.Id}/files", Json))!.Files.Single(f => f.Id == single.Id);
        Assert.Equal("NEWGRP", linked.ReleaseGroup);
        Assert.Equal(["German"], linked.Languages);
        Assert.Contains("Demo group NEWGRP", linked.CustomFormats!);
    }

    private static async Task WaitAsync(HttpClient client, string statusUrl)
    {
        for (var attempt = 0; attempt < 600; attempt++)
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
