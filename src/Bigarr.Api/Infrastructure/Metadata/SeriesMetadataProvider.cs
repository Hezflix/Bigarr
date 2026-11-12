using System.Globalization;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Metadata.Tvdb;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Metadata;

/// <summary>
/// Live series metadata: TVDB gives the episode list, numbering, specials and absolute numbers; TMDB
/// gives images, overviews and its ids. The two are linked through remote ids (TVDB's TheMovieDB id, else TMDB's
/// <c>find</c> by TVDB id). A show TVDB lacks is numbered by TMDB alone. Either client may be missing (no key).
/// </summary>
public sealed class SeriesMetadataProvider(
    TvdbClient? tvdb,
    TmdbClient? tmdb,
    IOptions<TvdbOptions> tvdbOptions,
    IOptions<MetadataOptions> tmdbOptions,
    ILogger<SeriesMetadataProvider> logger) : ISeriesMetadataProvider
{
    /// <summary>Same as Sonarr's placeholder for an episode without a title yet.</summary>
    public const string UntitledEpisode = "TBA";

    public string Name => (tvdb, tmdb) switch
    {
        (not null, not null) => "tvdb+tmdb",
        (not null, null) => "tvdb",
        _ => "tmdb",
    };

    public bool CanNumberByTvdb => tvdb is not null;

    /// <summary>
    /// Without a TVDB key the answer is TMDB's show (and, with <paramref name="withEpisodes"/>, TMDB's numbering, marked
    /// <see cref="SeriesMetadata.NumberedByTmdb"/>): good for a lookup, never to replace a TVDB episode list.
    /// </summary>
    public async Task<SeriesMetadata?> GetByTvdbIdAsync(int tvdbId, bool withEpisodes, CancellationToken cancellationToken)
    {
        if (tvdb is null)
        {
            var hit = tmdb is null ? null : (await tmdb.FindByTvdbIdAsync(tvdbId, cancellationToken))?.TvResults.FirstOrDefault();
            var details = hit is null ? null : await tmdb!.GetTvAsync(hit.Id, cancellationToken);
            return details is null ? null : await FromTmdbAsync(details, withEpisodes, cancellationToken) with { TvdbId = tvdbId };
        }

        var series = await tvdb.GetSeriesAsync(tvdbId, cancellationToken);
        if (series is null)
        {
            return null;
        }

        var tmdbId = TmdbIdOf(series.RemoteIds);
        TmdbTvDetails? tv = null;
        if (tmdb is not null)
        {
            tmdbId ??= (await tmdb.FindByTvdbIdAsync(tvdbId, cancellationToken))?.TvResults.FirstOrDefault()?.Id;
            tv = tmdbId is { } id ? await tmdb.GetTvAsync(id, cancellationToken) : null;
        }

        return await CombineAsync(series, tv, tmdbId, withEpisodes, cancellationToken);
    }

    public async Task<SeriesMetadata?> GetByTmdbIdAsync(int tmdbId, bool withEpisodes, CancellationToken cancellationToken)
    {
        if (tmdb is null)
        {
            // TVDB cannot be asked reliably by a TMDB id (its remote-id search mixes films and shows).
            return null;
        }

        var tv = await tmdb.GetTvAsync(tmdbId, cancellationToken);
        if (tv is null)
        {
            return null;
        }

        if (tvdb is not null && tv.ExternalIds?.TvdbId is > 0 and var tvdbId)
        {
            var series = await tvdb.GetSeriesAsync(tvdbId, cancellationToken);
            if (series is not null)
            {
                return await CombineAsync(series, tv, tmdbId, withEpisodes, cancellationToken);
            }

            logger.LogInformation("TMDB {TmdbId} points to TVDB {TvdbId}, which TVDB does not know: numbering from TMDB", tmdbId, tvdbId);
        }

        return await FromTmdbAsync(tv, withEpisodes, cancellationToken);
    }

    /// <summary>How many TMDB hits are bridged to their TVDB id (one small request each).</summary>
    public const int BridgedSearchResults = 10;

    /// <summary>
    /// TMDB ranks names far better than TVDB's search (which misses "one piece" the anime and "frieren"), so with both
    /// keys the order is TMDB's, each hit bridged to its TVDB id, followed by TVDB-only hits.
    /// </summary>
    public async Task<IReadOnlyList<SeriesMetadata>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var tvdbSearch = tvdb is null ? Task.FromResult<IReadOnlyList<TvdbSearchHit>>([]) : tvdb.SearchAsync(query, cancellationToken);
        if (tmdb is null)
        {
            return (await tvdbSearch).Select(MapSearchHit).OfType<SeriesMetadata>().ToList();
        }

        var tmdbResults = (await tmdb.SearchTvAsync(query, cancellationToken))?.Results ?? [];
        var results = tmdbResults.Select(MapTmdbSearchResult).ToList();
        if (tvdb is null)
        {
            return results;
        }

        var bridged = await Task.WhenAll(results.Take(BridgedSearchResults).Select(async r =>
        {
            var ids = await tmdb.GetTvExternalIdsAsync(r.TmdbId!.Value, cancellationToken);
            return r with { TvdbId = ids?.TvdbId is > 0 ? ids.TvdbId : null, ImdbId = NullIfBlank(ids?.ImdbId) };
        }));
        var merged = bridged.Concat(results.Skip(BridgedSearchResults)).ToList();

        var tvdbHits = (await tvdbSearch).Select(MapSearchHit).OfType<SeriesMetadata>();
        foreach (var hit in tvdbHits)
        {
            var twin = merged.FindIndex(m => (m.TvdbId is not null && m.TvdbId == hit.TvdbId) || (m.TmdbId is not null && m.TmdbId == hit.TmdbId));
            if (twin < 0)
            {
                merged.Add(hit);
            }
            else if (merged[twin].TvdbId is null)
            {
                merged[twin] = merged[twin] with { TvdbId = hit.TvdbId };
            }
        }

        return merged;
    }

    public async Task<IReadOnlyList<EpisodeGroupSummary>> GetEpisodeGroupsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        if (tmdb is null)
        {
            return [];
        }

        var groups = await tmdb.GetEpisodeGroupsAsync(tmdbId, cancellationToken);
        return (groups?.Results ?? []).Select(TmdbOrders.Summary).ToList();
    }

    public async Task<IReadOnlyList<OrderEpisode>?> GetOrderEpisodesAsync(int tmdbId, OrderChoice order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (tmdb is null)
        {
            return null;
        }

        switch (order.Ordering)
        {
            case EpisodeOrder.TmdbDefault:
                var tv = await tmdb.GetTvAsync(tmdbId, cancellationToken);
                if (tv is null)
                {
                    return null;
                }

                return TmdbOrders.FromDefaultOrder(await tmdb.GetTvSeasonsAsync(tv.Id, tv.Seasons.Select(s => s.SeasonNumber), cancellationToken));
            case EpisodeOrder.TmdbEpisodeGroup when !string.IsNullOrWhiteSpace(order.EpisodeGroupId):
                var group = await tmdb.GetEpisodeGroupAsync(order.EpisodeGroupId, cancellationToken);
                return group is null ? null : TmdbOrders.FromEpisodeGroup(group);
            default:
                return null;
        }
    }

    // ---- TVDB + TMDB ------------------------------------------------------------------------------------------------

    private async Task<SeriesMetadata> CombineAsync(TvdbSeriesExtended series, TmdbTvDetails? tv, int? tmdbId, bool withEpisodes, CancellationToken ct)
    {
        var language = tvdbOptions.Value.Language;
        var title = Translation(series.Translations?.NameTranslations?.Select(t => (t.Language, t.Name)), language)
            ?? NullIfBlank(series.Name) ?? NullIfBlank(tv?.Name) ?? $"TVDB {series.Id}";
        var originalTitle = NullIfBlank(tv?.OriginalName) ?? NullIfBlank(series.Name);
        var originalLanguage = IsoCodes.Language(series.OriginalLanguage) ?? NullIfBlank(tv?.OriginalLanguage);
        var genres = (series.Genres ?? []).Select(g => g.Name).Concat(tv?.Genres.Select(g => g.Name) ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var firstAired = ParseDate(series.FirstAired) ?? ParseDate(tv?.FirstAirDate);
        var country = IsoCodes.Country(series.OriginalCountry) ?? tv?.OriginCountry.FirstOrDefault()?.ToLowerInvariant();

        var images = TmdbImages(tv?.PosterPath, tv?.BackdropPath);
        if (images.All(i => i.Type != ImageType.Poster) && TvdbArtwork(series.Image) is { } poster)
        {
            images.Insert(0, new MediaImage(ImageType.Poster, poster));
        }

        var alternatives = AlternativeTitles(
            title,
            [
                (originalTitle, null),
                .. (series.Aliases ?? []).Select(a => (a.Name, IsoCodes.Language(a.Language))),
                .. (tv?.AlternativeTitles?.Results ?? []).Select(a => (a.Title, a.Iso31661)),
            ]);

        var metadata = new SeriesMetadata
        {
            TvdbId = series.Id,
            TmdbId = tmdbId ?? tv?.Id,
            ImdbId = ImdbIdOf(series.RemoteIds) ?? NullIfBlank(tv?.ExternalIds?.ImdbId),
            Title = title,
            OriginalTitle = string.Equals(originalTitle, title, StringComparison.OrdinalIgnoreCase) ? null : originalTitle,
            Year = ParseYear(series.Year) ?? firstAired?.Year,
            Overview = NullIfBlank(tv?.Overview)
                ?? Translation(series.Translations?.OverviewTranslations?.Select(t => (t.Language, t.Overview)), language)
                ?? NullIfBlank(series.Overview),
            Status = TvdbStatus(series.Status?.Name, tv?.Status),
            Network = NullIfBlank(series.OriginalNetwork?.Name) ?? NullIfBlank(series.LatestNetwork?.Name) ?? tv?.Networks.FirstOrDefault()?.Name,
            Runtime = series.AverageRuntime is > 0 ? series.AverageRuntime : tv?.EpisodeRunTime.FirstOrDefault() is > 0 and var r ? r : null,
            AirTime = NullIfBlank(series.AirsTime),
            OriginalCountry = country,
            NetworkCountry = IsoCodes.Country(series.OriginalNetwork?.Country) ?? IsoCodes.Country(series.LatestNetwork?.Country)
                ?? NullIfBlank(tv?.Networks.FirstOrDefault()?.OriginCountry)?.ToLowerInvariant(),
            OriginalLanguage = originalLanguage,
            Genres = genres,
            AlternativeTitles = alternatives,
            Images = images,
            FirstAired = firstAired,
            LastAired = ParseDate(series.LastAired) ?? ParseDate(tv?.LastAirDate),
            SuggestedType = Suggest(genres, originalLanguage, tv?.Type),
            Source = tv is null ? "tvdb" : "tvdb+tmdb",
        };

        if (!withEpisodes)
        {
            return metadata;
        }

        var tvdbEpisodes = await tvdb!.GetEpisodesAsync(series.Id, ct);
        var tmdbSeasons = tv is null || tmdb is null
            ? []
            : await tmdb.GetTvSeasonsAsync(tv.Id, tv.Seasons.Select(s => s.SeasonNumber), ct);
        var tmdbEpisodes = tmdbSeasons.SelectMany(s => s.Episodes).ToList();

        var episodes = MergeEpisodes(tvdbEpisodes, tmdbEpisodes);
        var seasonNumbers = episodes.Select(e => e.SeasonNumber)
            .Concat((series.Seasons ?? []).Where(s => IsDefaultOrder(s, series.DefaultSeasonType)).Select(s => s.Number))
            .Distinct()
            .Order();
        var seasons = seasonNumbers.Select(n =>
        {
            var tmdbSeason = tv?.Seasons.FirstOrDefault(s => s.SeasonNumber == n);
            var tvdbSeason = series.Seasons?.FirstOrDefault(s => s.Number == n && IsDefaultOrder(s, series.DefaultSeasonType));
            return new SeasonMetadata(
                n,
                n == 0 ? "Specials" : null,
                NullIfBlank(tmdbSeason?.Overview),
                TmdbImage(tmdbSeason?.PosterPath) ?? TvdbArtwork(tvdbSeason?.Image));
        }).ToList();

        return metadata with { Seasons = seasons, Episodes = episodes };
    }

    /// <summary>
    /// TVDB episodes in TVDB numbering, each linked to its TMDB twin when one agrees: same number with an air date
    /// within two days (or the same title when a date is missing), else the same air date with the same title or alone
    /// that day. Unmatched episodes simply have no TMDB id.
    /// </summary>
    private List<EpisodeMetadata> MergeEpisodes(IReadOnlyList<TvdbEpisode> tvdbEpisodes, IReadOnlyList<TmdbTvEpisode> tmdbEpisodes)
    {
        var byNumber = tmdbEpisodes.ToLookup(e => (e.SeasonNumber, e.EpisodeNumber));
        var byDate = tmdbEpisodes.Where(e => ParseDate(e.AirDate) is not null).ToLookup(e => ParseDate(e.AirDate)!.Value);
        var used = new HashSet<int>();

        var result = new List<EpisodeMetadata>(tvdbEpisodes.Count);
        foreach (var episode in tvdbEpisodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.Number))
        {
            var aired = ParseDate(episode.Aired);
            var twin = byNumber[(episode.SeasonNumber, episode.Number)].FirstOrDefault(c => !used.Contains(c.Id) && Agrees(episode, aired, c));
            if (twin is null && aired is { } date)
            {
                var sameDay = byDate[date].Where(c => !used.Contains(c.Id) && (c.SeasonNumber == 0) == (episode.SeasonNumber == 0)).ToList();
                twin = sameDay.FirstOrDefault(c => SameTitle(episode.Name, c.Name)) ?? (sameDay.Count == 1 ? sameDay[0] : null);
            }

            if (twin is not null)
            {
                used.Add(twin.Id);
            }

            result.Add(new EpisodeMetadata
            {
                TvdbId = episode.Id,
                TmdbId = twin?.Id,
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.Number,
                AbsoluteEpisodeNumber = episode.AbsoluteNumber is > 0 ? episode.AbsoluteNumber : null,
                AirDate = aired,
                Title = NullIfBlank(episode.Name) ?? NullIfBlank(twin?.Name) ?? UntitledEpisode,
                Overview = NullIfBlank(episode.Overview) ?? NullIfBlank(twin?.Overview),
                Runtime = episode.Runtime is > 0 ? episode.Runtime : twin?.Runtime is > 0 ? twin.Runtime : null,
                ImageUrl = TmdbImage(twin?.StillPath) ?? TvdbArtwork(episode.Image),
            });
        }

        return result;
    }

    private static bool Agrees(TvdbEpisode episode, DateOnly? aired, TmdbTvEpisode candidate)
    {
        var other = ParseDate(candidate.AirDate);
        if (aired is { } a && other is { } b)
        {
            return Math.Abs(a.DayNumber - b.DayNumber) <= 2 || SameTitle(episode.Name, candidate.Name);
        }

        return aired is null && other is null || SameTitle(episode.Name, candidate.Name);
    }

    private static bool SameTitle(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        var left = a.CleanMovieTitle();
        return left.Length > 0 && left == b.CleanMovieTitle();
    }

    private static bool IsDefaultOrder(TvdbSeason season, int? defaultSeasonType) =>
        defaultSeasonType is { } type ? season.Type?.Id == type : string.Equals(season.Type?.Type, "official", StringComparison.OrdinalIgnoreCase);

    // ---- TMDB alone -------------------------------------------------------------------------------------------------

    private async Task<SeriesMetadata> FromTmdbAsync(TmdbTvDetails tv, bool withEpisodes, CancellationToken ct)
    {
        var title = NullIfBlank(tv.Name) ?? NullIfBlank(tv.OriginalName) ?? $"TMDB {tv.Id}";
        var genres = tv.Genres.Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList();
        var firstAired = ParseDate(tv.FirstAirDate);
        var metadata = new SeriesMetadata
        {
            TvdbId = tv.ExternalIds?.TvdbId is > 0 ? tv.ExternalIds.TvdbId : null,
            TmdbId = tv.Id,
            ImdbId = NullIfBlank(tv.ExternalIds?.ImdbId),
            Title = title,
            OriginalTitle = string.Equals(tv.OriginalName, title, StringComparison.OrdinalIgnoreCase) ? null : NullIfBlank(tv.OriginalName),
            Year = firstAired?.Year,
            Overview = NullIfBlank(tv.Overview),
            Status = TmdbStatus(tv.Status),
            Network = tv.Networks.FirstOrDefault()?.Name,
            Runtime = tv.EpisodeRunTime.FirstOrDefault() is > 0 and var r ? r : null,
            OriginalCountry = tv.OriginCountry.FirstOrDefault()?.ToLowerInvariant(),
            NetworkCountry = NullIfBlank(tv.Networks.FirstOrDefault()?.OriginCountry)?.ToLowerInvariant(),
            OriginalLanguage = NullIfBlank(tv.OriginalLanguage),
            Genres = genres,
            AlternativeTitles = AlternativeTitles(title, [(tv.OriginalName, null), .. (tv.AlternativeTitles?.Results ?? []).Select(a => (a.Title, a.Iso31661))]),
            Images = TmdbImages(tv.PosterPath, tv.BackdropPath),
            FirstAired = firstAired,
            LastAired = ParseDate(tv.LastAirDate),
            SuggestedType = Suggest(genres, tv.OriginalLanguage, tv.Type),
            Source = "tmdb",
        };

        if (!withEpisodes || tmdb is null)
        {
            return metadata;
        }

        var seasons = await tmdb.GetTvSeasonsAsync(tv.Id, tv.Seasons.Select(s => s.SeasonNumber), ct);
        return metadata with
        {
            Seasons = seasons.Select(s => new SeasonMetadata(
                s.SeasonNumber,
                s.SeasonNumber == 0 ? "Specials" : NullIfBlank(s.Name),
                NullIfBlank(s.Overview),
                TmdbImage(s.PosterPath))).ToList(),
            Episodes = seasons.SelectMany(s => s.Episodes).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(e => new EpisodeMetadata
            {
                TmdbId = e.Id,
                SeasonNumber = e.SeasonNumber,
                EpisodeNumber = e.EpisodeNumber,
                AirDate = ParseDate(e.AirDate),
                Title = NullIfBlank(e.Name) ?? UntitledEpisode,
                Overview = NullIfBlank(e.Overview),
                Runtime = e.Runtime is > 0 ? e.Runtime : null,
                ImageUrl = TmdbImage(e.StillPath),
            }).ToList(),
        };
    }

    // ---- search hits ------------------------------------------------------------------------------------------------

    private SeriesMetadata? MapSearchHit(TvdbSearchHit hit)
    {
        if (!int.TryParse(hit.TvdbId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvdbId))
        {
            return null;
        }

        var language = tvdbOptions.Value.Language;
        var title = (hit.Translations?.GetValueOrDefault(language) is { Length: > 0 } translated ? translated : null)
            ?? NullIfBlank(hit.Name) ?? $"TVDB {tvdbId}";
        var originalLanguage = IsoCodes.Language(hit.PrimaryLanguage);
        var genres = hit.Genres ?? [];
        return new SeriesMetadata
        {
            TvdbId = tvdbId,
            TmdbId = TmdbIdOf(hit.RemoteIds),
            ImdbId = ImdbIdOf(hit.RemoteIds),
            Title = title,
            OriginalTitle = string.Equals(hit.Name, title, StringComparison.OrdinalIgnoreCase) ? null : NullIfBlank(hit.Name),
            Year = ParseYear(hit.Year) ?? ParseDate(hit.FirstAirTime)?.Year,
            Overview = (hit.Overviews?.GetValueOrDefault(language) is { Length: > 0 } overview ? overview : null) ?? NullIfBlank(hit.Overview),
            Status = TvdbStatus(hit.Status, null),
            Network = NullIfBlank(hit.Network),
            OriginalCountry = IsoCodes.Country(hit.Country),
            OriginalLanguage = originalLanguage,
            Genres = genres,
            AlternativeTitles = AlternativeTitles(title, (hit.Aliases ?? []).Select(a => ((string?)a, (string?)null)).ToList()),
            Images = TvdbArtwork(hit.ImageUrl) is { } poster ? [new MediaImage(ImageType.Poster, poster)] : [],
            FirstAired = ParseDate(hit.FirstAirTime),
            SuggestedType = Suggest(genres, originalLanguage, null),
            Source = "tvdb",
        };
    }

    private SeriesMetadata MapTmdbSearchResult(TmdbTvSearchResult r)
    {
        var title = NullIfBlank(r.Name) ?? NullIfBlank(r.OriginalName) ?? $"TMDB {r.Id}";
        var genres = r.GenreIds.Select(id => TvGenres.GetValueOrDefault(id)).OfType<string>().ToList();
        return new SeriesMetadata
        {
            TmdbId = r.Id,
            Title = title,
            OriginalTitle = string.Equals(r.OriginalName, title, StringComparison.OrdinalIgnoreCase) ? null : NullIfBlank(r.OriginalName),
            Year = ParseDate(r.FirstAirDate)?.Year,
            Overview = NullIfBlank(r.Overview),
            OriginalCountry = r.OriginCountry.FirstOrDefault()?.ToLowerInvariant(),
            OriginalLanguage = NullIfBlank(r.OriginalLanguage),
            Genres = genres,
            Images = TmdbImages(r.PosterPath, r.BackdropPath),
            FirstAired = ParseDate(r.FirstAirDate),
            SuggestedType = Suggest(genres, r.OriginalLanguage, null),
            Source = "tmdb",
        };
    }

    /// <summary>TMDB TV genre ids → names, for search results which only carry ids.</summary>
    private static readonly Dictionary<int, string> TvGenres = new()
    {
        [10759] = "Action & Adventure",
        [16] = "Animation",
        [35] = "Comedy",
        [80] = "Crime",
        [99] = "Documentary",
        [18] = "Drama",
        [10751] = "Family",
        [10762] = "Kids",
        [9648] = "Mystery",
        [10763] = "News",
        [10764] = "Reality",
        [10765] = "Sci-Fi & Fantasy",
        [10766] = "Soap",
        [10767] = "Talk",
        [10768] = "War & Politics",
        [37] = "Western",
    };

    // ---- helpers ----------------------------------------------------------------------------------------------------

    /// <summary>A hint only: talk shows and news are daily, Japanese animation is anime.</summary>
    public static SeriesType Suggest(IReadOnlyCollection<string> genres, string? originalLanguage, string? tmdbType)
    {
        bool Has(string genre) => genres.Any(g => string.Equals(g, genre, StringComparison.OrdinalIgnoreCase));

        if (tmdbType is "Talk Show" or "News" || Has("Talk Show") || Has("Talk") || Has("News"))
        {
            return SeriesType.Daily;
        }

        if (Has("Anime") || (Has("Animation") && string.Equals(IsoCodes.Language(originalLanguage), "ja", StringComparison.Ordinal)))
        {
            return SeriesType.Anime;
        }

        return SeriesType.Standard;
    }

    private static int? TmdbIdOf(IEnumerable<TvdbRemoteId>? remoteIds) =>
        remoteIds?.Where(r => r.Type == 12 || (r.SourceName?.Contains("TheMovieDB", StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(r => int.TryParse(r.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
            .FirstOrDefault(id => id > 0);

    private static string? ImdbIdOf(IEnumerable<TvdbRemoteId>? remoteIds) =>
        remoteIds?.FirstOrDefault(r => r.Type == 2 || string.Equals(r.SourceName, "IMDB", StringComparison.OrdinalIgnoreCase))?.Id is { Length: > 0 } id
        && id.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
            ? id
            : null;

    private static string? Translation(IEnumerable<(string? Language, string? Text)>? translations, string language) =>
        translations?.FirstOrDefault(t => string.Equals(t.Language, language, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t.Text)).Text?.Trim();

    private static List<AlternativeTitle> AlternativeTitles(string title, IEnumerable<(string? Title, string? Language)> candidates)
    {
        var result = new List<AlternativeTitle>();
        foreach (var (candidate, language) in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)
                || string.Equals(candidate, title, StringComparison.OrdinalIgnoreCase)
                || result.Any(a => string.Equals(a.Title, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(new AlternativeTitle(candidate.Trim(), language));
        }

        return result;
    }

    private List<MediaImage> TmdbImages(string? posterPath, string? backdropPath)
    {
        var images = new List<MediaImage>();
        if (TmdbImage(posterPath) is { } poster)
        {
            images.Add(new MediaImage(ImageType.Poster, poster));
        }

        if (TmdbImage(backdropPath) is { } fanart)
        {
            images.Add(new MediaImage(ImageType.Fanart, fanart));
        }

        return images;
    }

    private string? TmdbImage(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : tmdbOptions.Value.ImageBaseUrl.TrimEnd('/') + "/original" + path;

    /// <summary>TVDB serves absolute URLs for series art and site-relative paths (<c>/banners/…</c>) for episodes.</summary>
    private string? TvdbArtwork(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : tvdbOptions.Value.ArtworkBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private static SeriesStatus TvdbStatus(string? tvdbStatus, string? tmdbStatus) => tvdbStatus?.Trim().ToLowerInvariant() switch
    {
        "continuing" => SeriesStatus.Continuing,
        "ended" => SeriesStatus.Ended,
        "upcoming" => SeriesStatus.Upcoming,
        _ => TmdbStatus(tmdbStatus),
    };

    private static SeriesStatus TmdbStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "ended" or "canceled" => SeriesStatus.Ended,
        "in production" or "planned" or "pilot" => SeriesStatus.Upcoming,
        _ => SeriesStatus.Continuing,
    };

    private static int? ParseYear(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) && year > 1800 ? year : null;

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 10)
        {
            return null;
        }

        return DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
