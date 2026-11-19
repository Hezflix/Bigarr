using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Server;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Downloads.Mock;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>Radarr replaced, Sonarr still compared: <c>Bigarr:Shadow:Enabled=true</c> with <c>Films=false</c>.</summary>
public sealed class FilmsLiveShadowApiFactory() : BigarrApiFactory(
    null,
    new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true", ["Bigarr:Shadow:Films"] = "false" },
    deleteOnDispose: true,
    time: new FixedTimeProvider(SeriesEndpointsTests.Now));

/// <summary>Films and series leave shadow mode one at a time: the kind still in it records picks and owns nothing in the client.</summary>
public sealed class FilmsLiveShadowTests(FilmsLiveShadowApiFactory factory) : IClassFixture<FilmsLiveShadowApiFactory>
{
    [Fact]
    public async Task A_film_grab_reaches_the_client_while_a_series_grab_stays_a_pick()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-films-live-{Guid.NewGuid():N}"));
        var matrix = await AddMovieAsync(client, 603, hd, root.Id);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var downloadClient = await MockClientAsync(client);
        int? grabId = null;
        try
        {
            var release = (await SearchAsync(client, matrix.Id)).Releases.First(r => r.MatchesMovie && r.Versions.Single().Approved);
            var filmGrab = await client.PostAsJsonAsync("/api/v1/grab", new GrabRequest(
                matrix.Versions.Single().Id, release.IndexerId, release.Guid, release.Title, release.DownloadUrl, release.MagnetUrl, release.InfoHash,
                release.Size, release.Seeders, release.Flags, release.Protocol, release.Indexer, release.PublishDate), Json);
            Assert.Equal(HttpStatusCode.Created, filmGrab.StatusCode);
            var film = (await filmGrab.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
            Assert.False(film.Shadow);
            Assert.NotNull(film.GrabId);
            grabId = film.GrabId;

            var pack = (await SeriesSearchTests.SearchAsync(client, series.Id, "season=1")).Releases[0];
            var seriesGrab = await client.PostAsJsonAsync("/api/v1/grab", SeriesSetup.Grab(pack), Json);
            Assert.Equal(HttpStatusCode.OK, seriesGrab.StatusCode);
            var episode = (await seriesGrab.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
            Assert.True(episode.Shadow);
            Assert.NotNull(episode.ShadowPickId);

            var torrents = factory.Services.GetRequiredService<MockDownloadClientState>().All;
            Assert.Contains(torrents, t => t.Hash == film.DownloadId && t.Category == downloadClient.Category);
            Assert.DoesNotContain(torrents, t => t.Category == downloadClient.SeriesCategory);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.False(await db.ShadowPicks.AnyAsync(p => p.MovieId == matrix.Id));
            Assert.True(await db.SeriesShadowPicks.AnyAsync(p => p.Id == episode.ShadowPickId));
        }
        finally
        {
            if (grabId is { } id)
            {
                await client.DeleteAsync($"/api/v1/queue/{id}?removeFromClient=true&deleteFiles=true&blocklist=false");
            }

            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/movies/{matrix.Id}");
            TryDelete(root.Path);
        }
    }

    [Fact]
    public async Task The_queue_leaves_the_series_category_to_the_app_still_compared()
    {
        var client = await ClientAsync(factory);
        var downloadClient = await MockClientAsync(client);
        var torrents = factory.Services.GetRequiredService<MockDownloadClientState>();
        var filmHash = Guid.NewGuid().ToString("N");
        var seriesHash = Guid.NewGuid().ToString("N");
        torrents.Add(filmHash, "The.Matrix.1999.1080p.BluRay.x264-HAND", downloadClient.Category, 1_000_000, paused: false);
        torrents.Add(seriesHash, "Breaking.Bad.S01E01.1080p.WEB-DL-HAND", downloadClient.SeriesCategory, 1_000_000, paused: false);
        try
        {
            var queue = (await (await client.PostAsync("/api/v1/queue/refresh", null)).Content.ReadFromJsonAsync<QueueResponse>(Json))!;
            Assert.Contains(queue.Items, i => i.DownloadId == filmHash);
            Assert.DoesNotContain(queue.Items, i => i.DownloadId == seriesHash);
        }
        finally
        {
            torrents.Remove(filmHash);
            torrents.Remove(seriesHash);
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    [Fact]
    public async Task Status_and_health_say_which_kind_is_still_compared_and_warn_about_a_radarr_still_followed()
    {
        var client = await ClientAsync(factory);

        var status = (await client.GetFromJsonAsync<SystemStatusResponse>("/api/v1/system/status", Json))!;
        Assert.True(status.ShadowEnabled);
        Assert.False(status.ShadowFilms);
        Assert.True(status.ShadowSeries);

        var shadow = (await client.GetFromJsonAsync<ShadowStatusResponse>("/api/v1/shadow", Json))!;
        Assert.True(shadow.Enabled);
        Assert.False(shadow.Films);
        Assert.True(shadow.Series);

        int followId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var follow = new FollowedRadarr { Name = $"radarr-{Guid.NewGuid():N}"[..20], Url = "http://radarr.invalid:7878", ApiKey = "key", VersionName = "1080p" };
            db.FollowedRadarrs.Add(follow);
            await db.SaveChangesAsync();
            followId = follow.Id;
        }

        try
        {
            var health = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
            var notice = Assert.Single(health.Issues, i => i.Source == "shadow");
            Assert.Equal(HealthIssueType.Notice, notice.Type);
            Assert.Contains("series only", notice.Message, StringComparison.Ordinal);
            Assert.Contains(health.Issues, i => i.Source == "followedRadarrs" && i.Type == HealthIssueType.Warning);
            Assert.DoesNotContain(health.Issues, i => i.Source == "followedSonarrs");
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            await db.FollowedRadarrs.Where(r => r.Id == followId).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData(false, null, null, false, false)]
    [InlineData(true, null, null, true, true)]
    [InlineData(true, false, null, false, true)]
    [InlineData(true, null, false, true, false)]
    [InlineData(false, true, null, true, false)]
    public void Each_kind_follows_Enabled_unless_overridden(bool enabled, bool? films, bool? series, bool filmsInShadow, bool seriesInShadow)
    {
        var options = new ShadowOptions { Enabled = enabled, Films = films, Series = series };
        Assert.Equal(filmsInShadow, options.FilmsInShadow);
        Assert.Equal(seriesInShadow, options.SeriesInShadow);
        Assert.Equal(filmsInShadow || seriesInShadow, options.AnyInShadow);
    }

    private static async Task<DownloadClientResponse> MockClientAsync(HttpClient client)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var created = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest($"Mock {id}", DownloadClientImplementation.Mock, Category: $"films-{id}", SeriesCategory: $"tv-{id}"), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
    }
}
