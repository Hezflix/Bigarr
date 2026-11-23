using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bigarr.Api.Features.Discover;
using Bigarr.Api.Features.Requests;
using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Requests;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Requests.RequestsEnv;

namespace Bigarr.Api.Tests.Requests;

/// <summary>
/// Requests from ask to available: pending, approved with edits into the library through its own add path, declined,
/// cancelled; states after approval read from the library; duplicates, permissions, quotas and auto-approve.
/// </summary>
public sealed class RequestLifecycleTests
{
    private static readonly UserPermissions Defaults = UserPermissions.Defaults();

    [Fact]
    public async Task Two_seasons_requested_approved_with_another_profile_then_follow_the_episodes_files()
    {
        await using var env = await StartAsync();
        var user = await env.UserAsync("demo-friend", Defaults);
        var manager = await env.UserAsync("demo-manager", Defaults, UserRole.RequestManager);

        var asked = await AskOkAsync(user, RequestMediaType.Series, 1396, seasons: [1, 2]);
        Assert.Equal(RequestState.Pending, asked.State);
        Assert.Equal([1, 2], asked.Seasons);
        Assert.True(asked.CanCancel);

        // Pending shows on Discover, per season; asking again for those seasons is refused, the rest is still open.
        var page = (await user.GetFromJsonAsync<DiscoverSeriesResponse>("/api/v1/discover/series/1396", Json))!;
        Assert.Equal(RequestState.Pending, page.Requests!.Standard);
        Assert.Equal(RequestState.Pending, page.Seasons.Single(s => s.SeasonNumber == 1).Requests!.Standard);
        Assert.Null(page.Seasons.Single(s => s.SeasonNumber == 3).Requests);
        Assert.Equal(HttpStatusCode.Conflict, (await AskAsync(user, RequestMediaType.Series, 1396, seasons: [2])).StatusCode);

        // The manager sees it, approves it with the 4K series profile instead of the default.
        Assert.Equal(1, (await manager.GetFromJsonAsync<RequestCountsResponse>("/api/v1/requests/count", Json))!.Pending);
        var options = (await manager.GetFromJsonAsync<RequestOptionsResponse>("/api/v1/requests/options?mediaType=series", Json))!;
        var series4K = options.Profiles.Single(p => p.Name == "Series 4K");
        Assert.All(options.RootFolders, r => Assert.DoesNotContain("/", r.Name, StringComparison.Ordinal));
        var approved = await env.ApproveAsync(manager, asked.Id, new ApproveRequestRequest(QualityProfileId: series4K.Id));
        Assert.Equal(RequestStatus.Approved, approved.Status);
        Assert.Equal(RequestState.Processing, approved.State);
        Assert.Equal("Series 4K", approved.QualityProfile);
        Assert.Equal("demo-manager", approved.DecidedBy!.Username);

        // In the library: the series, its version on that profile, seasons 1 and 2 wanted, the others not, no new seasons.
        var (versionId, profileId, monitoredSeasons, newItems) = await env.DbAsync(async db =>
        {
            var version = await db.SeriesVersions.Include(v => v.Seasons).ThenInclude(s => s.Season).SingleAsync(v => v.Series.TmdbId == 1396);
            return (version.Id, version.QualityProfileId, version.Seasons.Where(s => s.Monitored).Select(s => s.Season.SeasonNumber).Order().ToList(), version.MonitorNewItems);
        });
        Assert.Equal(series4K.Id, profileId);
        Assert.Equal([1, 2], monitoredSeasons);
        Assert.Equal(NewItemMonitoring.None, newItems);

        // Files for season 1 only: partially available; season 2 too: available, and "available" goes out once.
        await env.GiveFilesAsync(versionId, 1);
        Assert.Equal(RequestState.PartiallyAvailable, (await GetAsync(user, asked.Id)).State);
        await env.GiveFilesAsync(versionId, 2);
        Assert.Equal(RequestState.Available, (await GetAsync(user, asked.Id)).State);
        await env.RunJobAsync(RequestAvailabilityJob.JobName);
        await env.RunJobAsync(RequestAvailabilityJob.JobName);
        var events = await env.OutboxTypesAsync();
        Assert.Equal(1, events.Count(e => e == "requestCreated"));
        Assert.Equal(1, events.Count(e => e == "requestApproved"));
        Assert.Equal(1, events.Count(e => e == "requestAvailable"));

        // Every season left: 1 and 2 are in the library; 3, and 4 and 5 that only the library lists, are still open.
        var rest = await AskOkAsync(user, RequestMediaType.Series, 1396);
        Assert.Equal([3, 4, 5], rest.Seasons);
    }

