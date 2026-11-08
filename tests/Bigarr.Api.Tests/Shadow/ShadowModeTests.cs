using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>Boots the app with <c>Bigarr:Shadow:Enabled=true</c>, the way it runs next to Radarr.</summary>
public sealed class ShadowApiFactory() : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" });

/// <summary>
/// Shadow mode: every grab path records Bigarr's pick (one row per version and release) and no download client is ever
/// called, even a configured one; the version stays wanted; the health check says so without a warning.
/// </summary>
public sealed class ShadowModeTests(ShadowApiFactory factory) : IClassFixture<ShadowApiFactory>
{
    [Fact]
    public async Task Every_grab_path_records_a_pick_and_never_calls_a_download_client()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-shadow-{Guid.NewGuid():N}"));
        var matrix = await AddMovieAsync(client, 603, hd, root.Id);
        var inception = await AddMovieAsync(client, 27205, hd, root.Id);
        var indexer = await AddMockIndexerAsync(client, "Mock shadow");

        // A real client type pointed at a server that fails the test on any request.
        await using var qbittorrent = await FakeHttpServer.StartAsync();
        qbittorrent.Responder = _ => new FakeHttpServer.Answer(500);
        var host = new Uri(qbittorrent.Url);
        var added = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest("Never called", DownloadClientImplementation.QBittorrent, Host: host.Host, Port: host.Port, Username: "admin", Password: "secret"), Json);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        try
        {
            // RSS pass: the best release of each film is picked, nothing is grabbed.
            var pass = await (await client.PostAsync("/api/v1/rss/sync", null)).Content.ReadFromJsonAsync<RssSyncResponse>(Json);
            Assert.Equal(0, pass!.Grabbed);
            Assert.Equal(2, pass.Picked);

            var releases = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases?matched=true", Json))!;
            var picked = releases.Where(r => r.ShadowPickId is not null).ToList();
            Assert.Equal(2, picked.Count);
            Assert.All(picked, r => Assert.Null(r.GrabId));
            Assert.All(releases.Where(r => r.AttributedVersionId is not null && r.ShadowPickId is null), r => Assert.Contains("was picked for", r.GrabError, StringComparison.Ordinal));

            var matrixVersion = matrix.Versions.Single();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var picks = await db.ShadowPicks.AsNoTracking().OrderBy(p => p.TmdbId).ToListAsync();
                Assert.Equal(2, picks.Count);
                var matrixPick = picks.Single(p => p.TmdbId == 603);
                Assert.Equal(matrixVersion.Id, matrixPick.VersionId);
                Assert.Equal(matrix.Id, matrixPick.MovieId);
                Assert.Equal(matrixVersion.Name, matrixPick.VersionName);
                Assert.Equal("rss", matrixPick.Trigger);
                Assert.Equal("Bluray-1080p", matrixPick.Quality);
                Assert.Equal("Mock shadow", matrixPick.IndexerName);
                Assert.NotNull(matrixPick.PublishDate);
                Assert.Equal(1, matrixPick.Picks);

                // Nothing about the version moved: no grab, no journal line, no event.
                Assert.False(await db.Grabs.AnyAsync());
                Assert.False(await db.History.AnyAsync(h => h.EventType == HistoryEventType.Grabbed));
                Assert.False(await db.Outbox.AnyAsync(e => e.Type == DomainEventCatalog.NameOf(typeof(ReleaseGrabbed))));

                // Every release of the pass is kept as a sighting.
                Assert.Equal(pass.New, await db.ReleaseSightings.CountAsync());
            }

            // The interactive grab of the same release: the same row, picked once more.
            var search = await SearchAsync(client, matrix.Id);
            var release = search.Releases.Single(r => r.Title == picked.Single(p => p.Movie!.Id == matrix.Id).Title);
            var grab = await client.PostAsJsonAsync("/api/v1/grab", new GrabRequest(
                matrixVersion.Id, release.IndexerId, release.Guid, release.Title, release.DownloadUrl, release.MagnetUrl, release.InfoHash,
                release.Size, release.Seeders, release.Flags, release.Protocol, release.Indexer, release.PublishDate), Json);
            Assert.Equal(HttpStatusCode.OK, grab.StatusCode);
            var recorded = (await grab.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
            Assert.True(recorded.Shadow);
            Assert.Null(recorded.GrabId);
            Assert.Equal(picked.Single(p => p.Movie!.Id == matrix.Id).ShadowPickId, recorded.ShadowPickId);

            // An automatic search: the version is still wanted (a pick changes nothing stored), and what the search finds is
            // judged against the RSS pick, the file Bigarr would have had: only a release better than it is picked.
            var searched = (await (await client.PostAsync($"/api/v1/movies/{inception.Id}/search", null)).Content.ReadFromJsonAsync<List<AutomaticSearchResult>>(Json))!;
            var outcome = Assert.Single(searched);
            Assert.Equal(AutomaticSearchOutcome.Picked, outcome.Outcome);
            Assert.Null(outcome.GrabId);
            Assert.NotEqual(picked.Single(p => p.Movie!.Id == inception.Id).Title, outcome.ReleaseTitle);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var matrixPick = await db.ShadowPicks.AsNoTracking().SingleAsync(p => p.TmdbId == 603);
                Assert.Equal(2, matrixPick.Picks);
                Assert.Equal("rss", matrixPick.Trigger); // the first trigger stays
                Assert.True(matrixPick.LastPickedAt >= matrixPick.PickedAt);
                Assert.True(await db.ShadowPicks.AnyAsync(p => p.TmdbId == 27205 && p.ReleaseTitle == outcome.ReleaseTitle));
                Assert.False(await db.Grabs.AnyAsync());
            }

            // Health: a notice about shadow mode, no warning about the client nobody uses.
            var health = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
            var shadow = Assert.Single(health.Issues, i => i.Source == "shadow");
            Assert.Equal(HealthIssueType.Notice, shadow.Type);
            Assert.DoesNotContain(health.Issues, i => i.Source == "downloadClients");

            Assert.Empty(qbittorrent.Requests);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{matrix.Id}");
            await client.DeleteAsync($"/api/v1/movies/{inception.Id}");
            TryDelete(root.Path);
        }
    }

    [Fact]
    public async Task Without_a_download_client_shadow_mode_is_a_notice_not_a_warning()
    {
        var client = await ClientAsync(factory);
        var health = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
        Assert.Contains(health.Issues, i => i.Source == "shadow" && i.Type == HealthIssueType.Notice);
        Assert.DoesNotContain(health.Issues, i => i.Source == "downloadClients");

        var status = await client.GetFromJsonAsync<Features.Server.SystemStatusResponse>("/api/v1/system/status", Json);
        Assert.True(status!.ShadowEnabled);
    }
}
