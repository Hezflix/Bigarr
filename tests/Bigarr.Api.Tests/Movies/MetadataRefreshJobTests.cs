using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Events;
using Bigarr.Domain.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Movies;

/// <summary>
/// The scheduled metadata refresh (Radarr's "Refresh Movie" task) on the real app, with the fixture catalogue behind a
/// provider the test can break: due films are asked for, never refreshed first; recent ones are left alone; a dead
/// provider stops the run and raises a health issue until a run goes through.
/// </summary>
public sealed class MetadataRefreshJobTests
{
    private const int Matrix = 603;
    private const int Amelie = 194;
    private const int BladeRunner = 78;
    private const int SpiritedAway = 129;
    private const int MissionImpossible = 954;
    private const int Inception = 27205;
    private const int Interstellar = 157336;
    private const int Unreleased = 900000001;

    [Fact]
    public async Task Due_films_are_refreshed_never_refreshed_first_and_recent_ones_are_left_alone()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var provider = new ScriptedMetadataProvider();
        // TMDB renamed The Matrix since Radarr's snapshot; the unreleased film stays two months away whenever this runs.
        provider.Edit = m => m.TmdbId switch
        {
            Matrix => m with { Title = "The Matrix: Director's Cut" },
            Unreleased => m with { InCinemasDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(60) },
            _ => m,
        };
        using var factory = new RefreshFactory(provider, clock);
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(factory.ConfigDirectory, "library"));
        var added = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(Matrix, hd, root.Id, Monitored: false));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var matrix = (await added.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
        var ids = new Dictionary<int, int> { [Matrix] = matrix.Id };
        foreach (var tmdbId in new[] { Amelie, BladeRunner, Unreleased, Inception, Interstellar })
        {
            ids[tmdbId] = (await AddMovieAsync(client, tmdbId, hd, root.Id)).Id;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await SetLastRefreshAsync(factory, ids[Matrix], null, title: "Matrix, The"); // as a Radarr import leaves it
        await SetLastRefreshAsync(factory, ids[Amelie], null);
        await SetLastRefreshAsync(factory, ids[BladeRunner], now.AddDays(-200)); // stale
        await SetLastRefreshAsync(factory, ids[Unreleased], now.AddHours(-13)); // announced
        await SetLastRefreshAsync(factory, ids[Inception], now.AddHours(-1)); // recent
        await SetLastRefreshAsync(factory, ids[Interstellar], now.AddDays(-30)); // came out long ago
        provider.Calls.Clear();

        var stopwatch = Stopwatch.StartNew();
        var run = await RunAsync(client);
        stopwatch.Stop();

        Assert.True(run.Success, run.Error);
        Assert.Equal("4 of 6 film(s) due, 4 refreshed, 0 failed", run.Summary);
        Assert.Equal([Matrix, Amelie, BladeRunner, Unreleased], provider.Calls);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(700), $"4 films per second: three gaps of 250 ms, took {stopwatch.Elapsed}");

        // Metadata only: the version keeps its folder and its monitoring, whatever the new title says.
        var refreshed = await GetMovieAsync(client, ids[Matrix]);
        Assert.Equal("The Matrix: Director's Cut", refreshed.Title);
        Assert.Equal(now, refreshed.LastMetadataRefreshAt);
        var version = Assert.Single(refreshed.Versions);
        Assert.Equal(matrix.Versions.Single().FolderName, version.FolderName);
        Assert.False(version.Monitored);
        Assert.Equal(now.AddHours(-1), (await GetMovieAsync(client, ids[Inception])).LastMetadataRefreshAt);

        // What was refreshed is not due again: a run cut short resumes where it stopped.
        provider.Calls.Clear();
        run = await RunAsync(client);
        Assert.Equal("Nothing due among 6 film(s)", run.Summary);
        Assert.Empty(provider.Calls);

        // Thirteen hours later, only the film whose dates may still move is asked again.
        clock.Advance(TimeSpan.FromHours(13));
        run = await RunAsync(client);
        Assert.Equal("1 of 6 film(s) due, 1 refreshed, 0 failed", run.Summary);
        Assert.Equal([Unreleased], provider.Calls);
    }

    [Fact]
    public async Task A_dead_provider_stops_the_run_and_raises_a_health_issue_until_a_run_goes_through()
    {
        var provider = new ScriptedMetadataProvider();
        using var factory = new RefreshFactory(provider, new MutableTimeProvider(DateTimeOffset.UtcNow), Fast);
        var client = await ClientAsync(factory);
        var ids = await AddNeverRefreshedAsync(factory, client, Matrix, Amelie, BladeRunner, SpiritedAway, MissionImpossible);
        provider.Calls.Clear();

        provider.Fails = _ => true;
        var run = await RunAsync(client);

        Assert.False(run.Success);
        Assert.Equal("Stopped after 3 metadata provider failures in a row (Response status code does not indicate success: 503 (Service Unavailable)); 0 of 5 due film(s) were refreshed, the others wait for the next run.", run.Error);
        Assert.Equal([Matrix, Amelie, BladeRunner], provider.Calls); // the other two are not asked
        var issue = Assert.Single((await CheckHealthAsync(client)).Issues, i => i.Source == "metadataRefresh");
        Assert.Equal(HealthIssueType.Warning, issue.Type);
        Assert.Equal($"The metadata refresh did not finish: {run.Error}", issue.Message);
        foreach (var id in ids)
        {
            Assert.Null((await GetMovieAsync(client, id)).LastMetadataRefreshAt);
        }

        provider.Fails = _ => false;
        run = await RunAsync(client);
        Assert.True(run.Success, run.Error);
        Assert.Equal("5 of 5 film(s) due, 5 refreshed, 0 failed", run.Summary);
        Assert.DoesNotContain((await CheckHealthAsync(client)).Issues, i => i.Source == "metadataRefresh");
    }

    [Fact]
    public async Task A_film_that_fails_is_logged_and_stays_due_and_only_failures_in_a_row_stop_the_run()
    {
        var provider = new ScriptedMetadataProvider();
        using var factory = new RefreshFactory(provider, new MutableTimeProvider(DateTimeOffset.UtcNow), Fast);
        var client = await ClientAsync(factory);
        await AddNeverRefreshedAsync(factory, client, Matrix, Amelie, BladeRunner, SpiritedAway, MissionImpossible);

        // Two failures, a success, two failures: never three in a row.
        provider.Fails = tmdbId => tmdbId != BladeRunner;
        var run = await RunAsync(client);
        Assert.True(run.Success, run.Error);
        Assert.Equal("5 of 5 film(s) due, 1 refreshed, 4 failed", run.Summary);
        Assert.DoesNotContain((await CheckHealthAsync(client)).Issues, i => i.Source == "metadataRefresh");

        provider.Fails = _ => false;
        provider.Calls.Clear();
        run = await RunAsync(client);
        Assert.Equal("4 of 5 film(s) due, 4 refreshed, 0 failed", run.Summary);
        Assert.Equal([Matrix, Amelie, SpiritedAway, MissionImpossible], provider.Calls);
    }

    [Fact]
    public async Task On_the_fixture_catalogue_the_refresh_leaves_every_film_alone()
    {
        using var factory = new BigarrApiFactory();
        var client = await ClientAsync(factory);
        var ids = await AddNeverRefreshedAsync(factory, client, Matrix);

        var run = await RunAsync(client);

        Assert.True(run.Success, run.Error);
        Assert.Equal("No TMDB API key: films are not refreshed against the fixture catalogue", run.Summary);
        Assert.Null((await GetMovieAsync(client, ids[0])).LastMetadataRefreshAt);
    }

    /// <summary>No pacing, and a short streak, so the failure tests stay quick.</summary>
    private static readonly Dictionary<string, string> Fast = new()
    {
        ["Bigarr:Metadata:RefreshFilmsPerSecond"] = "0",
        ["Bigarr:Metadata:RefreshMaxConsecutiveFailures"] = "3",
    };

    private static async Task<TaskRunResponse> RunAsync(HttpClient client)
    {
        var response = await client.PostAsync($"/api/v1/system/tasks/{MetadataRefreshJob.JobName}/run", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
    }

    private static async Task<HealthResponse> CheckHealthAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/health/check", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HealthResponse>(Json))!;
    }

    /// <summary>Adds the films in this order, then forgets their refresh, as a Radarr import leaves them.</summary>
    private static async Task<List<int>> AddNeverRefreshedAsync(BigarrApiFactory factory, HttpClient client, params int[] tmdbIds)
    {
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(factory.ConfigDirectory, "library"));
        var ids = new List<int>();
        foreach (var tmdbId in tmdbIds)
        {
            var movie = await AddMovieAsync(client, tmdbId, hd, root.Id);
            await SetLastRefreshAsync(factory, movie.Id, null);
            ids.Add(movie.Id);
        }

        return ids;
    }

    private static async Task SetLastRefreshAsync(BigarrApiFactory factory, int movieId, DateTime? at, string? title = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var movie = await db.Movies.SingleAsync(m => m.Id == movieId);
        movie.LastMetadataRefreshAt = at;
        movie.Title = title ?? movie.Title;
        await db.SaveChangesAsync();
    }

    /// <summary>The real app with the test's clock and metadata provider.</summary>
    private sealed class RefreshFactory(IMetadataProvider provider, TimeProvider time, IReadOnlyDictionary<string, string>? settings = null)
        : BigarrApiFactory(null, settings, deleteOnDispose: true, time)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton(provider));
        }
    }

    /// <summary>The fixture catalogue behind a switch: the test edits what it answers, makes it fail, and reads what was asked.</summary>
    private sealed class ScriptedMetadataProvider : IMetadataProvider
    {
        private readonly FixtureMetadataProvider _fixtures = new();

        public ConcurrentQueue<int> Calls { get; } = new();

        public Func<int, bool> Fails { get; set; } = _ => false;

        public Func<MovieMetadata, MovieMetadata> Edit { get; set; } = m => m;

        public string Name => "scripted";

        public async Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
        {
            Calls.Enqueue(tmdbId);
            if (Fails(tmdbId))
            {
                throw new HttpRequestException("Response status code does not indicate success: 503 (Service Unavailable).", null, HttpStatusCode.ServiceUnavailable);
            }

            var found = await _fixtures.GetMovieAsync(tmdbId, cancellationToken);
            return found is null ? null : Edit(found);
        }

        public Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken) =>
            _fixtures.GetMovieByImdbIdAsync(imdbId, cancellationToken);

        public Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken) =>
            _fixtures.SearchAsync(query, year, cancellationToken);
    }
}
