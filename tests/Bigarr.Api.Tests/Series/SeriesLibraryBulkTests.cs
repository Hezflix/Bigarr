using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// The series library page's API: the list carries what the page filters, sorts and colours on (per version status,
/// qualities, cutoff, root folder, tags), and the bulk editor mirrors the films' (one version of many series).
/// </summary>
public sealed class SeriesLibraryBulkTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    private const int BreakingBad = 81189;
    private const int Frieren = 424536;
    private const long FileSize = 4096;

    [Fact]
    public async Task The_list_carries_status_qualities_cutoff_and_settings_per_version_and_matches_the_series_page()
    {
        var client = await ClientAsync();
        var (hd, root) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, BreakingBad);
        await DeleteIfPresentAsync(client, Frieren);
        var kids = await TagAsync(client, "kids-list");
        var bb = await AddAsync(client, new AddSeriesRequest(hd, root, TvdbId: BreakingBad));
        var frieren = await AddAsync(client, new AddSeriesRequest(hd, root, TvdbId: Frieren, MonitorNewItems: NewItemMonitoring.None, TagIds: [kids]));
        var bbHd = bb.Versions.Single().Id;
        var bb4k = await AddVersionAsync(client, bb.Id, new AddSeriesVersionRequest("4k", hd));

        // 1080p: a file for every regular episode and one special; 4k: one 720p file (below the cutoff) and a grab running.
        await FilesAsync(bbHd, ParserQuality.Bluray1080p.Id, regular: int.MaxValue, specials: 1);
        await FilesAsync(bb4k, ParserQuality.HDTV720p.Id, regular: 1, specials: 0);
        await GrabAsync(bb4k);

        var list = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!;
        var bbRow = list.Single(s => s.Id == bb.Id);
        var hdRow = bbRow.Versions.Single(v => v.Id == bbHd);
        Assert.Equal((VersionStatus.Available, true, root), (hdRow.Status, hdRow.CutoffMet, hdRow.RootFolderId));
        Assert.Equal(["Bluray-1080p"], hdRow.Qualities);
        Assert.Equal((62, 62, 63 * FileSize), (hdRow.Statistics.EpisodeCount, hdRow.Statistics.EpisodeFileCount, hdRow.Statistics.SizeOnDisk));

        var uhdRow = bbRow.Versions.Single(v => v.Id == bb4k);
        Assert.Equal((VersionStatus.Downloading, false), (uhdRow.Status, uhdRow.CutoffMet));
        Assert.Equal(["HDTV-720p"], uhdRow.Qualities);

        var frierenRow = list.Single(s => s.Id == frieren.Id).Versions.Single();
        Assert.Equal((VersionStatus.Missing, (bool?)null, NewItemMonitoring.None), (frierenRow.Status, frierenRow.CutoffMet, frierenRow.MonitorNewItems));
        Assert.Equal([kids], frierenRow.Tags);
        Assert.Empty(frierenRow.Qualities!);

        // The list's light reads give the same figures as the series page's per-season statistics.
        foreach (var row in list.Where(s => s.Id == bb.Id || s.Id == frieren.Id))
        {
            var detail = (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{row.Id}", Json))!;
            Assert.Equal(detail.Versions.Select(v => (v.Id, v.Statistics)), row.Versions.Select(v => (v.Id, v.Statistics)));
        }

        // Nothing aired and monitored yet, no file: pending, like a film before its release.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            await db.EpisodeTargets.Where(t => t.VersionId == frierenRow.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Monitored, false));
        }

        var after = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!;
        Assert.Equal(VersionStatus.Pending, after.Single(s => s.Id == frieren.Id).Versions.Single().Status);
    }

    [Fact]
    public async Task Bulk_update_changes_the_selected_version_skips_series_without_it_and_rejects_bad_requests()
    {
        var client = await ClientAsync();
        var (hd, root) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, BreakingBad);
        await DeleteIfPresentAsync(client, Frieren);
        var bb = await AddAsync(client, new AddSeriesRequest(hd, root, TvdbId: BreakingBad));
        var frieren = await AddAsync(client, new AddSeriesRequest(hd, root, TvdbId: Frieren));
        await AddVersionAsync(client, bb.Id, new AddSeriesVersionRequest("4k", hd));
        int[] ids = [bb.Id, frieren.Id];

        var unmonitored = await UpdateAsync(client, new SeriesBulkUpdateRequest([.. ids], "version:4k", Monitored: false));
        Assert.Equal([BulkOutcome.Updated, BulkOutcome.Skipped], unmonitored.Results.Select(r => r.Outcome));
        Assert.Equal(["4k"], unmonitored.Results[0].Versions);
        Assert.Equal("No 4k version", unmonitored.Results[1].Detail);
        var detail = await GetAsync(client, bb.Id);
        Assert.False(detail.Versions.Single(v => v.Name == "4k").Monitored);
        Assert.True(detail.Versions.Single(v => v.IsDefault).Monitored);

        // The same request again changes nothing.
        Assert.Equal(BulkOutcome.Unchanged, (await UpdateAsync(client, new SeriesBulkUpdateRequest([.. ids], "version:4k", Monitored: false))).Results[0].Outcome);

        var newItems = await UpdateAsync(client, new SeriesBulkUpdateRequest([.. ids, 987654], "all", MonitorNewItems: NewItemMonitoring.None));
        Assert.Equal(["1080p", "4k"], newItems.Results[0].Versions.Order());
        Assert.Equal((BulkOutcome.Skipped, "Not in the library"), (newItems.Results[2].Outcome, newItems.Results[2].Detail));
        Assert.All((await GetAsync(client, bb.Id)).Versions, v => Assert.Equal(NewItemMonitoring.None, v.MonitorNewItems));

        // The series type belongs to the series: it changes even where the version is missing.
        var type = await UpdateAsync(client, new SeriesBulkUpdateRequest([.. ids], "version:4k", SeriesType: SeriesType.Daily));
        Assert.All(type.Results, r => Assert.Equal(BulkOutcome.Updated, r.Outcome));
        Assert.Equal(SeriesType.Daily, (await GetAsync(client, frieren.Id)).SeriesType);

        var uhd = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!.Single(p => p.Name == QualitySeeder.SeriesUhdProfileName).Id;
        await UpdateAsync(client, new SeriesBulkUpdateRequest([.. ids], QualityProfileId: uhd));
        Assert.Equal(uhd, (await GetAsync(client, frieren.Id)).Versions.Single().QualityProfileId);
        Assert.Equal(hd, (await GetAsync(client, bb.Id)).Versions.Single(v => v.Name == "4k").QualityProfileId);

        var kids = await TagAsync(client, "kids-bulk");
        var anime = await TagAsync(client, "anime-bulk");
        async Task<List<int>> TagsAfterAsync(List<int> tagIds, TagsMode mode)
        {
            await UpdateAsync(client, new SeriesBulkUpdateRequest([frieren.Id], TagIds: tagIds, ApplyTags: mode));
            return (await GetAsync(client, frieren.Id)).Versions.Single().Tags;
        }

        Assert.Equal([kids], await TagsAfterAsync([kids], TagsMode.Add));
        Assert.Equal(new[] { kids, anime }.Order(), await TagsAfterAsync([anime], TagsMode.Add));
        Assert.Equal([anime], await TagsAfterAsync([kids], TagsMode.Remove));
        Assert.Empty(await TagsAfterAsync([], TagsMode.Replace));

        var film = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!.First(p => p.Kind == ProfileKind.Movies).Id;
        foreach (var bad in new[]
        {
            new SeriesBulkUpdateRequest([frieren.Id], "4K", Monitored: true),
            new SeriesBulkUpdateRequest([], Monitored: true),
            new SeriesBulkUpdateRequest([frieren.Id]),
            new SeriesBulkUpdateRequest([frieren.Id], Monitored: false, QualityProfileId: 999),
            new SeriesBulkUpdateRequest([frieren.Id], Monitored: false, QualityProfileId: film),
            new SeriesBulkUpdateRequest([frieren.Id], Monitored: false, TagIds: [999]),
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/series/bulk", bad, Json)).StatusCode);
        }

        Assert.True((await GetAsync(client, frieren.Id)).Versions.Single().Monitored);
    }

    [Fact]
    public async Task Bulk_delete_states_its_plan_then_follows_the_film_rules()
    {
        var client = await ClientAsync();
        var (hd, root) = await SetupAsync(client);
        await DeleteIfPresentAsync(client, BreakingBad);
        await DeleteIfPresentAsync(client, Frieren);
        var bb = await AddAsync(client, new AddSeriesRequest(hd, root, TvdbId: BreakingBad));
        var frieren = await AddAsync(client, new AddSeriesRequest(hd, root, TvdbId: Frieren));
        var bb4k = await AddVersionAsync(client, bb.Id, new AddSeriesVersionRequest("4k", hd));
        await FilesAsync(bb.Versions.Single().Id, ParserQuality.Bluray1080p.Id, regular: 3, specials: 0);
        await FilesAsync(bb4k, ParserQuality.Bluray1080p.Id, regular: 2, specials: 0);
        int[] ids = [bb.Id, frieren.Id];

        var defaults = await DeleteAsync(client, new SeriesBulkDeleteRequest([.. ids], "default", DeleteFiles: true, DryRun: true));
        Assert.True(defaults.DryRun);
        Assert.Equal((BulkOutcome.VersionsDeleted, "4k", 3), (defaults.Results[0].Outcome, defaults.Results[0].Promoted, defaults.Results[0].Files));
        Assert.Equal(BulkOutcome.SeriesDeleted, defaults.Results[1].Outcome);

        var all = await DeleteAsync(client, new SeriesBulkDeleteRequest([.. ids], "all", DeleteFiles: true, DryRun: true));
        Assert.Equal((BulkOutcome.SeriesDeleted, 5), (all.Results[0].Outcome, all.Results[0].Files));
        Assert.Equal(["1080p", "4k"], all.Results[0].Versions);

        var skipped = await DeleteAsync(client, new SeriesBulkDeleteRequest([.. ids], "version:4k", DryRun: true));
        Assert.Equal((BulkOutcome.VersionsDeleted, 0), (skipped.Results[0].Outcome, skipped.Results[0].Files));
        Assert.Equal((BulkOutcome.Skipped, "No 4k version"), (skipped.Results[1].Outcome, skipped.Results[1].Detail));

        // The dry runs changed nothing.
        Assert.Equal(2, (await GetAsync(client, bb.Id)).Versions.Count);

        var done = await DeleteAsync(client, new SeriesBulkDeleteRequest([.. ids], "default"));
        Assert.False(done.DryRun);
        Assert.Equal([BulkOutcome.VersionsDeleted, BulkOutcome.SeriesDeleted], done.Results.Select(r => r.Outcome));
        var left = await GetAsync(client, bb.Id);
        Assert.Equal(("4k", true), (left.Versions.Single().Name, left.Versions.Single().IsDefault));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/series/{frieren.Id}")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.False(await db.Episodes.AnyAsync(e => e.SeriesId == frieren.Id));
        Assert.Equal(0, await db.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM AcquisitionTargets a WHERE a.Kind = {(int)TargetKind.Episode} AND NOT EXISTS (SELECT 1 FROM EpisodeTargets e WHERE e.Id = a.Id)")
            .SingleAsync());
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Episode files for the first <paramref name="regular"/> regular episodes and <paramref name="specials"/> specials, linked to the version's targets.</summary>
    private async Task FilesAsync(int versionId, int qualityId, int regular, int specials)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var targets = await db.EpisodeTargets.Include(t => t.Episode).Where(t => t.VersionId == versionId).OrderBy(t => t.Episode.SeasonNumber).ThenBy(t => t.Episode.EpisodeNumber).ToListAsync();
        var picked = targets.Where(t => t.Episode.SeasonNumber > 0).Take(regular).Concat(targets.Where(t => t.Episode.SeasonNumber == 0).Take(specials)).ToList();
        foreach (var target in picked)
        {
            var file = new EpisodeFile
            {
                VersionId = versionId,
                RelativePath = $"v{versionId}/S{target.Episode.SeasonNumber:00}E{target.Episode.EpisodeNumber:00}.mkv",
                SeasonNumber = target.Episode.SeasonNumber,
                Size = FileSize,
                QualityId = qualityId,
            };
            db.EpisodeFiles.Add(file);
            target.EpisodeFile = file;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>An active grab on the version's last episode.</summary>
    private async Task GrabAsync(int versionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var target = await db.EpisodeTargets.Where(t => t.VersionId == versionId).OrderByDescending(t => t.Id).FirstAsync();
        var grab = new Grab { ReleaseTitle = "Demo.Show.S05E16.2160p.WEB-DL-GRP" };
        grab.AddTarget(target);
        db.Grabs.Add(grab);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task DeleteIfPresentAsync(HttpClient client, int tvdbId)
    {
        var list = (await client.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!;
        foreach (var series in list.Where(s => s.TvdbId == tvdbId))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/series/{series.Id}")).StatusCode);
        }
    }

    private static async Task<(int ProfileId, int RootId)> SetupAsync(HttpClient client)
    {
        var profiles = await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json);
        var hd = profiles!.Single(p => p.Name == QualitySeeder.SeriesHdProfileName);
        var roots = (await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!.Where(r => r.Kind == MediaKind.Series).ToList();
        if (roots.Count == 0)
        {
            var dir = Directory.CreateTempSubdirectory("bigarr-series-bulk-").FullName;
            var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir, MediaKind.Series));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            roots = [(await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!];
        }

        return (hd.Id, roots[0].Id);
    }

    private static async Task<SeriesResponse> AddAsync(HttpClient client, AddSeriesRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/series", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesResponse>(Json))!;
    }

    private static async Task<int> AddVersionAsync(HttpClient client, int seriesId, AddSeriesVersionRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/series/{seriesId}/versions", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesVersionResponse>(Json))!.Id;
    }

    private static async Task<SeriesResponse> GetAsync(HttpClient client, int seriesId) =>
        (await client.GetFromJsonAsync<SeriesResponse>($"/api/v1/series/{seriesId}", Json))!;

    private static async Task<int> TagAsync(HttpClient client, string label)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tags", new TagRequest(label), Json);
        return (await response.Content.ReadFromJsonAsync<TagResponse>(Json))!.Id;
    }

    private static async Task<SeriesBulkResponse> UpdateAsync(HttpClient client, SeriesBulkUpdateRequest request)
    {
        var response = await client.PutAsJsonAsync("/api/v1/series/bulk", request, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesBulkResponse>(Json))!;
    }

    private static async Task<SeriesBulkDeleteResponse> DeleteAsync(HttpClient client, SeriesBulkDeleteRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/series/bulk/delete", request, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesBulkDeleteResponse>(Json))!;
    }
}
