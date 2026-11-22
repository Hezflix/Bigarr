using Bigarr.Api.Infrastructure.SonarrImport;
using Microsoft.EntityFrameworkCore;
using TvSeries = Bigarr.Domain.Tv.Series;

namespace Bigarr.Api.Features.Migration;

/// <summary>The ids that tie a Bigarr series to a Sonarr one.</summary>
internal sealed record SeriesKeys(int Id, int? TvdbId, int? TmdbId, string? ImdbId);

/// <summary>A Sonarr show's ids as Sonarr has them (0 or null when unknown).</summary>
internal readonly record struct SonarrShowKeys(int TvdbId, int TmdbId, string? ImdbId)
{
    public static SonarrShowKeys Of(SonarrSeries series) => new(series.TvdbId, series.TmdbId, series.ImdbId);
}

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

    public T? Find(SonarrSeries series) => Find(SonarrShowKeys.Of(series));

    public T? Find(SonarrShowKeys show)
    {
        if (show.TvdbId > 0 && _byTvdb.TryGetValue(show.TvdbId, out var byTvdb))
        {
            return byTvdb;
        }

        if (show.TmdbId > 0 && _byTmdb.TryGetValue(show.TmdbId, out var byTmdb))
        {
            return byTmdb;
        }

        var imdb = show.ImdbId?.Trim();
        return !string.IsNullOrEmpty(imdb) && _byImdb.TryGetValue(imdb, out var byImdb) && byImdb.Count == 1 ? byImdb[0] : default;
    }
}

internal static class SeriesKeyIndex
{
    public static SeriesKeyIndex<T> Build<T>(IEnumerable<T> items, Func<T, SeriesKeys> keys) => new(items, keys);

    /// <summary>The Bigarr series any of these Sonarr shows can be, indexed: those sharing a TVDB, TMDB or IMDb id.</summary>
    public static async Task<SeriesKeyIndex<SeriesKeys>> LoadAsync(IQueryable<TvSeries> library, IEnumerable<SonarrShowKeys> shows, CancellationToken ct)
    {
        var list = shows.ToList();
        var tvdbIds = list.Where(s => s.TvdbId > 0).Select(s => (int?)s.TvdbId).Distinct().ToList();
        var tmdbIds = list.Where(s => s.TmdbId > 0).Select(s => (int?)s.TmdbId).Distinct().ToList();
        var imdbIds = list.Select(s => s.ImdbId?.Trim()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var candidates = list.Count == 0
            ? []
            : await library.AsNoTracking()
                .Where(s => tvdbIds.Contains(s.TvdbId) || tmdbIds.Contains(s.TmdbId) || imdbIds.Contains(s.ImdbId))
                .Select(s => new SeriesKeys(s.Id, s.TvdbId, s.TmdbId, s.ImdbId))
                .ToListAsync(ct);
        return Build(candidates, k => k);
    }

    /// <summary>The Bigarr series id a Sonarr show is, or null.</summary>
    public static async Task<int?> SeriesIdAsync(IQueryable<TvSeries> library, SonarrShowKeys show, CancellationToken ct) =>
        (await LoadAsync(library, [show], ct)).Find(show)?.Id;
}
