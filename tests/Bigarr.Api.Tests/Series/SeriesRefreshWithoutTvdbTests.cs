using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// The fixture catalogue, or what a provider without a TVDB key answers: TMDB's show and TMDB's own numbering (no
/// TVDB ids, other season and episode numbers) for the same TVDB id.
/// </summary>
public sealed class SwitchableSeriesProvider : ISeriesMetadataProvider
{
    private readonly FixtureSeriesMetadataProvider _fixtures = new();

    public bool Keyless { get; set; }

    public string Name => Keyless ? "tmdb" : _fixtures.Name;

    public bool CanNumberByTvdb => !Keyless;

    public async Task<SeriesMetadata?> GetByTvdbIdAsync(int tvdbId, bool withEpisodes, CancellationToken cancellationToken) =>
        Tmdbify(await _fixtures.GetByTvdbIdAsync(tvdbId, withEpisodes, cancellationToken));

    public async Task<SeriesMetadata?> GetByTmdbIdAsync(int tmdbId, bool withEpisodes, CancellationToken cancellationToken) =>
        Tmdbify(await _fixtures.GetByTmdbIdAsync(tmdbId, withEpisodes, cancellationToken));

    public Task<IReadOnlyList<SeriesMetadata>> SearchAsync(string query, CancellationToken cancellationToken) => _fixtures.SearchAsync(query, cancellationToken);

    public Task<IReadOnlyList<EpisodeGroupSummary>> GetEpisodeGroupsAsync(int tmdbId, CancellationToken cancellationToken) => _fixtures.GetEpisodeGroupsAsync(tmdbId, cancellationToken);

    public Task<IReadOnlyList<OrderEpisode>?> GetOrderEpisodesAsync(int tmdbId, OrderChoice order, CancellationToken cancellationToken) => _fixtures.GetOrderEpisodesAsync(tmdbId, order, cancellationToken);

    /// <summary>TMDB numbers its seasons its own way: every regular episode in one long season, ids TMDB's only.</summary>
    private SeriesMetadata? Tmdbify(SeriesMetadata? found) =>
        !Keyless || found is null
            ? found
            : found with
            {
                Source = "tmdb",
                Seasons = [new SeasonMetadata(1)],
                Episodes = found.Episodes.Where(e => e.SeasonNumber > 0).Select((e, i) => e with { TvdbId = null, SeasonNumber = 1, EpisodeNumber = i + 1 }).ToList(),
            };
}

public sealed class KeylessSeriesApiFactory : BigarrApiFactory
{
    public KeylessSeriesApiFactory()
        : base(null, null, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now))
    {
    }

    public SwitchableSeriesProvider Provider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISeriesMetadataProvider>();
            services.AddSingleton<ISeriesMetadataProvider>(Provider);
        });
    }
}

/// <summary>
/// The incident end to end: a library imported Sonarr-style (TVDB ids, no TMDB ids, never refreshed) and the scheduled
/// refresh run without a TVDB key. It used to drop TVDB's episodes and add TMDB's next to them; now the episode list is
/// untouched, no provider is asked, and health says why. With TVDB back, the normal refresh runs.
/// </summary>
public sealed class SeriesRefreshWithoutTvdbTests(KeylessSeriesApiFactory factory) : IClassFixture<KeylessSeriesApiFactory>
{
    [Fact]
    public async Task The_scheduled_refresh_without_a_tvdb_key_changes_no_episode_and_health_says_so()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        using (var scope = factory.Services.CreateScope())
        {
            // As a Sonarr import leaves it: TVDB ids only, never refreshed by Bigarr.
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            await db.Episodes.Where(e => e.SeriesId == series.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.TmdbId, (int?)null));
            await db.Series.Where(s => s.Id == series.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastMetadataRefreshAt, (DateTime?)null));
        }

        var before = await EpisodesAsync(series.Id);
        factory.Provider.Keyless = true;
        try
        {
            var run = await RunRefreshAsync(client);
            Assert.True(run.Success, run.Error);
            Assert.Contains("1 waiting for a TVDB key", run.Summary, StringComparison.Ordinal);
            Assert.Equal(Snap(before), Snap(await EpisodesAsync(series.Id)));

            // Asked by hand: refused, nothing changed.
            var manual = await client.PostAsync($"/api/v1/series/{series.Id}/refresh", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, manual.StatusCode);
            Assert.Equal(Snap(before), Snap(await EpisodesAsync(series.Id)));

            var health = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
            Assert.Contains(health.Issues, i => i.Source == "seriesMetadata" && i.Message.StartsWith("TVDB key missing: series episode lists are not refreshed", StringComparison.Ordinal));

            // Adding a show TVDB knows is refused rather than numbered by TMDB.
            var profiles = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Quality.QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
            var root = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Library.RootFolderResponse>>("/api/v1/rootfolders", Json))!.First(r => r.Kind == Bigarr.Domain.Library.MediaKind.Series);
            var add = await client.PostAsJsonAsync("/api/v1/series", new AddSeriesRequest(profiles.First(p => p.Name == Bigarr.Api.Features.Quality.QualitySeeder.SeriesHdProfileName).Id, root.Id, TvdbId: 424536));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, add.StatusCode);
        }
        finally
        {
            factory.Provider.Keyless = false;
        }

        // With TVDB: the normal refresh, same episodes (TVDB ids match), TMDB ids back, health clear of the warning.
        var refreshed = await client.PostAsync($"/api/v1/series/{series.Id}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var after = await EpisodesAsync(series.Id);
        Assert.Equal(before.Select(e => (e.Id, e.TvdbId, e.SeasonNumber, e.EpisodeNumber)), after.Select(e => (e.Id, e.TvdbId, e.SeasonNumber, e.EpisodeNumber)));
        Assert.All(after.Where(e => e.SeasonNumber > 0), e => Assert.NotNull(e.TmdbId));
        var clear = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
        Assert.DoesNotContain(clear.Issues, i => i.Message.StartsWith("TVDB key missing", StringComparison.Ordinal) || i.Message.StartsWith("TVDB failing", StringComparison.Ordinal));
    }

    private static async Task<TaskRunResponse> RunRefreshAsync(HttpClient client)
    {
        var run = await client.PostAsync($"/api/v1/system/tasks/{SeriesRefreshJob.JobName}/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        return (await run.Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
    }

    private static List<(int Id, int? TvdbId, int? TmdbId, int Season, int Number)> Snap(List<Episode> episodes) =>
        episodes.Select(e => (e.Id, e.TvdbId, e.TmdbId, e.SeasonNumber, e.EpisodeNumber)).ToList();

    private async Task<List<Episode>> EpisodesAsync(int seriesId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().Episodes.AsNoTracking()
            .Where(e => e.SeriesId == seriesId).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ThenBy(e => e.Id).ToListAsync();
    }
}
