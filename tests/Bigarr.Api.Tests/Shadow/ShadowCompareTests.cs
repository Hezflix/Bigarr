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
/// Activity → Compare end to end: Radarr's grabs read by the follow, Bigarr's picks, paired into the four outcomes, with
/// the reasons on each side: Bigarr's engine on a release only Radarr took (rejected, or not even this film), whether
/// Bigarr's feeds ever saw it, and Radarr's parse of a release only Bigarr picked.
/// </summary>
public sealed class ShadowCompareTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    private const int Heat = 949;
    private const int Matrix = 603;
    private const int Inception = 27205;
    private const int FightClub = 550;
    private const int Interstellar = 157336;

    [Fact]
    public async Task Grabs_and_picks_are_paired_and_each_side_says_why()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-compare-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.AddRange([
            new FakeRadarr.Film { Id = 1, Title = "Heat", Year = 1995, TmdbId = Heat },
            new FakeRadarr.Film { Id = 2, Title = "The Matrix", Year = 1999, TmdbId = Matrix },
            new FakeRadarr.Film { Id = 3, Title = "Inception", Year = 2010, TmdbId = Inception, File = new(30, "Inception.2010.1080p.BluRay.x264-GRP.mkv", 7, "Bluray-1080p", 1024, "GRP", 100) },
            new FakeRadarr.Film { Id = 4, Title = "Fight Club", Year = 1999, TmdbId = FightClub },
            new FakeRadarr.Film { Id = 5, Title = "Interstellar", Year = 2014, TmdbId = Interstellar },
        ]);
        radarr.Parse = title => title.StartsWith("Inception", StringComparison.Ordinal) ? (3, "Bluray-1080p", ["Radarr x264"], 0) : (0, "Unknown", [], 0);

        try
        {
            var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
                "Radarr", radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = hd }, new() { [FakeRadarr.RootPath] = root.Id }), Json);
            var follow = (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
            await SyncAsync(client, follow.Id);

            var now = DateTime.UtcNow;
            await PickAsync(Heat, "Heat.1995.1080p.BluRay.x264-SPARKS", "g-heat", now.AddMinutes(-10));
            await PickAsync(Matrix, "The.Matrix.1999.1080p.WEB-DL.DDP5.1.H.264-NTb", "g-matrix-web", now.AddHours(-3));
            await PickAsync(Inception, "Inception.2010.1080p.BluRay.REMUX.AVC.DTS-HD.MA.5.1-EPSiLON", "g-inception-remux", now.AddHours(-30));

            radarr.History.AddRange([
                Grabbed(201, 1, now, "Heat.1995.1080p.BluRay.x264-SPARKS", "g-heat", 7, "Bluray-1080p"),
                Grabbed(202, 2, now, "The.Matrix.1999.1080p.BluRay.x264-SPARKS", "g-matrix-bluray", 7, "Bluray-1080p"),
                // Bigarr's 1080p profile does not allow 2160p.
                Grabbed(203, 4, now, "Fight.Club.1999.2160p.UHD.BluRay.x265-TERMiNAL", "g-fight-club", 19, "Bluray-2160p"),
                // A title that is not Interstellar's: Bigarr would not even match it to the film.
                Grabbed(204, 5, now, "Gone.Girl.2014.1080p.BluRay.x264-GRP", "g-gone-girl", 7, "Bluray-1080p"),
            ]);

            // The RSS pass saw Fight Club's release (and judged it then); Gone Girl's never came through Bigarr's feeds.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                db.ReleaseSightings.Add(new ReleaseSighting
                {
                    IndexerName = "Some Indexer (Prowlarr)",
                    Guid = "g-fight-club",
                    Title = "Fight.Club.1999.2160p.UHD.BluRay.x265-TERMiNAL",
                    SeenAt = now.AddMinutes(-20),
                    TmdbId = FightClub,
                    Verdicts = [new SightingVerdict("1080p", false, ["Quality Bluray-2160p is not wanted in profile 1080p"], 0)],
                });
                await db.SaveChangesAsync();
            }

            await SyncAsync(client, follow.Id);

            var compare = (await client.GetFromJsonAsync<CompareResponse>("/api/v1/shadow/compare", Json))!;
            Assert.True(compare.ShadowEnabled);
            Assert.Equal(new CompareCounts(1, 1, 1, 2), compare.Summary);
            Assert.Equal("1080p", Assert.Single(compare.ByVersion).VersionName);
            Assert.Equal(["1080p"], compare.Versions);
            Assert.Equal(5, compare.Total);

            var same = Assert.Single(compare.Rows, r => r.Outcome == CompareOutcome.Same);
            Assert.Equal(Heat, same.TmdbId);
            Assert.NotNull(same.MovieId);
            Assert.InRange(same.BigarrLeadSeconds!.Value, 9 * 60, 11 * 60); // Bigarr picked it ten minutes before Radarr grabbed it

            var different = Assert.Single(compare.Rows, r => r.Outcome == CompareOutcome.Different);
            Assert.Equal("The.Matrix.1999.1080p.BluRay.x264-SPARKS", different.Radarr!.Title);
            Assert.Equal("The.Matrix.1999.1080p.WEB-DL.DDP5.1.H.264-NTb", different.Bigarr!.Title);
            Assert.True(different.Radarr.Bigarr!.Approved); // Bigarr would have taken Radarr's too

            var bigarrOnly = Assert.Single(compare.Rows, r => r.Outcome == CompareOutcome.BigarrOnly);
            Assert.Equal(Inception, bigarrOnly.TmdbId);
            var view = bigarrOnly.Bigarr!.Radarr!;
            Assert.Equal(Inception, view.TmdbId);
            Assert.Equal("Bluray-1080p", view.FileQuality);
            Assert.Equal(100, view.FileScore);
            Assert.Equal(0, view.CustomFormatScore);

            var radarrOnly = compare.Rows.Where(r => r.Outcome == CompareOutcome.RadarrOnly).ToDictionary(r => r.TmdbId);
            var rejected = radarrOnly[FightClub];
            Assert.False(rejected.Radarr!.Bigarr!.Approved);
            Assert.Null(rejected.Radarr.Bigarr.Unmatched);
            Assert.Contains(rejected.Radarr.Bigarr.Rejections, r => r.Contains("2160p", StringComparison.Ordinal));
            Assert.NotNull(rejected.Sighting);
            Assert.True(rejected.Sighting.Matched);
            Assert.False(rejected.Sighting.Verdict!.Approved);

            var unmatched = radarrOnly[Interstellar];
            Assert.NotNull(unmatched.Radarr!.Bigarr!.Unmatched);
            Assert.Null(unmatched.Sighting); // never seen in Bigarr's feeds

            // Filters.
            var onlyRadarr = (await client.GetFromJsonAsync<CompareResponse>("/api/v1/shadow/compare?outcome=radarrOnly", Json))!;
            Assert.Equal(2, onlyRadarr.Rows.Count);
            Assert.Equal(compare.Summary, onlyRadarr.Summary); // the counts ignore the outcome filter
            var none = (await client.GetFromJsonAsync<CompareResponse>("/api/v1/shadow/compare?version=4k", Json))!;
            Assert.Empty(none.Rows);
            Assert.Equal(0, none.Summary.Total);
            var lastDay = (await client.GetFromJsonAsync<CompareResponse>("/api/v1/shadow/compare?days=1", Json))!;
            Assert.DoesNotContain(lastDay.Rows, r => r.Outcome == CompareOutcome.BigarrOnly); // Inception's pick is 30 hours old
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/shadow/compare?outcome=nope")).StatusCode);
        }
        finally
        {
            TryDelete(library);
        }
    }

    private static FakeRadarr.Line Grabbed(int id, int movieId, DateTime date, string title, string guid, int qualityId, string quality) =>
        new(id, movieId, "grabbed", date, title, qualityId, quality, $"DL{id}", new()
        {
            ["indexer"] = "Some Indexer (Prowlarr)",
            ["guid"] = guid,
            ["size"] = "8589934592",
            ["releaseSource"] = "Rss",
            ["customFormatScore"] = "0",
        });

    private static async Task SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/radarrs/{id}/sync", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task PickAsync(int tmdbId, string title, string guid, DateTime at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var version = await db.MovieVersions.AsNoTracking().Include(v => v.Movie).SingleAsync(v => v.Movie.TmdbId == tmdbId && v.Name == "1080p");
        db.ShadowPicks.Add(new ShadowPick
        {
            VersionId = version.Id,
            MovieId = version.MovieId,
            TmdbId = tmdbId,
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