    [Fact]
    public async Task A_film_request_is_declined_or_cancelled_and_duplicates_are_refused()
    {
        await using var env = await StartAsync();
        var user = await env.UserAsync("demo-friend", Defaults);
        var other = await env.UserAsync("demo-other", Defaults);

        var mine = await AskOkAsync(user, RequestMediaType.Movie, 603);
        Assert.Equal(HttpStatusCode.Conflict, (await AskAsync(other, RequestMediaType.Movie, 603)).StatusCode);

        // Someone else's request is not theirs to see, cancel or decide on.
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/requests/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/requests/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/v1/requests/{mine.Id}/approve", new ApproveRequestRequest(), Json)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<MediaRequestPage>("/api/v1/requests", Json))!.Records);

        // Declined with a reason: final, and the slot opens again.
        var declined = await env.Admin.PostAsJsonAsync($"/api/v1/requests/{mine.Id}/decline", new DeclineRequestRequest("Not this one"), Json);
        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);
        var seen = await GetAsync(user, mine.Id);
        Assert.Equal(RequestState.Declined, seen.State);
        Assert.Equal("Not this one", seen.DeclineReason);
        Assert.False(seen.CanCancel);
        Assert.Equal(HttpStatusCode.Conflict, (await env.Admin.PostAsJsonAsync($"/api/v1/requests/{mine.Id}/approve", new ApproveRequestRequest(), Json)).StatusCode);
        Assert.Contains("requestDeclined", await env.OutboxTypesAsync());

