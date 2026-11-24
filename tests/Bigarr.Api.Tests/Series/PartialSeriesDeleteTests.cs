using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// A series with versions in several folders loses them one folder after the other. When one folder cannot go, the
/// folders that did go must leave the database too, with their history line: the library must not list files that
/// no longer exist.
/// </summary>
public sealed class PartialSeriesDeleteTests : IDisposable
{
    private readonly string _rootA = Directory.CreateTempSubdirectory("bigarr-partial-series-a-").FullName;
    private readonly string _rootB = Directory.CreateTempSubdirectory("bigarr-partial-series-b-").FullName;

    public void Dispose()
    {
        TryDelete(_rootA);
        TryDelete(_rootB);
    }

    [Fact]
    public async Task A_series_delete_that_fails_on_one_folder_clears_the_files_that_did_go_and_keeps_the_series()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return; // a read-only folder stops nothing for root: the condition cannot be set up
        }

        using var factory = new BigarrApiFactory();
        var client = await ClientAsync(factory);
        var rootA = await AddSeriesRootAsync(client, _rootA);
        var rootB = await AddSeriesRootAsync(client, _rootB);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var first = series.Versions.Single();
        var profile = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!.Single(p => p.Name == QualitySeeder.SeriesHdProfileName);
        var added = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/versions", new AddSeriesVersionRequest("second", profile.Id, rootB.Id, "Breaking Bad second"), Json);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var second = (await added.Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!;
        Assert.Equal(rootA.Id, first.RootFolderId);

        var folderA = Path.Combine(_rootA, first.FolderName);
        var folderB = Path.Combine(_rootB, second.FolderName);
        var fileA = await GiveFileAsync(factory, first.Id, folderA);
        var fileB = await GiveFileAsync(factory, second.Id, folderB);

        try
        {
            File.SetUnixFileMode(folderB, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var failed = await client.DeleteAsync($"/api/v1/series/{series.Id}?deleteFiles=true");
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

            Assert.False(Directory.Exists(folderA));
            Assert.True(File.Exists(fileB));
            Assert.Contains((await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!, s => s.Id == series.Id);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Null(await TargetFileIdAsync(db, first.Id));
            Assert.NotNull(await TargetFileIdAsync(db, second.Id));
            Assert.False(await db.EpisodeFiles.AnyAsync(f => f.VersionId == first.Id));
            Assert.True(await db.EpisodeFiles.AnyAsync(f => f.VersionId == second.Id));
            var line = await db.History.AsNoTracking().SingleAsync(h => h.SeriesId == series.Id && h.EventType == HistoryEventType.FileDeleted);
            Assert.Equal(first.Name, line.VersionName);
            Assert.Equal(Path.Combine(first.FolderName, Path.GetFileName(fileA)), line.Data["path"]);
        }
        finally
        {
            File.SetUnixFileMode(folderB, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var retried = await client.DeleteAsync($"/api/v1/series/{series.Id}?deleteFiles=true");
        Assert.True(retried.IsSuccessStatusCode, await retried.Content.ReadAsStringAsync());
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!, s => s.Id == series.Id);
        Assert.False(Directory.Exists(folderB));
    }

    private static async Task<RootFolderResponse> AddSeriesRootAsync(HttpClient client, string path)
    {
        var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path, MediaKind.Series));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }

    private static async Task<int?> TargetFileIdAsync(BigarrDbContext db, int versionId) =>
        (await db.EpisodeTargets.AsNoTracking().Include(t => t.Episode)
            .SingleAsync(t => t.VersionId == versionId && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 1)).EpisodeFileId;

    /// <summary>An episode file on disk at the top of the version's folder (a read-only folder then stops its removal before anything inside is touched), recorded against the version's first episode (as an import would).</summary>
    private static async Task<string> GiveFileAsync(BigarrApiFactory factory, int versionId, string folder)
    {
        const string name = "Breaking Bad - S01E01 - Pilot Bluray-1080p.mkv";
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(path, new byte[4096]);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Include(t => t.Episode).SingleAsync(t => t.VersionId == versionId && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 1);
        target.EpisodeFile = new EpisodeFile
        {
            VersionId = versionId,
            RelativePath = name,
            SeasonNumber = 1,
            Size = 4096,
            QualityId = ParserQuality.Bluray1080p.Id,
            OriginalReleaseTitle = "Breaking.Bad.S01E01.1080p.BluRay.x264-GRP",
            ReleaseGroup = "GRP",
        };
        await db.SaveChangesAsync();
        return path;
    }
}
