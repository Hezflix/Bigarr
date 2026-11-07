using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>Shadow mode with a clock the test moves, for the hour between two reads of Radarr's whole film list.</summary>
public sealed class ShadowClockApiFactory() : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, time: Clock)
{
    public static readonly MutableTimeProvider Clock = new(DateTimeOffset.UtcNow);
}

/// <summary>
/// What a pass over a followed Radarr downloads: the whole film list only when it is needed (the first pass after start,
/// a film added or deleted, once an hour), otherwise the films its history names, one request each; and every answer
/// gzipped when Radarr offers it.
/// </summary>
public sealed class RadarrFollowTransferTests(ShadowClockApiFactory factory) : IClassFixture<ShadowClockApiFactory>
{
    private const string List = "/api/v3/movie";
    private const int Heat = 949;
    private const int Alien = 348;
    private const int Coco = 354912;
    private const int Matrix = 603;
    private const int Se7en = 807;

    [Fact]
    public async Task A_pass_reads_the_film_list_when_it_starts_and_not_again_while_nothing_asks_for_it()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-transfer-idle-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Alien", Year = 1979, TmdbId = Alien });

        try
        {
            var follow = await FollowAsync(client, radarr, hd, root.Id);

            // First pass after start: the whole list, so that nothing is missed.
            var first = await PassAsync(radarr, follow.Id);
            Assert.Contains(List, first);

            // Nothing happened in Radarr: its history, its profiles and its tags, and no film at all.
            var idle = await PassAsync(radarr, follow.Id);
            Assert.Equal(["/api/v3/history/since", "/api/v3/qualityprofile", "/api/v3/tag"], idle.Order().ToArray());
        }
        finally
        {
            TryDelete(library);
        }
    }

    [Fact]
    public async Task A_file_event_in_the_history_fetches_that_film_alone()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-transfer-file-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        var heat = new FakeRadarr.Film { Id = 1, Title = "Heat", Year = 1995, TmdbId = Heat, File = new(10, "Heat.1995.1080p.BluRay.x264-GRP.mkv", 7, "Bluray-1080p", 2048, "GRP") };
        radarr.Films.AddRange([heat, new FakeRadarr.Film { Id = 2, Title = "Alien", Year = 1979, TmdbId = Alien }]);
        Write(library, heat);

        try
        {
            var follow = await FollowAsync(client, radarr, hd, root.Id);
            await PassAsync(radarr, follow.Id);
            Assert.Equal(7, (await VersionAsync(Heat)).File!.QualityId);

            // Radarr upgrades Heat's file: one history line, no webhook.
            heat.File = new(11, "Heat.1995.1080p.BluRay.REMUX.AVC-EPSiLON.mkv", 30, "Remux-1080p", 4096, "EPSiLON");
            Write(library, heat);
            var at = DateTime.UtcNow.AddMinutes(1);
            radarr.History.Add(new FakeRadarr.Line(1, 1, "movieFileDeleted", at, "Heat.1995.1080p.BluRay.x264-GRP.mkv", 7, "Bluray-1080p", null, new() { ["reason"] = "Upgrade" }));
            radarr.History.Add(new FakeRadarr.Line(2, 1, "downloadFolderImported", at.AddSeconds(1), "Heat.1995.1080p.BluRay.REMUX.AVC-EPSiLON", 30, "Remux-1080p", "HEAT0001", new() { ["fileId"] = "11" }));

            var paths = await PassAsync(radarr, follow.Id);
            Assert.Contains("/api/v3/movie/1", paths);
            Assert.DoesNotContain("/api/v3/movie/2", paths);
            Assert.DoesNotContain(List, paths);
            Assert.Equal(30, (await VersionAsync(Heat)).File!.QualityId);
        }
        finally
        {
            TryDelete(library);
        }
    }

    [Fact]
    public async Task A_movie_added_webhook_makes_the_next_pass_read_the_list_and_import_the_film()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-transfer-added-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Alien", Year = 1979, TmdbId = Alien });

        try
        {
            var follow = await FollowAsync(client, radarr, hd, root.Id);
            await PassAsync(radarr, follow.Id);
            var created = await client.PostAsJsonAsync("/api/v1/apikeys", new CreateApiKeyRequest("Radarr webhook", "default"), Json);
            var key = (await created.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>(Json))!.Key;
            var url = $"/api/v1/shadow/radarrs/{follow.Id}/webhook?apikey={key}";

            // A grab (or anything else that history carries) wakes a pass that reads no list.
            var before = radarr.Requests.Count();
            Assert.Equal(HttpStatusCode.Accepted, (await factory.CreateAnonymousClient().PostAsync(url, Body("Grab"))).StatusCode);
            await WaitUntilAsync(async () => radarr.Requests.Skip(before).Any(r => r.Path == "/api/v3/tag") ? "passed" : null, "the pass the grab asked for");
            Assert.DoesNotContain(List, radarr.Requests.Skip(before).Select(r => r.Path));

            // A film added to Radarr is not in its history: the webhook says so, and the pass reads the list.
            radarr.Films.Add(new FakeRadarr.Film { Id = 2, Title = "Coco", Year = 2017, TmdbId = Coco });
            before = radarr.Requests.Count();
            Assert.Equal(HttpStatusCode.Accepted, (await factory.CreateAnonymousClient().PostAsync(url, Body("MovieAdded"))).StatusCode);
            await WaitUntilAsync(async () => radarr.Requests.Skip(before).Any(r => r.Path == List) ? "read" : null, "the list read");
            await WaitUntilAsync(async () => await VersionOrNullAsync(Coco) is { } v ? v : null, "Coco imported by the pass the webhook asked for");
            Assert.Contains(List, radarr.Requests.Skip(before).Select(r => r.Path));
        }
        finally
        {
            TryDelete(library);
        }
    }

    [Fact]
    public async Task The_list_is_read_again_every_hour_to_catch_what_history_does_not_say()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-transfer-hourly-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        var heat = new FakeRadarr.Film { Id = 1, Title = "Se7en", Year = 1995, TmdbId = Se7en, File = new(10, "Se7en.1995.1080p.BluRay.x264-GRP.mkv", 7, "Bluray-1080p", 2048, "GRP") };
        radarr.Films.Add(heat);
        Write(library, heat);

        try
        {
            var follow = await FollowAsync(client, radarr, hd, root.Id);
            await PassAsync(radarr, follow.Id);

            // Radarr loses Se7en's file and gains Matrix, with no history line and no webhook.
            heat.File = null;
            radarr.Films.Add(new FakeRadarr.Film { Id = 2, Title = "The Matrix", Year = 1999, TmdbId = Matrix });

            ShadowClockApiFactory.Clock.Advance(TimeSpan.FromMinutes(15));
            Assert.DoesNotContain(List, await PassAsync(radarr, follow.Id));
            Assert.NotNull((await VersionAsync(Se7en)).File);
            Assert.Null(await VersionOrNullAsync(Matrix));

            ShadowClockApiFactory.Clock.Advance(RadarrFollower.FullListInterval);
            Assert.Contains(List, await PassAsync(radarr, follow.Id));
            Assert.Null((await VersionAsync(Se7en)).File);
            Assert.NotNull(await VersionOrNullAsync(Matrix));
        }
        finally
        {
            TryDelete(library);
        }
    }

    [Fact]
    public async Task Radarr_answers_are_gzipped_when_it_offers_and_decoded_by_bigarr()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var library = Directory.CreateTempSubdirectory("bigarr-transfer-gzip-").FullName;
        var root = await AddRootAsync(client, library);
        await using var radarr = await FakeRadarr.StartAsync();
        radarr.Gzip = true;
        radarr.Films.Add(new FakeRadarr.Film { Id = 1, Title = "Alien", Year = 1979, TmdbId = Alien });

        try
        {
            var follow = await FollowAsync(client, radarr, hd, root.Id);
            await PassAsync(radarr, follow.Id);

            var list = radarr.Requests.First(r => r.Path == List);
            Assert.Contains("gzip", list.Headers["Accept-Encoding"], StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(await VersionOrNullAsync(Alien));
        }
        finally
        {
            TryDelete(library);
        }
    }

    /// <summary>One pass as the schedule runs it (the list only when due), and the paths it asked Radarr for.</summary>
    private async Task<List<string>> PassAsync(FakeRadarr radarr, int id)
    {
        var before = radarr.Requests.Count();
        using var scope = factory.Services.CreateScope();
        var pass = await scope.ServiceProvider.GetRequiredService<RadarrFollower>().SyncAsync(id, CancellationToken.None);
        Assert.True(pass.Success, pass.Summary);
        return radarr.Requests.Skip(before).Select(r => r.Path).ToList();
    }

    private static StringContent Body(string eventType) =>
        new($$"""{"eventType":"{{eventType}}","instanceName":"Radarr"}""", Encoding.UTF8, "application/json");

    private static async Task<FollowedRadarrResponse> FollowAsync(HttpClient client, FakeRadarr radarr, int profileId, int rootId)
    {
        var created = await client.PostAsJsonAsync("/api/v1/shadow/radarrs", new FollowedRadarrRequest(
            $"Radarr {Guid.NewGuid():N}"[..20], radarr.Url, FakeRadarr.Key, "1080p", new() { [1] = profileId }, new() { [FakeRadarr.RootPath] = rootId }), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<FollowedRadarrResponse>(Json))!;
    }

    private static async Task<FollowedRadarrResponse> GetAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<FollowedRadarrResponse>($"/api/v1/shadow/radarrs/{id}", Json))!;

    private async Task<MovieVersion> VersionAsync(int tmdbId) =>
        await VersionOrNullAsync(tmdbId) ?? throw new InvalidOperationException($"TMDB {tmdbId} has no 1080p version.");

    private async Task<MovieVersion?> VersionOrNullAsync(int tmdbId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.MovieVersions.AsNoTracking().Include(v => v.File).FirstOrDefaultAsync(v => v.Movie.TmdbId == tmdbId && v.Name == "1080p");
    }

    private static void Write(string library, FakeRadarr.Film film)
    {
        var folder = Directory.CreateDirectory(Path.Combine(library, film.Folder)).FullName;
        File.WriteAllBytes(Path.Combine(folder, film.File!.RelativePath), new byte[film.File.Size]);
    }
}
