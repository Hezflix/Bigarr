using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Import;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Movies;

/// <summary>The library's bulk editor: one version per film chosen by a selector, the single-item rules, the same events.</summary>
public sealed class BulkEditTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private readonly string _movies = Directory.CreateTempSubdirectory("bigarr-bulk-movies-").FullName;
    private readonly string _library = Directory.CreateTempSubdirectory("bigarr-bulk-lib-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-bulk-bin-").FullName;

    public void Dispose()
    {
        TryDelete(_movies);
        TryDelete(_library);
        TryDelete(_bin);
    }

    [Fact]
    public async Task Update_changes_the_selected_version_skips_films_without_it_and_publishes_only_real_changes()
    {
        var client = await ClientAsync(factory);
        var (hd, uhd) = await ProfilesAsync(client);
        var root = await RootAsync(client);
        var matrix = await AddMovieAsync(client, 603, hd, root);
        var interstellar = await AddMovieAsync(client, 157336, hd, root);
        var inception = await AddMovieAsync(client, 27205, hd, root);
        var matrix4k = await AddVersionAsync(client, matrix.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd));
        var interstellar4k = await AddVersionAsync(client, interstellar.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd));
        int[] ids = [matrix.Id, interstellar.Id, inception.Id];
        var before = await EventsAsync("versionUpdated");

        var unmonitored = await UpdateAsync(client, new BulkUpdateRequest([.. ids], "version:4k", Monitored: false));
        Assert.Equal([BulkOutcome.Updated, BulkOutcome.Updated, BulkOutcome.Skipped], unmonitored.Results.Select(r => r.Outcome));
        Assert.Equal(["4k"], unmonitored.Results[0].Versions);
        Assert.Equal("No 4k version", unmonitored.Results[2].Detail);
        Assert.False((await GetMovieAsync(client, matrix.Id)).Versions.Single(v => v.Name == "4k").Monitored);
        Assert.True((await GetMovieAsync(client, matrix.Id)).Versions.Single(v => v.IsDefault).Monitored);
        var published = (await EventsAsync("versionUpdated")).Except(before).ToList();
        Assert.Equal([matrix4k.Id, interstellar4k.Id], published.Order());

        // The same request again changes nothing, so nobody hears about it.
        var again = await UpdateAsync(client, new BulkUpdateRequest([.. ids], "version:4k", Monitored: false));
        Assert.All(again.Results.Take(2), r => Assert.Equal(BulkOutcome.Unchanged, r.Outcome));
        Assert.Equal(published.Count, (await EventsAsync("versionUpdated")).Except(before).Count());

        var defaults = await UpdateAsync(client, new BulkUpdateRequest([.. ids], "default", QualityProfileId: uhd, MinimumAvailability: MinimumAvailability.Announced));
        Assert.All(defaults.Results, r => Assert.Equal(["1080p"], r.Versions));
        var inceptionDefault = (await GetMovieAsync(client, inception.Id)).Versions.Single();
        Assert.Equal(uhd, inceptionDefault.QualityProfileId);
        Assert.Equal(MinimumAvailability.Announced, inceptionDefault.MinimumAvailability);

        // "all" only reports the versions that changed: the default ones were monitored already.
        var all = await UpdateAsync(client, new BulkUpdateRequest([.. ids, 987654], "all", Monitored: true));
        Assert.Equal(["4k"], all.Results[0].Versions);
        Assert.Equal(BulkOutcome.Unchanged, all.Results[2].Outcome);
        Assert.Equal((BulkOutcome.Skipped, "Not in the library"), (all.Results[3].Outcome, all.Results[3].Detail));
    }

    [Fact]
    public async Task Tags_are_added_removed_or_replaced_and_bad_requests_change_nothing()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var movie = await AddMovieAsync(client, 335984, hd, await RootAsync(client));
        var kids = await TagAsync(client, "kids");
        var seerr = await TagAsync(client, "seerr");

        async Task<List<int>?> TagsAfterAsync(List<int> tagIds, TagsMode mode)
        {
            var response = await UpdateAsync(client, new BulkUpdateRequest([movie.Id], TagIds: tagIds, ApplyTags: mode));
            Assert.NotEqual(BulkOutcome.Skipped, Assert.Single(response.Results).Outcome);
            return (await GetMovieAsync(client, movie.Id)).Versions.Single().Tags;
        }

        Assert.Equal([kids], await TagsAfterAsync([kids], TagsMode.Add));
        Assert.Equal(new[] { kids, seerr }.Order(), await TagsAfterAsync([seerr], TagsMode.Add));
        Assert.Equal([seerr], await TagsAfterAsync([kids], TagsMode.Remove));
        Assert.Equal([kids], await TagsAfterAsync([kids], TagsMode.Replace));
        Assert.Empty((await TagsAfterAsync([], TagsMode.Replace))!);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/movies/bulk", new BulkUpdateRequest([movie.Id], "4K", Monitored: true), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/movies/bulk", new BulkUpdateRequest([], Monitored: true), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/movies/bulk", new BulkUpdateRequest([movie.Id], "all"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/movies/bulk", new BulkUpdateRequest([movie.Id], Monitored: false, QualityProfileId: 999), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/movies/bulk", new BulkUpdateRequest([movie.Id], Monitored: false, TagIds: [999]), Json)).StatusCode);
        Assert.True((await GetMovieAsync(client, movie.Id)).Versions.Single().Monitored);
    }

    [Fact]
    public async Task Search_queues_the_selected_versions_and_reports_the_films_it_skipped()
    {
        var client = await ClientAsync(factory);
        var (hd, uhd) = await ProfilesAsync(client);
        var root = await RootAsync(client);
        var bladeRunner = await AddMovieAsync(client, 78, hd, root);
        var parasite = await AddMovieAsync(client, 496243, hd, root);
        await AddVersionAsync(client, bladeRunner.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd));

        var response = await client.PostAsJsonAsync("/api/v1/movies/bulk/search", new BulkSearchRequest([bladeRunner.Id, parasite.Id], "version:4k"), Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var results = (await response.Content.ReadFromJsonAsync<BulkResponse>(Json))!.Results;
        Assert.Equal((BulkOutcome.Queued, "4k"), (results[0].Outcome, results[0].Versions.Single()));
        Assert.Equal(BulkOutcome.Skipped, results[1].Outcome);

        var all = (await (await client.PostAsJsonAsync("/api/v1/movies/bulk/search", new BulkSearchRequest([bladeRunner.Id], "all"), Json)).Content.ReadFromJsonAsync<BulkResponse>(Json))!;
        Assert.Equal(["1080p", "4k"], all.Results.Single().Versions);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/movies/bulk/search", new BulkSearchRequest([bladeRunner.Id], "nope"), Json)).StatusCode);
    }

    [Fact]
    public async Task Delete_dry_run_states_the_plan_then_the_real_run_follows_it()
    {
        var client = await ClientAsync(factory);
        var (hd, uhd) = await ProfilesAsync(client);
        var root = await RootAsync(client);
        var amelie = await AddMovieAsync(client, 194, hd, root);
        var spirited = await AddMovieAsync(client, 129, hd, root);
        var dune = await AddMovieAsync(client, 693134, hd, root);
        var amelie4k = await AddVersionAsync(client, amelie.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd));
        await AddVersionAsync(client, dune.Id, new AddVersionRequest("imax", VersionMode.OneShot, uhd));
        var request = new BulkDeleteRequest([amelie.Id, spirited.Id, dune.Id], "default", AddImportExclusion: true, DryRun: true);

        var plan = await DeleteAsync(client, request);
        Assert.True(plan.DryRun);
        Assert.Equal((BulkOutcome.VersionsDeleted, "4k"), (plan.Results[0].Outcome, plan.Results[0].Promoted));
        Assert.Equal(BulkOutcome.MovieDeleted, plan.Results[1].Outcome);
        Assert.Equal(BulkOutcome.Skipped, plan.Results[2].Outcome);
        Assert.Equal(2, (await GetMovieAsync(client, amelie.Id)).Versions.Count);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/movies/{spirited.Id}")).StatusCode);

        var deletedVersions = await EventsAsync("versionDeleted");
        var deletedMovies = await EventsAsync("movieDeleted", "movie");
        var done = await DeleteAsync(client, request with { DryRun = false });
        Assert.Equal(plan.Results.Select(r => r.Outcome), done.Results.Select(r => r.Outcome));
        var amelieAfter = await GetMovieAsync(client, amelie.Id);
        Assert.Equal(amelie4k.Id, Assert.Single(amelieAfter.Versions).Id);
        Assert.True(amelieAfter.Versions[0].IsDefault);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/movies/{spirited.Id}")).StatusCode);
        Assert.True(Assert.Single((await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=tmdb:129", Json))!).Excluded);
        Assert.Equal(2, (await GetMovieAsync(client, dune.Id)).Versions.Count);
        Assert.Contains(amelie.Versions[0].Id, (await EventsAsync("versionDeleted")).Except(deletedVersions));
        Assert.Equal([spirited.Id], (await EventsAsync("movieDeleted", "movie")).Except(deletedMovies));

        var whole = await DeleteAsync(client, new BulkDeleteRequest([dune.Id], "all"));
        Assert.Equal(["1080p", "imax"], Assert.Single(whole.Results).Versions);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/movies/{dune.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_with_files_sends_them_to_the_recycle_bin_and_the_list_carries_the_file()
    {
        var client = await ClientAsync(factory);
        var (_, uhd) = await ProfilesAsync(client);
        var settings = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/mediamanagement", settings.ToRequest() with { RecycleBinPath = _bin }, Json)).StatusCode);
        var video = Path.Combine(_library, "The Matrix Reloaded (2003)", "The.Matrix.Reloaded.2003.1080p.BluRay.x264-GRP.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
        await File.WriteAllBytesAsync(video, new byte[64 * 1024]);
        var root = await AddRootAsync(client, _library);
        await ScanAsync(client, root.Id);

        var summary = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 604);
        var scanned = Assert.Single(summary.Versions);
        Assert.Equal((VersionStatus.Available, "Bluray-1080p", 64 * 1024L, true), (scanned.Status, scanned.Quality, scanned.Size, scanned.CutoffMet));
        Assert.Equal(root.Id, scanned.RootFolderId);
        Assert.Equal(7.0, summary.TmdbRating);
        await AddVersionAsync(client, summary.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd));

        var request = new BulkDeleteRequest([summary.Id], "version:1080p", DeleteFiles: true, DryRun: true);
        var plan = await DeleteAsync(client, request);
        Assert.Equal(_bin, plan.RecycleBin);
        Assert.Equal((1, "4k"), (plan.Results.Single().Files, plan.Results.Single().Promoted));
        Assert.True(File.Exists(video));

        var done = await DeleteAsync(client, request with { DryRun = false });
        Assert.Equal(BulkOutcome.VersionsDeleted, done.Results.Single().Outcome);
        Assert.False(File.Exists(video));
        Assert.Single(Directory.GetFiles(_bin, "*.mkv", SearchOption.AllDirectories));
        Assert.Equal("4k", (await GetMovieAsync(client, summary.Id)).Versions.Single(v => v.IsDefault).Name);
    }

    [Fact]
    public async Task A_one_shot_version_gets_no_cutoff_verdict_since_it_is_never_upgraded()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var video = Path.Combine(_library, "Mission Impossible (1996)", "Mission.Impossible.1996.720p.BluRay.x264-GRP.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
        await File.WriteAllBytesAsync(video, new byte[64 * 1024]);
        var root = await AddRootAsync(client, _library);
        await ScanAsync(client, root.Id);

        var movie = (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.TmdbId == 954);
        var version = Assert.Single(movie.Versions);
        async Task<(bool? List, bool? Page)> VerdictsAsync() => (
            (await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json))!.Single(m => m.Id == movie.Id).Versions.Single(v => v.Id == version.Id).CutoffMet,
            (await GetMovieAsync(client, movie.Id)).Versions.Single(v => v.Id == version.Id).CutoffMet);

        Assert.Equal("Bluray-720p", version.Quality);
        Assert.Equal((false, false), await VerdictsAsync());

        // The default version must stay managed: hand the default over before turning this one into a one-shot.
        var spare = await AddVersionAsync(client, movie.Id, new AddVersionRequest("spare", VersionMode.Managed, hd));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/movies/{movie.Id}/versions/{spare.Id}/promote", null)).StatusCode);
        var update = await client.PutAsJsonAsync($"/api/v1/movies/{movie.Id}/versions/{version.Id}", new UpdateVersionRequest(Mode: VersionMode.OneShot), Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal((null, null), await VerdictsAsync());
    }

    private async Task<int> RootAsync(HttpClient client)
    {
        var roots = (await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!;
        return roots.FirstOrDefault(r => r.Path == _movies)?.Id ?? (await AddRootAsync(client, _movies)).Id;
    }

    private static async Task<int> TagAsync(HttpClient client, string label)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tags", new TagRequest(label), Json);
        return (await response.Content.ReadFromJsonAsync<TagResponse>(Json))!.Id;
    }

    private static async Task<BulkResponse> UpdateAsync(HttpClient client, BulkUpdateRequest request)
    {
        var response = await client.PutAsJsonAsync("/api/v1/movies/bulk", request, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BulkResponse>(Json))!;
    }

    private static async Task<BulkDeleteResponse> DeleteAsync(HttpClient client, BulkDeleteRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies/bulk/delete", request, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BulkDeleteResponse>(Json))!;
    }

    private static async Task ScanAsync(HttpClient client, int rootId)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/rootfolders/{rootId}/scan", null)).StatusCode);
        await WaitUntilAsync(
            async () =>
            {
                var status = (await client.GetFromJsonAsync<ScanStatusResponse>($"/api/v1/rootfolders/{rootId}/scan", Json))!;
                Assert.NotEqual(ScanState.Failed, status.State);
                return status.State == ScanState.Completed ? status : null;
            },
            "library scan");
    }

    /// <summary>Ids of the version (or movie) every outbox event of that type is about.</summary>
    private async Task<List<int>> EventsAsync(string type, string subject = "version")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var payloads = await db.Outbox.Where(e => e.Type == type).Select(e => e.Payload).ToListAsync();
        return payloads.Select(p => JsonDocument.Parse(p).RootElement.GetProperty(subject).GetProperty("id").GetInt32()).ToList();
    }
}
