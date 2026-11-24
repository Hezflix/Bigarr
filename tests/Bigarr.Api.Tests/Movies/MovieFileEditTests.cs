using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Movies;

/// <summary>
/// Radarr's "edit file" on a film's file: the release group (and edition, quality, languages) the user says it has. The
/// custom formats judged at read time follow at once, the change is journaled, nothing on disk moves.
/// </summary>
public sealed class MovieFileEditTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task The_release_group_is_set_and_cleared_and_the_custom_formats_follow()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var (movieId, versionId, fileId) = await SeedAsync(990201, "Invented.Film.2020.1080p.BluRay.x264-OLDGRP.mkv", "OLDGRP");
        await GroupFormatAsync(session, "Demo group NEWGRP", "^(NEWGRP)$", 50);

        var before = await VersionAsync(session, movieId, versionId);
        Assert.Equal("OLDGRP", before.File!.ReleaseGroup);
        Assert.DoesNotContain("Demo group NEWGRP", before.CustomFormats!);

        var set = await session.PutAsJsonAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file", new UpdateMediaFileRequest(ReleaseGroup: "  NEWGRP "), Json);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var updated = (await set.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
        Assert.Equal("NEWGRP", updated.File!.ReleaseGroup);
        Assert.Contains("Demo group NEWGRP", updated.CustomFormats!);
        Assert.Equal(before.CustomFormatScore + 50, updated.CustomFormatScore);

        // The film's page reads the same, and the file kept its name: renaming is the Rename dialog's job.
        var page = await VersionAsync(session, movieId, versionId);
        Assert.Equal("NEWGRP", page.File!.ReleaseGroup);
        Assert.Contains("Demo group NEWGRP", page.CustomFormats!);
        Assert.Equal("Invented.Film.2020.1080p.BluRay.x264-OLDGRP.mkv", page.File.RelativePath);

        var cleared = await session.PutAsJsonAsync($"/api/v1/movies/{movieId}/versions/{versionId}/file", new UpdateMediaFileRequest(ReleaseGroup: " "), Json);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var after = (await cleared.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
        Assert.Null(after.File!.ReleaseGroup);
        Assert.DoesNotContain("Demo group NEWGRP", after.CustomFormats!);

        // The dialog sends every field back: a language the scan left as Unknown is accepted as it is.
        var whole = await session.PutAsJsonAsync(
            $"/api/v1/movies/{movieId}/versions/{versionId}/file",
            new UpdateMediaFileRequest(ReleaseGroup: "", Edition: "", QualityId: TestData.BlurayQualityId, Languages: ["Unknown"]),
            Json);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal(["Unknown"], (await whole.Content.ReadFromJsonAsync<VersionResponse>(Json))!.File!.Languages);

        var lines = (await HistoryAsync(fileId)).Where(l => l.Data.ContainsKey("releaseGroup")).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(versionId, l.TargetId));
        Assert.Equal("OLDGRP → NEWGRP", lines[0].Data["releaseGroup"]);
        Assert.Equal("NEWGRP → —", lines[1].Data["releaseGroup"]);
    }

    [Fact]
    public async Task Edition_quality_and_languages_change_together_and_fields_left_out_stay()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var (movieId, versionId, fileId) = await SeedAsync(990202, "Other.Film.2021.1080p.BluRay.x264-KEEP.mkv", "KEEP");
        var definitions = (await session.GetFromJsonAsync<List<JsonElement>>("/api/v1/qualitydefinitions", Json))!;
        var other = definitions.Select(d => (Id: d.GetProperty("id").GetInt32(), Name: d.GetProperty("name").GetString()!)).First(d => d.Id != 0 && d.Id != TestData.BlurayQualityId);

        var response = await session.PutAsJsonAsync(
            $"/api/v1/movies/{movieId}/versions/{versionId}/file",
            new UpdateMediaFileRequest(Edition: "Director's Cut", QualityId: other.Id, Languages: ["french", "English", "French"]),
            Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var file = (await response.Content.ReadFromJsonAsync<VersionResponse>(Json))!.File!;
        Assert.Equal("KEEP", file.ReleaseGroup);
        Assert.Equal("Director's Cut", file.Edition);
        Assert.Equal(other.Id, file.QualityId);
        Assert.Equal(other.Name, file.Quality);
        Assert.Equal(["French", "English"], file.Languages);

        var line = Assert.Single(await HistoryAsync(fileId));
        Assert.False(line.Data.ContainsKey("releaseGroup"));
        Assert.Equal("— → Director's Cut", line.Data["edition"]);
        Assert.Equal($"Bluray-1080p → {other.Name}", line.Data["quality"]);
        Assert.Equal("— → French, English", line.Data["languages"]);

        // The same facts again change nothing and journal nothing.
        var again = await session.PutAsJsonAsync(
            $"/api/v1/movies/{movieId}/versions/{versionId}/file",
            new UpdateMediaFileRequest(ReleaseGroup: "KEEP", Edition: "Director's Cut", QualityId: other.Id, Languages: ["French", "English"]),
            Json);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(await HistoryAsync(fileId));
    }

    [Fact]
    public async Task Unknown_files_and_wrong_facts_are_refused()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var (movieId, versionId, _) = await SeedAsync(990203, "Third.Film.2022.1080p.BluRay.x264-GRP.mkv", "GRP");
        int emptyVersionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var movie = await db.Movies.Include(m => m.Versions).SingleAsync(m => m.Id == movieId);
            var empty = TestData.NewVersion(movie, "Empty");
            db.MovieVersions.Add(empty);
            await db.SaveChangesAsync();
            emptyVersionId = empty.Id;
        }

        var url = $"/api/v1/movies/{movieId}/versions/{versionId}/file";
        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync($"/api/v1/movies/{movieId}/versions/999999/file", new UpdateMediaFileRequest(ReleaseGroup: "X"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync($"/api/v1/movies/999999/versions/{versionId}/file", new UpdateMediaFileRequest(ReleaseGroup: "X"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync($"/api/v1/movies/{movieId}/versions/{emptyVersionId}/file", new UpdateMediaFileRequest(ReleaseGroup: "X"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync(url, new UpdateMediaFileRequest(QualityId: 0), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync(url, new UpdateMediaFileRequest(QualityId: 4242), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync(url, new UpdateMediaFileRequest(Languages: ["Klingon"]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync(url, new UpdateMediaFileRequest(Languages: ["Original"]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync(url, new UpdateMediaFileRequest(ReleaseGroup: new string('x', 256)), Json)).StatusCode);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(url, new UpdateMediaFileRequest(ReleaseGroup: "X"), Json)).StatusCode);

        Assert.Equal("GRP", (await VersionAsync(session, movieId, versionId)).File!.ReleaseGroup);
    }

    private async Task<(int MovieId, int VersionId, int FileId)> SeedAsync(int tmdbId, string relativePath, string group)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var (movie, version) = await TestData.SeedMovieAsync(db, tmdbId);
        var file = TestData.NewFile(version, relativePath);
        file.ReleaseGroup = group;
        file.OriginalReleaseTitle = Path.GetFileNameWithoutExtension(relativePath);
        db.MediaFiles.Add(file);
        await db.SaveChangesAsync();
        return (movie.Id, version.Id, file.Id);
    }

    private async Task<List<HistoryEvent>> HistoryAsync(int fileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.History.AsNoTracking().Where(h => h.MediaFileId == fileId && h.EventType == HistoryEventType.FileEdited).OrderBy(h => h.Id).ToListAsync();
    }

    private static async Task<VersionResponse> VersionAsync(HttpClient session, int movieId, int versionId)
    {
        var movie = (await session.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movieId}", Json))!;
        return movie.Versions.Single(v => v.Id == versionId);
    }

    /// <summary>A format matching one release group, scored on every profile.</summary>
    private static async Task GroupFormatAsync(HttpClient session, string name, string pattern, int score)
    {
        var profiles = (await session.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var spec = new CustomFormatSpecificationDto("Group", "ReleaseGroupSpecification", false, true, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(pattern) });
        var created = await session.PostAsJsonAsync("/api/v1/customformats", new CustomFormatRequest(name, false, [spec], profiles.Select(p => new CustomFormatScoreRequest(p.Id, score)).ToList()), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
}