        // The other user asks now and takes it back while pending.
        var theirs = await AskOkAsync(other, RequestMediaType.Movie, 603);
        Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync($"/api/v1/requests/{theirs.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/requests/{theirs.Id}")).StatusCode);

        // Approved, it cannot be cancelled; and a film already in the library cannot be asked for.
        var again = await AskOkAsync(other, RequestMediaType.Movie, 603);
        await env.ApproveAsync(env.Admin, again.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await other.DeleteAsync($"/api/v1/requests/{again.Id}")).StatusCode);
        var inLibrary = await AskAsync(user, RequestMediaType.Movie, 603);
        Assert.Equal(HttpStatusCode.Conflict, inLibrary.StatusCode);
    }

    [Fact]
    public async Task Approval_adds_the_film_through_the_library_add_and_its_state_follows_the_file()
    {
        await using var env = await StartAsync();
        var user = await env.UserAsync("demo-friend", Defaults);
        var asked = await AskOkAsync(user, RequestMediaType.Movie, 603);
        var approved = await env.ApproveAsync(env.Admin, asked.Id);
        Assert.Equal(RequestState.Processing, approved.State);
        Assert.NotNull(approved.LibraryId); // the admin sees the library id

        var version = await env.DbAsync(db => db.MovieVersions.Include(v => v.QualityProfile).SingleAsync(v => v.Movie.TmdbId == 603));
        Assert.True(version.IsDefault);
        Assert.True(version.Monitored);
        Assert.Equal("1080p", version.Name);
        Assert.Contains("movieAdded", await env.OutboxTypesAsync());

        await env.DbAsync(async db =>
        {
            db.MediaFiles.Add(TestData.NewFile(await db.MovieVersions.SingleAsync(v => v.Id == version.Id), "The.Matrix.1999.1080p.mkv"));
            return await db.SaveChangesAsync();
        });
        Assert.Equal(RequestState.Available, (await GetAsync(user, asked.Id)).State);
        var list = (await env.Admin.GetFromJsonAsync<MediaRequestPage>("/api/v1/requests?filter=available", Json))!;
        Assert.Equal(asked.Id, Assert.Single(list.Records).Id);
        Assert.Empty((await env.Admin.GetFromJsonAsync<MediaRequestPage>("/api/v1/requests?filter=processing", Json))!.Records);
    }

    [Fact]
    public async Task Permissions_decide_what_a_user_may_ask_for_and_4K_lands_in_the_4K_version()
    {
        await using var env = await StartAsync();
        var filmsOnly = await env.UserAsync("demo-films", new UserPermissions { RequestFilms = true });
        Assert.Equal(HttpStatusCode.Forbidden, (await AskAsync(filmsOnly, RequestMediaType.Series, 1396)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AskAsync(filmsOnly, RequestMediaType.Movie, 603, fourK: true)).StatusCode);

        var fourK = await env.UserAsync("demo-4k", new UserPermissions { RequestFilms = true, Request4K = true });
        var asked = await AskOkAsync(fourK, RequestMediaType.Movie, 603, fourK: true);
        Assert.True(asked.FourK);
        await env.ApproveAsync(env.Admin, asked.Id);

        // A 4K-only film: its one version is the 4K one, and the standard slot stays open.
        var version = await env.DbAsync(db => db.MovieVersions.Include(v => v.QualityProfile).SingleAsync(v => v.Movie.TmdbId == 603));
        Assert.Equal("4k", version.Name);
        Assert.Equal("4K", version.QualityProfile.Name);
        var page = (await fourK.GetFromJsonAsync<DiscoverMovieResponse>("/api/v1/discover/movies/603", Json))!;
        Assert.Equal(AvailabilityState.NotInLibrary, page.Availability.Standard);
        Assert.Equal(AvailabilityState.Processing, page.Availability.FourK);
        Assert.Equal(RequestState.Approved, page.Requests!.FourK);

        // The standard one, asked next, becomes a second version.
        var standard = await AskOkAsync(fourK, RequestMediaType.Movie, 603);
        await env.ApproveAsync(env.Admin, standard.Id);
        Assert.Equal(["1080p", "4k"], await env.DbAsync(db => db.MovieVersions.Where(v => v.Movie.TmdbId == 603).OrderBy(v => v.Name).Select(v => v.Name).ToListAsync()));
    }

    [Fact]
    public async Task Auto_approve_flags_approve_at_once_and_admins_too()
    {
        await using var env = await StartAsync();
        var trusted = await env.UserAsync("demo-trusted", new UserPermissions { RequestFilms = true, RequestSeries = true, AutoApproveFilms = true });
        var film = await AskOkAsync(trusted, RequestMediaType.Movie, 603);
        Assert.Equal(RequestStatus.Approved, film.Status);
        Assert.True(film.AutoApproved);
        Assert.True(await env.DbAsync(db => db.Movies.AnyAsync(m => m.TmdbId == 603)));

        // The film flag says nothing about series.
        var show = await AskOkAsync(trusted, RequestMediaType.Series, 1396, seasons: [1]);
        Assert.Equal(RequestStatus.Pending, show.Status);

        var admin = await AskOkAsync(env.Admin, RequestMediaType.Movie, 604);
        Assert.Equal(RequestStatus.Approved, admin.Status);
        var events = await env.OutboxTypesAsync();
        Assert.Equal(2, events.Count(e => e == "requestApproved"));
    }

    [Fact]
    public async Task Quotas_limit_films_and_seasons_over_their_window_and_a_declined_request_does_not_count()
    {
        await using var env = await StartAsync();
        var limited = await env.UserAsync("demo-limited", Defaults, quota: new RequestQuota { FilmLimit = 1, FilmDays = 7, SeasonLimit = 2, SeasonDays = 7 });
        var first = await AskOkAsync(limited, RequestMediaType.Movie, 603);
        var refused = await AskAsync(limited, RequestMediaType.Movie, 604);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("limit", await refused.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var quota = (await limited.GetFromJsonAsync<QuotaResponse>("/api/v1/requests/quota", Json))!;
        Assert.Equal(new QuotaUsage(1, 7, 1, 0), quota.Films);
        Assert.False(quota.Exempt);

        await env.Admin.PostAsJsonAsync($"/api/v1/requests/{first.Id}/decline", new DeclineRequestRequest(), Json);
        await AskOkAsync(limited, RequestMediaType.Movie, 604);

        // Three seasons over a limit of two: refused whole; two fit.
        Assert.Equal(HttpStatusCode.Forbidden, (await AskAsync(limited, RequestMediaType.Series, 1396, seasons: [1, 2, 3])).StatusCode);
        await AskOkAsync(limited, RequestMediaType.Series, 1396, seasons: [1, 2]);

        // The admin sets the limits per user and the defaults new users get; the admin is never limited.
        var users = (await env.Admin.GetFromJsonAsync<List<Bigarr.Api.Features.Users.UserResponse>>("/api/v1/users", Json))!;
        Assert.Equal(new QuotaDto(1, 7, 2, 7), users.Single(u => u.Username == "demo-limited").Quota);
        var defaults = await env.Admin.PutAsJsonAsync("/api/v1/users/settings", new Bigarr.Api.Features.Users.UpdateUserSettingsRequest(true, Bigarr.Api.Features.Users.PermissionsDto.From(Defaults), new QuotaDto(5, 30, null, 7)), Json);
        Assert.Equal(new QuotaDto(5, 30, null, 7), (await defaults.Content.ReadFromJsonAsync<Bigarr.Api.Features.Users.UserSettingsResponse>(Json))!.DefaultQuota);
        Assert.True((await env.Admin.GetFromJsonAsync<QuotaResponse>("/api/v1/requests/quota", Json))!.Exempt);
    }

    [Fact]
    public async Task A_failed_add_keeps_the_request_and_a_retry_takes_it()
    {
        await using var env = await StartAsync(seriesRoot: false);
        var user = await env.UserAsync("demo-friend", Defaults);
        var asked = await AskOkAsync(user, RequestMediaType.Series, 1396, seasons: [1]);
        var approved = await env.ApproveAsync(env.Admin, asked.Id);
        Assert.Equal(RequestState.Failed, approved.State);
        Assert.Contains("root folder", approved.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requestFailed", await env.OutboxTypesAsync());

        // What the user sees: failed, not why.
        var seen = await GetAsync(user, asked.Id);
        Assert.Equal(RequestState.Failed, seen.State);
        Assert.Null(seen.Error);

        Assert.Equal(1, (await env.Admin.GetFromJsonAsync<RequestCountsResponse>("/api/v1/requests/count", Json))!.Failed);
        await env.AddRootAsync(Bigarr.Domain.Library.MediaKind.Series);
        var retried = await env.Admin.PostAsync($"/api/v1/requests/{asked.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var after = (await retried.Content.ReadFromJsonAsync<MediaRequestResponse>(Json))!;
        Assert.Equal(RequestStatus.Approved, after.Status);
        Assert.Null(after.Error);
    }

    [Fact]
    public async Task An_add_that_times_out_fails_the_request_and_a_retry_takes_it()
    {
        var provider = new FlakyMetadata();
        await using var env = await StartAsync(testServices: services => services.AddSingleton<IMetadataProvider>(provider));
        var user = await env.UserAsync("demo-friend", Defaults);
        var asked = await AskOkAsync(user, RequestMediaType.Movie, 604);

        // The metadata provider times out (a TaskCanceledException, not an HttpRequestException).
        var approved = await env.ApproveAsync(env.Admin, asked.Id);
        Assert.Equal(RequestState.Failed, approved.State);
        Assert.False(string.IsNullOrEmpty(approved.Error));
        var outbox = await env.OutboxTypesAsync();
        Assert.Contains("requestFailed", outbox);
        Assert.Equal(1, outbox.Count(t => t == "requestApproved"));

        var retried = await env.Admin.PostAsync($"/api/v1/requests/{asked.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var after = (await retried.Content.ReadFromJsonAsync<MediaRequestResponse>(Json))!;
        Assert.Equal(RequestStatus.Approved, after.Status);
        Assert.Null(after.Error);
        Assert.Equal(1, (await env.OutboxTypesAsync()).Count(t => t == "requestApproved"));
        Assert.True(await env.DbAsync(db => db.Movies.AnyAsync(m => m.TmdbId == 604)));
    }

    [Fact]
    public async Task An_approval_whose_caller_goes_away_still_ends_failed_or_approved()
    {
        var provider = new FlakyMetadata();
        await using var env = await StartAsync(testServices: services => services.AddSingleton<IMetadataProvider>(provider));
        var user = await env.UserAsync("demo-friend", Defaults);
        var asked = await AskOkAsync(user, RequestMediaType.Movie, 604);

        // The caller leaves while the library add waits for the metadata provider.
        using var gone = new CancellationTokenSource();
        provider.CancelCaller = gone;
        var adminId = await env.DbAsync(db => db.Users.Where(u => u.Username == BigarrApiFactory.AdminUsername).Select(u => u.Id).SingleAsync());
        var admin = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, adminId.ToString(System.Globalization.CultureInfo.InvariantCulture)), new Claim(ClaimTypes.Name, BigarrApiFactory.AdminUsername), new Claim(ClaimTypes.Role, nameof(UserRole.Admin))],
            "test"));
        await using (var scope = env.App.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RequestService>().ApproveAsync(admin, asked.Id, new ApproveRequestRequest(), gone.Token);
            Assert.True(result.IsSuccess);
        }

        Assert.True(gone.IsCancellationRequested, "the caller never left");
        var stored = await env.DbAsync(db => db.MediaRequests.AsNoTracking().SingleAsync(r => r.Id == asked.Id));
        Assert.Equal(RequestStatus.Failed, stored.Status);
        Assert.False(string.IsNullOrEmpty(stored.Error));
        Assert.Contains("requestFailed", await env.OutboxTypesAsync());

        Assert.Equal(HttpStatusCode.OK, (await env.Admin.PostAsync($"/api/v1/requests/{asked.Id}/retry", null)).StatusCode);
        Assert.True(await env.DbAsync(db => db.Movies.AnyAsync(m => m.TmdbId == 604)));
    }

    [Fact]
    public async Task A_stop_between_the_approval_and_the_add_is_repaired_at_startup()
    {
        await using var env = await StartAsync();
        var user = await env.UserAsync("demo-friend", Defaults);
        var stranded = await AskOkAsync(user, RequestMediaType.Movie, 604);
        var inLibrary = await AskOkAsync(user, RequestMediaType.Movie, 603);
        await env.ApproveAsync(env.Admin, inLibrary.Id);
        var imported = await AskOkAsync(user, RequestMediaType.Series, 1396, seasons: [1]);
        var pending = await AskOkAsync(user, RequestMediaType.Series, 1396, seasons: [2]);

        // The process stopped after the approval was saved: approved, nothing in the library, nothing imported.
        await env.DbAsync(async db =>
        {
            foreach (var request in await db.MediaRequests.Where(r => r.Id == stranded.Id || r.Id == imported.Id).ToListAsync())
            {
                request.Status = RequestStatus.Approved;
                request.AutoApproved = true;
            }

            (await db.MediaRequests.SingleAsync(r => r.Id == imported.Id)).SourceRef = "seerr:request:7";
            await db.SaveChangesAsync();
            return 0;
        });

        await using (var scope = env.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetServices<IStartupTask>().OfType<StrandedApprovals>().Single().RunAsync(CancellationToken.None);
        }

        var rows = await env.DbAsync(db => db.MediaRequests.AsNoTracking().ToDictionaryAsync(r => r.Id));
        Assert.Equal(RequestStatus.Failed, rows[stranded.Id].Status);
        Assert.False(string.IsNullOrEmpty(rows[stranded.Id].Error));
        Assert.Equal(RequestStatus.Approved, rows[inLibrary.Id].Status);
        Assert.Equal(RequestStatus.Approved, rows[imported.Id].Status);
        Assert.Equal(RequestStatus.Pending, rows[pending.Id].Status);
        Assert.Contains("requestFailed", await env.OutboxTypesAsync());

        // Failed, so the Retry button takes it.
        Assert.Equal(HttpStatusCode.OK, (await env.Admin.PostAsync($"/api/v1/requests/{stranded.Id}/retry", null)).StatusCode);
        Assert.True(await env.DbAsync(db => db.Movies.AnyAsync(m => m.TmdbId == 604)));
    }

    /// <summary>The fixture metadata, except that the first film lookup times out (as the TMDB client does), or cancels the caller then throws.</summary>
    private sealed class FlakyMetadata : IMetadataProvider
    {
        private readonly FixtureMetadataProvider _inner = new();
        private int _lookups;

        public CancellationTokenSource? CancelCaller { get; set; }

        public string Name => _inner.Name;

        public async Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _lookups) == 1)
            {
                if (CancelCaller is { } caller)
                {
                    await caller.CancelAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
            }

            return await _inner.GetMovieAsync(tmdbId, cancellationToken);
        }

        public Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken) => _inner.GetMovieByImdbIdAsync(imdbId, cancellationToken);

        public Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken) => _inner.SearchAsync(query, year, cancellationToken);
    }

    [Fact]
    public async Task What_a_user_receives_holds_no_profile_root_folder_path_or_followed_app()
    {
        await using var env = await StartAsync();
        var user = await env.UserAsync("demo-friend", Defaults);
        var manager = await env.UserAsync("demo-manager", Defaults, UserRole.RequestManager);
        var asked = await AskOkAsync(user, RequestMediaType.Movie, 603);
        await env.ApproveAsync(env.Admin, asked.Id);
        var roots = await env.DbAsync(db => db.RootFolders.Select(r => r.Path).ToListAsync());

        var mine = await user.GetStringAsync("/api/v1/requests");
        foreach (var path in roots)
        {
            Assert.DoesNotContain(path, mine, StringComparison.Ordinal);
        }

        var record = (await user.GetFromJsonAsync<MediaRequestPage>("/api/v1/requests", Json))!.Records.Single();
        Assert.Null(record.QualityProfile);
        Assert.Null(record.RootFolder);
        Assert.Null(record.ForwardStatus);
        Assert.Null(record.LibraryId);
        Assert.Null(record.RequestedBy);

        // A request manager: names, not paths; no library id, no forward error.
        var managed = await manager.GetStringAsync("/api/v1/requests");
        foreach (var path in roots)
        {
            Assert.DoesNotContain(path, managed, StringComparison.Ordinal);
        }

        var seen = (await manager.GetFromJsonAsync<MediaRequestPage>("/api/v1/requests", Json))!.Records.Single();
        Assert.Equal("1080p", seen.QualityProfile);
        Assert.NotNull(seen.RootFolder);
        Assert.Null(seen.LibraryId);
        Assert.Equal("demo-friend", seen.RequestedBy!.Username);

        // Users' own pages stay closed to them.
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/requests/options?mediaType=movie")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/v1/requests/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/v1/qualityprofiles")).StatusCode);
    }

    [Fact]
    public async Task In_shadow_mode_the_search_on_approval_only_records_a_pick()
    {
        await using var env = await StartAsync(shadow: true);
        var indexer = await env.Admin.PostAsJsonAsync("/api/v1/indexers", new Bigarr.Api.Features.Indexers.IndexerRequest("Mock requests", Bigarr.Domain.Indexers.IndexerImplementation.Mock, MinimumSeeders: 1, RateLimitSeconds: 0, SeedRatio: 1.0), Json);
        Assert.Equal(HttpStatusCode.Created, indexer.StatusCode);
        var user = await env.UserAsync("demo-friend", Defaults);
        var asked = await AskOkAsync(user, RequestMediaType.Movie, 603);
        await env.ApproveAsync(env.Admin, asked.Id);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await env.DbAsync(db => db.ShadowPicks.AnyAsync(p => p.TmdbId == 603)))
        {
            Assert.True(DateTime.UtcNow < deadline, "no pick recorded");
            await Task.Delay(100);
        }

        Assert.False(await env.DbAsync(db => db.Grabs.AnyAsync()));
        Assert.Equal(RequestState.Processing, (await GetAsync(user, asked.Id)).State);
    }
}
