using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;

namespace Bigarr.Api.Infrastructure.Metadata.Fixtures;

/// <summary>
/// A few well-known shows embedded in the assembly (a standard series, an anime with absolute numbers, a daily show),
/// trimmed from real TVDB/TMDB answers. Used by the tests and whenever neither a TVDB nor a TMDB key is configured.
/// </summary>
public sealed class FixtureSeriesMetadataProvider : ISeriesMetadataProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<SeriesMetadata> _series;

    private readonly Dictionary<int, TmdbOrderFixture> _orders;

    public FixtureSeriesMetadataProvider()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.series.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        _series = (JsonSerializer.Deserialize<List<SeriesMetadata>>(stream, Json) ?? []).Select(s => s with { Source = "fixtures" }).ToList();

        // TMDB's default order and episode groups of the fixture shows, recorded from the real API (snake_case).
        var ordersName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.series-orders.json", StringComparison.Ordinal));
        using var orders = assembly.GetManifestResourceStream(ordersName)!;
        _orders = (JsonSerializer.Deserialize<Dictionary<string, TmdbOrderFixture>>(orders, TmdbClient.Json) ?? [])
            .ToDictionary(p => int.Parse(p.Key, System.Globalization.CultureInfo.InvariantCulture), p => p.Value);
    }

    public string Name => "fixtures";

    public IReadOnlyList<SeriesMetadata> All => _series;

    public Task<SeriesMetadata?> GetByTvdbIdAsync(int tvdbId, bool withEpisodes, CancellationToken cancellationToken) =>
        Task.FromResult(Shape(_series.FirstOrDefault(s => s.TvdbId == tvdbId), withEpisodes));

    public Task<SeriesMetadata?> GetByTmdbIdAsync(int tmdbId, bool withEpisodes, CancellationToken cancellationToken) =>
        Task.FromResult(Shape(_series.FirstOrDefault(s => s.TmdbId == tmdbId), withEpisodes));

    public Task<IReadOnlyList<SeriesMetadata>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var needle = query.CleanMovieTitle();
        if (needle.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SeriesMetadata>>([]);
        }

        var results = _series
            .Where(s => new[] { s.Title, s.OriginalTitle }.Concat(s.AlternativeTitles.Select(a => a.Title))
                .OfType<string>()
                .Select(t => t.CleanMovieTitle())
                .Any(t => t.Length > 0 && t.Contains(needle, StringComparison.Ordinal)))
            .Select(s => Shape(s, withEpisodes: false)!)
            .ToList();
        return Task.FromResult<IReadOnlyList<SeriesMetadata>>(results);
    }

    public Task<IReadOnlyList<EpisodeGroupSummary>> GetEpisodeGroupsAsync(int tmdbId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EpisodeGroupSummary>>(
            _orders.TryGetValue(tmdbId, out var fixture) ? fixture.EpisodeGroups.Select(TmdbOrders.Summary).ToList() : []);

    public Task<IReadOnlyList<OrderEpisode>?> GetOrderEpisodesAsync(int tmdbId, OrderChoice order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!_orders.TryGetValue(tmdbId, out var fixture))
        {
            return Task.FromResult<IReadOnlyList<OrderEpisode>?>(null);
        }

        IReadOnlyList<OrderEpisode>? episodes = order.Ordering switch
        {
            EpisodeOrder.TmdbDefault => TmdbOrders.FromDefaultOrder(fixture.Seasons),
            EpisodeOrder.TmdbEpisodeGroup when order.EpisodeGroupId is { } id && fixture.GroupDetails.TryGetValue(id, out var group) => TmdbOrders.FromEpisodeGroup(group),
            _ => null,
        };
        return Task.FromResult(episodes);
    }

    private static SeriesMetadata? Shape(SeriesMetadata? series, bool withEpisodes) =>
        series is null || withEpisodes ? series : series with { Seasons = [], Episodes = [] };
}
