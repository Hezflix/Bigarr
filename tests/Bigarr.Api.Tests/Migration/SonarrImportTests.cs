using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Shadow;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// Moving in from Sonarr (S4): a standard and an anime show become series versions with every file on disk attached
/// (a multi-episode file to both its episodes, Sonarr's remux id translated, anime absolute numbers, a special), with
/// monitoring, tags, history, blocklist and exclusions; a second run changes nothing; a second Sonarr adds <c>4k</c>
/// versions to the same shows. Sonarr is played by <see cref="FakeSonarr"/>; the library folders are temp directories.
/// </summary>
public sealed class SonarrImportTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task A_sonarr_library_imports_with_every_file_attached_and_a_second_sonarr_adds_4k_versions()
    {
        var client = await ClientAsync(factory);
        var (hd, uhd) = await ProfilesAsync(client);
        var tv = await SeriesRootAsync(client, "bigarr-sonarr-tv-");
        var anime = await SeriesRootAsync(client, "bigarr-sonarr-anime-");
        var films = await AddRootAsync(client, Directory.CreateTempSubdirectory("bigarr-sonarr-films-").FullName);

        await using var sonarr = await FakeSonarr.StartAsync();
        var bb = FakeSonarr.BreakingBad();
        bb.Tags.Add(1);
        sonarr.Tags.Add((1, "seerr"));
        var frieren = FakeSonarr.Frieren();
        sonarr.Series.AddRange([bb, frieren]);

        // Breaking Bad: one file for S01E01–E02, S01E03, a remux for S02E01 (Sonarr's id 20), and S01E04 Sonarr lists but the disk lacks.
        var multi = FakeSonarr.AddFile(bb, new(11, 1, "Season 01/Breaking.Bad.S01E01E02.1080p.BluRay.x264-DEMAND.mkv", 7, "Bluray-1080p", 4096, "DEMAND"), (1, 1), (1, 2));
        var third = FakeSonarr.AddFile(bb, new(12, 1, "Season 01/Breaking.Bad.S01E03.1080p.BluRay.x264-DEMAND.mkv", 7, "Bluray-1080p", 2048, "DEMAND"), (1, 3));
        var remux = FakeSonarr.AddFile(bb, new(13, 2, "Season 02/Breaking.Bad.S02E01.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-EPSiLON.mkv", 20, "Bluray-1080p Remux", 8192, "EPSiLON"), (2, 1));
        FakeSonarr.AddFile(bb, new(14, 1, "Season 01/Breaking.Bad.S01E04.1080p.BluRay.x264-DEMAND.mkv", 7, "Bluray-1080p", 2048, "DEMAND"), (1, 4));
        foreach (var file in new[] { multi, third, remux })
        {
            FakeSonarr.Write(tv.Path, bb, file);
        }

        // Frieren: episode 1 (absolute 1) with two languages, and the special.
        var first = FakeSonarr.AddFile(frieren, new(21, 1, "Season 01/[SubsPlease] Sousou no Frieren - 01 (1080p).mkv", 3, "WEBDL-1080p", 1024, "SubsPlease", ["Japanese", "English"]), (1, 1));
        var special = FakeSonarr.AddFile(frieren, new(22, 0, "Specials/Frieren.S00E01.1080p.WEB-DL-GRP.mkv", 3, "WEBDL-1080p", 512, "GRP"), (0, 1));
        FakeSonarr.Write(anime.Path, frieren, first);
        FakeSonarr.Write(anime.Path, frieren, special);

        // History of S01E03: grabbed, imported, imported again (an upgrade); a season 2 pack grabbed (one line per episode).
        var e3 = sonarr.Episode(bb, 1, 3).Id;
        var at = new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        sonarr.History.Add(new(101, 1, e3, "grabbed", at, "Breaking.Bad.S01E03.720p.HDTV.x264-CTU", 4, "HDTV-720p", "DL1", new() { ["indexer"] = "The Old School (Prowlarr)", ["releaseGroup"] = "CTU" }));
        sonarr.History.Add(new(102, 1, e3, "downloadFolderImported", at.AddHours(1), "Breaking.Bad.S01E03.720p.HDTV.x264-CTU", 4, "HDTV-720p", "DL1", new() { ["importedPath"] = "/sonarr-tv/x.mkv" }));
        sonarr.History.Add(new(103, 1, e3, "downloadFolderImported", at.AddDays(1), "Breaking.Bad.S01E03.1080p.BluRay.x264-DEMAND", 7, "Bluray-1080p", "DL2", new() { ["importedPath"] = "/sonarr-tv/y.mkv" }));
        for (var n = 1; n <= 3; n++)
        {
            sonarr.History.Add(new(110 + n, 1, sonarr.Episode(bb, 2, n).Id, "grabbed", at.AddDays(2), "Breaking.Bad.S02.1080p.BluRay.x264-DEMAND", 7, "Bluray-1080p", "PACK", new() { ["releaseType"] = "SeasonPack" }));
        }

        sonarr.Blocklist.Add(new(1, 1, [sonarr.Episode(bb, 1, 5).Id], "Breaking.Bad.S01E05.FAKE.1080p-NOPE", at, "The Old School (Prowlarr)", "Fake"));
        sonarr.Exclusions.Add((999001, "A Show Nobody Wants"));

        // Preview.
        var previewResponse = await client.PostAsJsonAsync("/api/v1/migration/sonarr/preview", new SonarrImportPreviewRequest(sonarr.Url, FakeSonarr.Key), Json);
        Assert.True(previewResponse.StatusCode == HttpStatusCode.OK, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<SonarrImportPreviewResponse>(Json))!;
        Assert.Equal(2, preview.Series);
        Assert.Equal(6, preview.EpisodeFiles);
        Assert.Equal("1080p", preview.SuggestedVersionName);
        Assert.Equal(1, preview.SeriesTypes["standard"]);
        Assert.Equal(1, preview.SeriesTypes["anime"]);
        Assert.Equal(6, preview.HistoryRecords);
        Assert.Equal(1, preview.Exclusions);
        Assert.Equal([FakeSonarr.AnimeRootPath, FakeSonarr.RootPath], preview.RootFolders.Select(r => r.Path).ToArray());
        Assert.Equal(hd, preview.Profiles.Single(p => p.Name == "HD-1080p").SuggestedProfileId);
        Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync("/api/v1/migration/sonarr/preview", new SonarrImportPreviewRequest(sonarr.Url, "nope"), Json)).StatusCode);

        // A film root folder is refused.
        var refused = await client.PostAsJsonAsync("/api/v1/migration/sonarr/import", Request(sonarr.Url, "1080p", hd, films.Id, films.Id), Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var report = await ImportAsync(client, Request(sonarr.Url, "1080p", hd, tv.Id, anime.Id));
        Assert.True(report.SeriesAdded == 2, string.Join(" | ", report.Issues.Select(i => $"{i.Title}: {i.Reason}")) + $" failed={report.Failed} skipped={report.Skipped} total={report.Total}");
        Assert.Equal(5, report.FilesAdded);
        Assert.Equal(1, report.FilesMissing);
        Assert.Equal(6, report.HistoryAdded);
        Assert.Equal(1, report.BlocklistAdded);
        Assert.Equal(1, report.ExclusionsAdded);
        Assert.Equal(1, report.TagsAdded);
        Assert.Equal(0, report.Failed);
        Assert.Contains(report.Issues, i => i.Reason.StartsWith("File not found", StringComparison.Ordinal));

        var counts = await CountsAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var show = await db.Series.AsNoTracking().Include(s => s.Versions).ThenInclude(v => v.Tags).Include(s => s.Versions).ThenInclude(v => v.Seasons).ThenInclude(s => s.Season).SingleAsync(s => s.TvdbId == 81189);
            Assert.Equal(SeriesType.Standard, show.SeriesType);
            Assert.Null(show.LastMetadataRefreshAt);
            var version = Assert.Single(show.Versions);
            Assert.Equal("1080p", version.Name);
            Assert.True(version.IsDefault);
            Assert.Equal("Breaking Bad (2008)", version.FolderName);
            Assert.Equal(["seerr"], version.Tags.Select(t => t.Label));
            Assert.False(version.Seasons.Single(s => s.Season.SeasonNumber == 0).Monitored);
            Assert.True(version.Seasons.Single(s => s.Season.SeasonNumber == 1).Monitored);

            var targets = await db.EpisodeTargets.AsNoTracking().Include(t => t.Episode).Include(t => t.EpisodeFile).Where(t => t.VersionId == version.Id).ToListAsync();
            Assert.Equal(21, targets.Count);
            EpisodeTarget T(int s, int e) => targets.Single(t => t.Episode.SeasonNumber == s && t.Episode.EpisodeNumber == e);
            Assert.NotNull(T(1, 1).EpisodeFileId);
            Assert.Equal(T(1, 1).EpisodeFileId, T(1, 2).EpisodeFileId);
            Assert.Equal(7, T(1, 1).EpisodeFile!.QualityId);
            Assert.Equal("Breaking.Bad.S01E01E02.1080p.BluRay.x264-DEMAND", T(1, 1).EpisodeFile!.OriginalReleaseTitle);
            Assert.Equal(30, T(2, 1).EpisodeFile!.QualityId);
            Assert.Null(T(1, 4).EpisodeFileId);
            Assert.False(T(2, 13).Monitored);
            Assert.True(T(2, 12).Monitored);

            var e3Target = T(1, 3).Id;
            var history = await db.History.AsNoTracking().Where(h => h.TargetId == e3Target).OrderBy(h => h.CreatedAt).ToListAsync();
            Assert.Equal([HistoryEventType.Grabbed, HistoryEventType.Imported, HistoryEventType.Upgraded], history.Select(h => h.EventType));
            Assert.Equal("HDTV-720p", history[2].Data["previousQuality"]);
            Assert.All(history, h => Assert.Equal(show.Id, h.SeriesId));
            Assert.Equal("S01E03", history[0].Data["episode"]);
            Assert.Equal("sonarr", history[0].Data["importedFrom"]);
            var e5Target = T(1, 5).Id;
            Assert.Single(await db.Blocklist.AsNoTracking().Where(b => b.TargetId == e5Target).ToListAsync());
            Assert.True(await db.SeriesExclusions.AnyAsync(e => e.TvdbId == 999001));

            var animeShow = await db.Series.AsNoTracking().Include(s => s.Episodes).SingleAsync(s => s.TvdbId == 424536);
            Assert.Equal(SeriesType.Anime, animeShow.SeriesType);
            Assert.Equal(Enumerable.Range(1, 28), animeShow.Episodes.Where(e => e.SeasonNumber == 1).OrderBy(e => e.EpisodeNumber).Select(e => e.AbsoluteEpisodeNumber!.Value));
            Assert.Null(animeShow.Episodes.Single(e => e.SeasonNumber == 0).AbsoluteEpisodeNumber);
            var animeFiles = await db.EpisodeFiles.AsNoTracking().Where(f => f.Version.SeriesId == animeShow.Id).ToListAsync();
            Assert.Equal(2, animeFiles.Count);
            Assert.Equal(["Japanese", "English"], animeFiles.Single(f => f.SeasonNumber == 1).Languages);
            Assert.Contains(animeFiles, f => f.SeasonNumber == 0);
        }

        // A second run changes nothing.
        var again = await ImportAsync(client, Request(sonarr.Url, "1080p", hd, tv.Id, anime.Id));
        Assert.Equal(0, again.SeriesAdded);
        Assert.Equal(0, again.FilesAdded);
        Assert.Equal(0, again.HistoryAdded);
        Assert.Equal(0, again.BlocklistAdded);
        Assert.Equal(2, again.VersionsUpdated);
        Assert.Equal(counts, await CountsAsync());

        // A second Sonarr, the 4K one: Breaking Bad gets a 4k version next to the 1080p one, on its own root.
        var uhdRoot = await SeriesRootAsync(client, "bigarr-sonarr-4k-");
        await using var sonarr4k = await FakeSonarr.StartAsync();
        var bb4k = FakeSonarr.BreakingBad(7);
        bb4k.ProfileId = 2;
        var uhdFile = FakeSonarr.AddFile(bb4k, new(71, 1, "Season 01/Breaking.Bad.S01E01.2160p.BluRay.x265-SWTYBLZ.mkv", 19, "Bluray-2160p", 9000, "SWTYBLZ"), (1, 1));
        FakeSonarr.Write(uhdRoot.Path, bb4k, uhdFile);
        sonarr4k.Series.Add(bb4k);
        var uhdReport = await ImportAsync(client, new SonarrImportRequest(sonarr4k.Url, FakeSonarr.Key, "4k", new() { [2] = uhd }, new() { [FakeSonarr.RootPath] = uhdRoot.Id }, ImportProfileQualities: false));
        Assert.Equal(0, uhdReport.SeriesAdded);
        Assert.Equal(1, uhdReport.VersionsAdded);
        Assert.Equal(1, uhdReport.FilesAdded);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var show = await db.Series.AsNoTracking().Include(s => s.Versions).Include(s => s.Episodes).SingleAsync(s => s.TvdbId == 81189);
            Assert.Equal(["1080p", "4k"], show.Versions.OrderBy(v => v.Name).Select(v => v.Name));
            Assert.True(show.Versions.Single(v => v.Name == "1080p").IsDefault);
            Assert.Equal(21, show.Episodes.Count);
            var uhdVersion = show.Versions.Single(v => v.Name == "4k");
            Assert.Equal(uhd, uhdVersion.QualityProfileId);
            var file = await db.EpisodeFiles.AsNoTracking().SingleAsync(f => f.VersionId == uhdVersion.Id);
            Assert.Equal(19, file.QualityId);
        }

        // Nothing but GET ever reached either Sonarr.
        Assert.All(sonarr.Requests.Concat(sonarr4k.Requests), r => Assert.Equal("GET", r.Method));
    }

    private static SonarrImportRequest Request(string url, string version, int profile, int tvRoot, int animeRoot) =>
        new(url, FakeSonarr.Key, version, new() { [1] = profile }, new() { [FakeSonarr.RootPath] = tvRoot, [FakeSonarr.AnimeRootPath] = animeRoot }, ImportProfileQualities: false);

    private async Task<(int Series, int Files, int History, int Targets, int Blocklist)> CountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return (await db.Series.CountAsync(), await db.EpisodeFiles.CountAsync(), await db.History.CountAsync(), await db.EpisodeTargets.CountAsync(), await db.Blocklist.CountAsync());
    }

    internal static async Task<RootFolderResponse> SeriesRootAsync(HttpClient client, string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        var response = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path, MediaKind.Series), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }

    internal static async Task<SonarrImportStatusResponse> ImportAsync(HttpClient client, SonarrImportRequest request)
    {
        var started = await client.PostAsJsonAsync("/api/v1/migration/sonarr/import", request, Json);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        return await WaitUntilAsync(async () =>
        {
            var status = (await client.GetFromJsonAsync<SonarrImportStatusResponse>("/api/v1/migration/sonarr/status", Json))!;
            Assert.NotEqual(Features.Migration.RadarrImportState.Failed, status.State);
            return status.State == Features.Migration.RadarrImportState.Completed ? status : null;
        }, "the Sonarr import");
    }
}
