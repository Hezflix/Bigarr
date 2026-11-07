using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// Following a Radarr in shadow mode, pass after pass against a fake Radarr: new films come in through the import's
/// mapping, grabs are read from the history since the checkpoint (and caught up after downtime), imports, upgrades,
/// renames and deletions move the version's file record (never the disk), the key never leaves Bigarr, and nothing but
/// GET ever reaches Radarr.
/// </summary>
public sealed class RadarrFollowTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Heat = 949;
    private const int PulpFiction = 680;
    private const int FightClub = 550;

    [Fact]
    public async Task The_follow_keeps_the_version_in_line_with_radarr_and_records_its_grabs()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-follow-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();

        // Heat has its file on disk; Pulp Fiction is wanted. Neither is in Bigarr yet.
        var heat = new FakeRadarr.Film { Id = 1, Title = "Heat", Year = 1995, TmdbId = Heat, File = new(10, "Heat.1995.1080p.BluRay.x264-GRP.mkv", 7, "Bluray-1080p", 2048, "GRP") };
        var pulp = new FakeRadarr.Film { Id = 2, Title = "Pulp Fiction", Year = 1994, TmdbId = PulpFiction };
        radarr.Films.AddRange([heat, pulp]);
        var heatFirstFile = Write(library, heat);

        try
        {
            // Settings: the key goes in, never comes back.
            var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
                "Radarr 1080p", radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var createdBody = await created.Content.ReadAsStringAsync();
            Assert.DoesNotContain(FakeRadarr.Key, createdBody, StringComparison.Ordinal);
            var follow = JsonSerializer.Deserialize<FollowedRadarrResponse>(createdBody, Json)!;
            Assert.Equal(FollowedRadarrService.KeyMask, follow.ApiKey);
            Assert.DoesNotContain(FakeRadarr.Key, await client.GetStringAsync("/api/v1/shadow/radarrs"), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeRadarr.Key, await client.GetStringAsync("/api/v1/shadow"), StringComparison.Ordinal);

            // An update with the mask keeps the stored key: the connection test still goes through with it.
            var updated = await client.PutAsJsonAsync($"/api/v1/shadow/radarrs/{follow.Id}", new FollowedRadarrRequest(
                "Radarr 1080p", radarr.Url, FollowedRadarrService.KeyMask, "1080p", new() { [1] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var test = await client.PostAsync($"/api/v1/shadow/radarrs/{follow.Id}/test", null);
            Assert.Equal(HttpStatusCode.OK, test.StatusCode);
            Assert.Equal(2, (await test.Content.ReadFromJsonAsync<FollowedRadarrTestResponse>(Json))!.Movies);

            // Pass 1: the library comes in line with Radarr's, through the import's mapping.
            var first = await SyncAsync(client, follow.Id);
            Assert.Contains("2 film(s) added", first.Summary, StringComparison.Ordinal);
            var since = radarr.Requests.First(r => r.Path == "/api/v3/history/since");
            var asked = DateTime.Parse(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(since.Query)["date"].ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
            Assert.Equal(follow.HistoryCheckpoint - RadarrFollower.Overlap, asked, TimeSpan.FromSeconds(1));

            var heatVersion = await VersionAsync(Heat);
            Assert.Equal("Heat.1995.1080p.BluRay.x264-GRP.mkv", heatVersion.File!.RelativePath);
            Assert.Equal(7, heatVersion.File.QualityId);
            Assert.Null((await VersionAsync(PulpFiction)).File);

            // Bigarr picked a release for Pulp Fiction; the next pass asks Radarr what it makes of it.
            var pulpVersion = await VersionAsync(PulpFiction);
            await AddPickAsync(pulpVersion, "Pulp.Fiction.1994.1080p.WEB-DL.DDP5.1.H.264-NTb");
            radarr.Parse = title => title.StartsWith("Pulp", StringComparison.Ordinal) ? (2, "WEBDL-1080p", ["Radarr WEB"], 15) : (0, "Unknown", [], 0);

            // Pass 2: Radarr grabs Pulp Fiction.
            var grabbedAt = DateTime.UtcNow;
            radarr.History.Add(new FakeRadarr.Line(101, 2, "grabbed", grabbedAt, "Pulp.Fiction.1994.1080p.BluRay.x264-SPARKS", 7, "Bluray-1080p", "ABCDEF0123456789ABCDEF0123456789ABCDEF01", new()
            {
                ["indexer"] = "Some Indexer (Prowlarr)",
                ["releaseGroup"] = "SPARKS",
                ["size"] = "11702117171",
                ["guid"] = "https://indexer.invalid/details/101",
                ["torrentInfoHash"] = "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
                ["customFormatScore"] = "25",
                ["releaseSource"] = "Rss",
                ["publishedDate"] = grabbedAt.AddHours(-1).ToString("s") + "Z",
                ["downloadClientName"] = "qBittorrent",
                ["indexerFlags"] = "G_Freeleech",
            }));
            var second = await SyncAsync(client, follow.Id);
            Assert.Contains("1 new grab(s)", second.Summary, StringComparison.Ordinal);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var grab = await db.RadarrGrabs.AsNoTracking().SingleAsync();
                Assert.Equal(PulpFiction, grab.TmdbId);
                Assert.Equal(pulpVersion.Id, grab.VersionId);
                Assert.Equal(pulpVersion.MovieId, grab.MovieId);
                Assert.Equal("1080p", grab.VersionName);
                Assert.Equal("abcdef0123456789abcdef0123456789abcdef01", grab.InfoHash);
                Assert.Equal("Some Indexer (Prowlarr)", grab.IndexerName);
                Assert.Equal("Rss", grab.ReleaseSource);
                Assert.Equal("Bluray-1080p", grab.Quality);
                Assert.Equal(25, grab.CustomFormatScore);
                Assert.Equal(RadarrGrabState.Grabbed, grab.State);
                // Bigarr's own engine on Radarr's release, while the version was still wanted.
                Assert.NotNull(grab.BigarrVerdict);
                Assert.Null(grab.BigarrVerdict.Unmatched);
                Assert.True(grab.BigarrVerdict.Approved, string.Join(" · ", grab.BigarrVerdict.Rejections));
                Assert.Equal("Bluray-1080p", grab.BigarrVerdict.Quality);
                Assert.Equal("1080p", grab.BigarrVerdict.AttributedVersion);

                // The film page's journal says Radarr grabbed it.
                var line = await db.History.AsNoTracking().SingleAsync(h => h.TmdbId == PulpFiction && h.EventType == HistoryEventType.Grabbed);
                Assert.Equal("radarr", line.Data["importedFrom"]);

                // Radarr's view of Bigarr's pick.
                var view = (await db.ShadowPicks.AsNoTracking().SingleAsync()).RadarrView;
                Assert.NotNull(view);
                Assert.Equal(PulpFiction, view.TmdbId);
                Assert.Equal("WEBDL-1080p", view.Quality);
                Assert.Equal(["Radarr WEB"], view.CustomFormats);
                Assert.Equal(15, view.CustomFormatScore);
                Assert.True(view.Monitored);
                Assert.Null(view.FileQuality);
            }

            // Bigarr is down for a while; Radarr imports Pulp Fiction, upgrades Heat, and adds Fight Club.
            var later = grabbedAt.AddMinutes(30);
            pulp.File = new(20, "Pulp.Fiction.1994.1080p.BluRay.x264-SPARKS.mkv", 7, "Bluray-1080p", 3072, "SPARKS", 25);
            heat.File = new(11, "Heat.1995.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-EPSiLON.mkv", 30, "Remux-1080p", 4096, "EPSiLON");
            Write(library, pulp);
            Write(library, heat);
            radarr.Films.Add(new FakeRadarr.Film { Id = 3, Title = "Fight Club", Year = 1999, TmdbId = FightClub });
            radarr.History.Add(new FakeRadarr.Line(102, 2, "downloadFolderImported", later, "Pulp.Fiction.1994.1080p.BluRay.x264-SPARKS", 7, "Bluray-1080p", "ABCDEF0123456789ABCDEF0123456789ABCDEF01", new() { ["fileId"] = "20", ["importedPath"] = $"{FakeRadarr.RootPath}/Pulp Fiction (1994)/{pulp.File.RelativePath}" }));
            radarr.History.Add(new FakeRadarr.Line(103, 1, "movieFileDeleted", later.AddMinutes(1), $"{FakeRadarr.RootPath}/Heat (1995)/Heat.1995.1080p.BluRay.x264-GRP.mkv", 7, "Bluray-1080p", null, new() { ["reason"] = "Upgrade" }));
            radarr.History.Add(new FakeRadarr.Line(104, 1, "downloadFolderImported", later.AddMinutes(1).AddSeconds(1), "Heat.1995.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-EPSiLON", 30, "Remux-1080p", "HEAT0001", new() { ["fileId"] = "11" }));

            var heatFileBefore = (await VersionAsync(Heat)).File!.Id;
            var third = await SyncAsync(client, follow.Id);
            Assert.Contains("1 grab(s) imported or failed", third.Summary, StringComparison.Ordinal);
            Assert.Contains("1 film(s) added", third.Summary, StringComparison.Ordinal);

            var pulpNow = await VersionAsync(PulpFiction);
            Assert.Equal("Pulp.Fiction.1994.1080p.BluRay.x264-SPARKS.mkv", pulpNow.File!.RelativePath);
            Assert.Equal("SPARKS", pulpNow.File.ReleaseGroup);
            var heatNow = await VersionAsync(Heat);
            Assert.Equal(30, heatNow.File!.QualityId);
            Assert.NotEqual(heatFileBefore, heatNow.File.Id);
            Assert.NotNull(await VersionOrNullAsync(FightClub));

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var grab = await db.RadarrGrabs.AsNoTracking().SingleAsync();
                Assert.Equal(RadarrGrabState.Imported, grab.State);
                Assert.Equal(later, grab.ResolvedAt!.Value, TimeSpan.FromSeconds(1));

                // Radarr's history of the downtime, in the journal: the import after the upgrade deletion is an upgrade.
                var heatLines = await db.History.AsNoTracking().Where(h => h.TmdbId == Heat).OrderBy(h => h.CreatedAt).Select(h => h.EventType).ToListAsync();
                Assert.Equal([HistoryEventType.FileDeleted, HistoryEventType.Upgraded], heatLines);
                Assert.Equal(1, await db.History.CountAsync(h => h.TmdbId == PulpFiction && h.EventType == HistoryEventType.Imported));

                var saved = await db.FollowedRadarrs.AsNoTracking().SingleAsync();
                Assert.Equal(later.AddMinutes(1).AddSeconds(1), saved.HistoryCheckpoint, TimeSpan.FromMilliseconds(5));
                Assert.Null(saved.LastError);
            }

            // Radarr renames Pulp Fiction's file and deletes Heat's.
            pulp.File = pulp.File with { RelativePath = "Pulp Fiction (1994) Bluray-1080p.mkv" };
            Write(library, pulp);
            heat.File = null;
            radarr.History.Add(new FakeRadarr.Line(105, 2, "movieFileRenamed", later.AddMinutes(5), pulp.File.RelativePath, 7, "Bluray-1080p", null, new()));
            radarr.History.Add(new FakeRadarr.Line(106, 1, "movieFileDeleted", later.AddMinutes(6), "Heat.1995.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-EPSiLON.mkv", 30, "Remux-1080p", null, new() { ["reason"] = "Manual" }));
            var pulpFileBefore = pulpNow.File.Id;
            var fourth = await SyncAsync(client, follow.Id);
            Assert.Contains("1 file(s) renamed", fourth.Summary, StringComparison.Ordinal);
            Assert.Contains("1 file(s) gone", fourth.Summary, StringComparison.Ordinal);

            var renamed = await VersionAsync(PulpFiction);
            Assert.Equal(pulpFileBefore, renamed.File!.Id);
            Assert.Equal("Pulp Fiction (1994) Bluray-1080p.mkv", renamed.File.RelativePath);
            Assert.Null((await VersionAsync(Heat)).File);
            Assert.True(File.Exists(heatFirstFile), "the follow never touches the disk");

            // Radarr is down: the pass fails, the checkpoint stays, the next pass catches up.
            var checkpoint = (await client.GetFromJsonAsync<FollowedRadarrResponse>($"/api/v1/shadow/radarrs/{follow.Id}", Json))!.HistoryCheckpoint;
            radarr.Down = true;
            var failed = await client.PostAsync($"/api/v1/shadow/radarrs/{follow.Id}/sync", null);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            var down = (await client.GetFromJsonAsync<FollowedRadarrResponse>($"/api/v1/shadow/radarrs/{follow.Id}", Json))!;
            Assert.NotNull(down.LastError);
            Assert.Equal(checkpoint, down.HistoryCheckpoint);
            radarr.Down = false;
            await SyncAsync(client, follow.Id);
            Assert.Null((await client.GetFromJsonAsync<FollowedRadarrResponse>($"/api/v1/shadow/radarrs/{follow.Id}", Json))!.LastError);

            // Read only: nothing but GET ever reached Radarr.
            Assert.All(radarr.Requests, r => Assert.Equal("GET", r.Method));

            // Forgetting the Radarr forgets its grabs; Bigarr's picks stay.
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/shadow/radarrs/{follow.Id}")).StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                Assert.False(await db.RadarrGrabs.AnyAsync());
                Assert.True(await db.ShadowPicks.AnyAsync());
            }
        }
        finally
        {
            TryDelete(library);
        }
    }

    [Fact]
    public async Task A_follow_needs_a_key_a_valid_version_and_known_profiles()
    {
        var client = await ClientAsync(factory);
        var noKey = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest("No key", "http://radarr.invalid:7878", null, "1080p"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);

        var badVersion = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest("Bad version", "http://radarr.invalid:7878", "key", "Not A Slug"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, badVersion.StatusCode);

        var badProfile = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest("Bad profile", "http://radarr.invalid:7878", "key", "1080p", new() { [1] = 9999 }), Json);
        Assert.Equal(HttpStatusCode.BadRequest, badProfile.StatusCode);
    }

    [Fact]
    public async Task An_import_can_keep_following_its_radarr_from_the_moment_it_read_it()
    {
        var client = await ClientAsync(factory);
        var (_, uhd) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-follow-import-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Blade Runner", Year = 1982, TmdbId = 78, ProfileId = 2 });

        try
        {
            var before = DateTime.UtcNow;
            var started = await client.PostAsJsonAsync("/api/v1/migration/radarr/import", new Features.Migration.RadarrImportRequest(
                radarr.Url, FakeRadarr.Key, "4k", new() { [2] = uhd }, new() { [FakeRadarr.RootPath] = root.Id }, Follow: true), Json);
            Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
            await WaitUntilAsync(async () =>
            {
                var status = await client.GetFromJsonAsync<Features.Migration.RadarrImportStatusResponse>("/api/v1/migration/radarr/status", Json);
                return status!.State is Features.Migration.RadarrImportState.Completed or Features.Migration.RadarrImportState.Failed ? status : null;
            }, "the import");

            var followed = Assert.Single((await client.GetFromJsonAsync<List<FollowedRadarrResponse>>("/api/v1/shadow/radarrs", Json))!, f => f.VersionName == "4k");
            Assert.Equal("Radarr 4k", followed.Name);
            Assert.Equal(radarr.Url, followed.Url);
            Assert.Equal(FollowedRadarrService.KeyMask, followed.ApiKey);
            Assert.Equal(uhd, followed.ProfileMap[2]);
            Assert.Equal(root.Id, followed.RootFolderMap[FakeRadarr.RootPath]);
            Assert.InRange(followed.HistoryCheckpoint, before.AddSeconds(-1), DateTime.UtcNow);

            // The follow then runs with the stored key.
            await SyncAsync(client, followed.Id);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/shadow/radarrs/{followed.Id}")).StatusCode);
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

    private async Task<MovieVersion> VersionAsync(int tmdbId) =>
        await VersionOrNullAsync(tmdbId) ?? throw new InvalidOperationException($"TMDB {tmdbId} has no 1080p version.");

    private async Task<MovieVersion?> VersionOrNullAsync(int tmdbId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.MovieVersions.AsNoTracking().Include(v => v.File).Include(v => v.Movie).FirstOrDefaultAsync(v => v.Movie.TmdbId == tmdbId && v.Name == "1080p");
    }

    private async Task AddPickAsync(MovieVersion version, string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var now = DateTime.UtcNow;
        db.ShadowPicks.Add(new ShadowPick
        {
            VersionId = version.Id,
            MovieId = version.MovieId,
            TmdbId = version.Movie.TmdbId,
            MovieTitle = version.Movie.Title,
            VersionName = version.Name,
            Trigger = "rss",
            PickedAt = now,
            LastPickedAt = now,
            ReleaseTitle = title,
            ReleaseGuid = $"mock://{title}",
            Quality = "WEBDL-1080p",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Puts Radarr's file where Bigarr's root folder maps Radarr's: Bigarr only attaches a file it can see.</summary>
    private static string Write(string library, FakeRadarr.Film film)
    {
        var folder = Directory.CreateDirectory(Path.Combine(library, film.Folder)).FullName;
        var path = Path.Combine(folder, film.File!.RelativePath);
        File.WriteAllBytes(path, new byte[film.File.Size]);
        return path;
    }
}
