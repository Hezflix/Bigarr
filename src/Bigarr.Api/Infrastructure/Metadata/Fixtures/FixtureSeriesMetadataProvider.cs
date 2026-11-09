using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Domain.Metadata;
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

    public FixtureSeriesMetadataProvider()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.series.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        _series = (JsonSerializer.Deserialize<List<SeriesMetadata>>(stream, Json) ?? []).Select(s => s with { Source = "fixtures" }).ToList();
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

    private static SeriesMetadata? Shape(SeriesMetadata? series, bool withEpisodes) =>
        series is null || withEpisodes ? series : series with { Seasons = [], Episodes = [] };
}
