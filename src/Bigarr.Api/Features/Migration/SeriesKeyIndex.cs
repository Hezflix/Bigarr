using Bigarr.Api.Infrastructure.SonarrImport;

namespace Bigarr.Api.Features.Migration;

/// <summary>The ids that tie a Bigarr series to a Sonarr one.</summary>
internal sealed record SeriesKeys(int Id, int? TvdbId, int? TmdbId, string? ImdbId);

/// <summary>
/// Finds the Bigarr series a Sonarr series is: the one with its TVDB id, else the one with its TMDB id, else the only one
/// with its IMDb id. TVDB sometimes deletes a show and recreates it under a new id; Bigarr's refresh takes the new id while
/// Sonarr keeps the old one, and only the TMDB and IMDb ids still tie the two.
/// </summary>
internal sealed class SeriesKeyIndex<T>
{
    private readonly Dictionary<int, T> _byTvdb = [];
    private readonly Dictionary<int, T> _byTmdb = [];
    private readonly Dictionary<string, List<T>> _byImdb = new(StringComparer.OrdinalIgnoreCase);

    internal SeriesKeyIndex(IEnumerable<T> items, Func<T, SeriesKeys> keys)
    {
        foreach (var item in items)
        {
            var k = keys(item);
            if (k.TvdbId is { } tvdb)
            {
                _byTvdb.TryAdd(tvdb, item);
            }

            if (k.TmdbId is { } tmdb)
            {
                _byTmdb.TryAdd(tmdb, item);
            }

            if (!string.IsNullOrWhiteSpace(k.ImdbId))
            {
                (_byImdb.TryGetValue(k.ImdbId, out var list) ? list : _byImdb[k.ImdbId] = []).Add(item);
            }
        }
    }

    public T? Find(SonarrSeries series)
    {
        if (series.TvdbId > 0 && _byTvdb.TryGetValue(series.TvdbId, out var byTvdb))
        {
            return byTvdb;
        }

        if (series.TmdbId > 0 && _byTmdb.TryGetValue(series.TmdbId, out var byTmdb))
        {
            return byTmdb;
        }

        return !string.IsNullOrWhiteSpace(series.ImdbId) && _byImdb.TryGetValue(series.ImdbId, out var byImdb) && byImdb.Count == 1 ? byImdb[0] : default;
    }
}

internal static class SeriesKeyIndex
{
    public static SeriesKeyIndex<T> Build<T>(IEnumerable<T> items, Func<T, SeriesKeys> keys) => new(items, keys);
}
