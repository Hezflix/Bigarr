using System.Globalization;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Domain.Discover;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Discover;

/// <summary>Turns TMDB answers into Discover's shapes and puts the library's availability on every film and series.</summary>
public sealed partial class DiscoverService(TmdbDiscoverClient tmdb, LibraryAvailability library, IOptions<MetadataOptions> metadata, Requests.RequestBadges badges)
{
    /// <summary>Studios offered as tiles (TMDB company ids), the big names Seerr's studio row shows.</summary>
    public static readonly DiscoverTile[] Studios =
    [
        new(2, "Walt Disney Pictures"),
        new(3, "Pixar"),
        new(420, "Marvel Studios"),
        new(174, "Warner Bros. Pictures"),
        new(33, "Universal Pictures"),
        new(4, "Paramount"),
        new(34, "Sony Pictures"),
        new(521, "DreamWorks Animation"),
        new(41077, "A24"),
        new(10342, "Studio Ghibli"),
    ];

    /// <summary>Networks offered as tiles (TMDB network ids), the ones Seerr's network row shows first.</summary>
    public static readonly DiscoverTile[] Networks =
    [
        new(213, "Netflix"),
        new(49, "HBO"),
        new(2739, "Disney+"),
        new(1024, "Prime Video"),
        new(2552, "Apple TV+"),
        new(453, "Hulu"),
        new(174, "AMC"),
        new(67, "Showtime"),
        new(4, "BBC One"),
        new(6, "NBC"),
        new(16, "CBS"),
        new(2, "ABC"),
    ];

    public bool Configured => tmdb.Configured;

    public async Task<List<DiscoverTile>> TilesAsync(DiscoverSliderKind kind, CancellationToken ct) => kind switch
    {
        DiscoverSliderKind.MovieGenres => Tiles(await tmdb.MovieGenresAsync(ct)),
        DiscoverSliderKind.SeriesGenres => Tiles(await tmdb.SeriesGenresAsync(ct)),
        DiscoverSliderKind.Studios => [.. Studios],
        DiscoverSliderKind.Networks => [.. Networks],
        _ => [],
    };

    /// <summary>A page of a row of titles (empty when its value is not usable); null for a row of tiles.</summary>
    public async Task<DiscoverPage?> SliderPageAsync(DiscoverSlider slider, int page, CancellationToken ct)
    {
        if (DiscoverSlider.ShowsTiles(slider.Kind))
        {
            return null;
        }

        int? id = int.TryParse(slider.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        var words = slider.Value ?? string.Empty;
        var (answer, type) = slider.Kind switch
        {
            DiscoverSliderKind.Trending => (await tmdb.TrendingAsync(page, ct), (DiscoverMediaType?)null),
            DiscoverSliderKind.PopularMovies => (await tmdb.PopularMoviesAsync(page, ct), DiscoverMediaType.Movie),
            DiscoverSliderKind.UpcomingMovies => (await tmdb.UpcomingMoviesAsync(page, ct), DiscoverMediaType.Movie),
            DiscoverSliderKind.PopularSeries => (await tmdb.PopularSeriesAsync(page, ct), DiscoverMediaType.Series),
            DiscoverSliderKind.UpcomingSeries => (await tmdb.UpcomingSeriesAsync(page, ct), DiscoverMediaType.Series),
            DiscoverSliderKind.MovieGenre when id is not null => (await tmdb.DiscoverMoviesAsync(id, null, null, page, ct), DiscoverMediaType.Movie),
            DiscoverSliderKind.MovieKeyword when id is not null => (await tmdb.DiscoverMoviesAsync(null, id, null, page, ct), DiscoverMediaType.Movie),
            DiscoverSliderKind.Studio when id is not null => (await tmdb.DiscoverMoviesAsync(null, null, id, page, ct), DiscoverMediaType.Movie),
            DiscoverSliderKind.SeriesGenre when id is not null => (await tmdb.DiscoverSeriesAsync(id, null, null, page, ct), DiscoverMediaType.Series),
            DiscoverSliderKind.SeriesKeyword when id is not null => (await tmdb.DiscoverSeriesAsync(null, id, null, page, ct), DiscoverMediaType.Series),
            DiscoverSliderKind.Network when id is not null => (await tmdb.DiscoverSeriesAsync(null, null, id, page, ct), DiscoverMediaType.Series),
            DiscoverSliderKind.MovieSearch when words.Length > 0 => (await tmdb.SearchMoviesAsync(words, page, ct), DiscoverMediaType.Movie),
            DiscoverSliderKind.SeriesSearch when words.Length > 0 => (await tmdb.SearchSeriesAsync(words, page, ct), DiscoverMediaType.Series),
            _ => ((TmdbPage?)null, (DiscoverMediaType?)null),
        };

        // Trending mixes in people: a row of titles keeps films and series.
        return await PageAsync(answer, type, keepPeople: false, ct);
    }

    public async Task<DiscoverPage> MoviesAsync(int? genre, int? keyword, int? studio, int page, CancellationToken ct) =>
        await PageAsync(await tmdb.DiscoverMoviesAsync(genre, keyword, studio, page, ct), DiscoverMediaType.Movie, keepPeople: false, ct);

    public async Task<DiscoverPage> SeriesAsync(int? genre, int? keyword, int? network, int page, CancellationToken ct) =>
        await PageAsync(await tmdb.DiscoverSeriesAsync(genre, keyword, network, page, ct), DiscoverMediaType.Series, keepPeople: false, ct);

    public async Task<DiscoverPage> SearchAsync(string query, int page, CancellationToken ct) =>
        IdTerm().Match(query) is { Success: true } id
            ? await PageAsync(await ByIdAsync(id.Groups["source"].Value.ToLowerInvariant(), id.Groups["id"].Value, ct), null, keepPeople: false, ct)
            : await PageAsync(await tmdb.SearchAsync(query, page, ct), null, keepPeople: true, ct);

    /// <summary>
    /// A search by id, as the library's former Add pages took it: <c>tmdb:603</c> (the film and the series of that id),
    /// <c>tvdb:81189</c>, <c>imdb:tt0133093</c> or a bare <c>tt0133093</c>.
    /// </summary>
    private async Task<TmdbPage> ByIdAsync(string source, string id, CancellationToken ct)
    {
        var results = new List<TmdbListItem>();
        if (source is "tmdb" && int.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var tmdbId))
        {
            if (await tmdb.MovieAsync(tmdbId, ct) is { } movie)
            {
                results.Add(new TmdbListItem { Id = movie.Id, MediaType = "movie", Title = movie.Title, ReleaseDate = movie.ReleaseDate, PosterPath = movie.PosterPath, Overview = movie.Overview, VoteAverage = movie.VoteAverage });
            }

            if (await tmdb.SeriesAsync(tmdbId, ct) is { } tv)
            {
                results.Add(new TmdbListItem { Id = tv.Id, MediaType = "tv", Name = tv.Name, FirstAirDate = tv.FirstAirDate, PosterPath = tv.PosterPath, Overview = tv.Overview, VoteAverage = tv.VoteAverage });
            }
        }
        else if (source is "tvdb" or "imdb" or "")
        {
            var found = await tmdb.FindAsync(source == "tvdb" ? id : id.ToLowerInvariant(), source == "tvdb" ? "tvdb_id" : "imdb_id", ct);
            results.AddRange((found?.MovieResults ?? []).Select(r => { r.MediaType = "movie"; return r; }));
            results.AddRange((found?.TvResults ?? []).Select(r => { r.MediaType = "tv"; return r; }));
        }

        return new TmdbPage { Page = 1, TotalPages = 1, TotalResults = results.Count, Results = results };
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?:(?<source>tmdb|tvdb|imdb)\s*:\s*(?<id>\w+)|(?<source>)(?<id>tt\d{5,}))\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex IdTerm();

    public async Task<DiscoverMovieResponse?> MovieAsync(int tmdbId, bool admin, CancellationToken ct)
    {
        var movie = await tmdb.MovieAsync(tmdbId, ct);
        if (movie is null)
        {
            return null;
        }

        var related = (movie.Recommendations?.Results ?? []).Concat(movie.Similar?.Results ?? []).Select(r => r.Id).Append(movie.Id).Distinct().ToList();
        var films = await library.FilmsAsync(related, ct);
        var match = films.GetValueOrDefault(movie.Id);
        var fourK = await library.FourKVersionAsync(ct) is not null;
        var country = metadata.Value.CertificationCountry;

        var requests = await badges.ForAsync(Domain.Requests.RequestMediaType.Movie, related, ct);
        return new DiscoverMovieResponse(
            movie.Id,
            movie.ImdbId,
            movie.Title ?? movie.OriginalTitle ?? string.Empty,
            movie.OriginalTitle,
            YearOf(movie.ReleaseDate),
            NullIfEmpty(movie.ReleaseDate),
            movie.Runtime is > 0 ? movie.Runtime : null,
            NullIfEmpty(movie.Overview),
            NullIfEmpty(movie.Tagline),
            movie.Status,
            movie.ReleaseDates?.Results
                .FirstOrDefault(r => string.Equals(r.Iso31661, country, StringComparison.OrdinalIgnoreCase))?
                .ReleaseDates.Select(d => d.Certification).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
            Named(movie.Genres.Select(g => (g.Id, g.Name))),
            Named(movie.ProductionCompanies.Select(c => (c.Id, c.Name))),
            [.. (movie.Credits?.Crew ?? []).Where(c => c.Job == "Director").Select(c => c.Name ?? string.Empty).Where(n => n.Length > 0).Distinct()],
            movie.VoteAverage is > 0 ? movie.VoteAverage : null,
            Image(movie.PosterPath),
            Image(movie.BackdropPath),
            Trailer(movie.Videos),
            Cast(movie.Credits),
            WithRequests(Items(movie.Recommendations?.Results ?? [], DiscoverMediaType.Movie, films, null, fourK, keepPeople: false).Take(20), requests, null),
            WithRequests(Items(movie.Similar?.Results ?? [], DiscoverMediaType.Movie, films, null, fourK, keepPeople: false).Take(20), requests, null),
            match?.Availability ?? NotInLibrary(fourK),
            admin ? match?.MovieId : null,
            requests.GetValueOrDefault(movie.Id)?.Slots);
    }

    public async Task<DiscoverSeriesResponse?> SeriesAsync(int tmdbId, bool admin, CancellationToken ct)
    {
        var tv = await tmdb.SeriesAsync(tmdbId, ct);
        if (tv is null)
        {
            return null;
        }

        var match = await library.SeriesAsync(tv.Id, tv.ExternalIds?.TvdbId, ct);
        var related = (tv.Recommendations?.Results ?? []).Concat(tv.Similar?.Results ?? []).Select(r => r.Id).Distinct().ToList();
        var others = await library.SeriesAsync(related, ct);
        var fourK = await library.FourKVersionAsync(ct) is not null;
        var notInLibrary = NotInLibrary(fourK);
        var requests = await badges.ForAsync(Domain.Requests.RequestMediaType.Series, [.. related.Append(tv.Id).Distinct()], ct);
        var mine = requests.GetValueOrDefault(tv.Id);

        // TMDB's seasons, with the availability of the library's season of the same number (the library numbers seasons as
        // TVDB does, which matches TMDB for most shows); a season only the library has comes after them.
        var seasons = tv.Seasons
            .OrderBy(s => s.SeasonNumber == 0 ? int.MaxValue : s.SeasonNumber)
            .Select(s => new DiscoverSeasonResponse(
                s.SeasonNumber,
                string.IsNullOrWhiteSpace(s.Name) ? $"Season {s.SeasonNumber}" : s.Name,
                s.EpisodeCount,
                NullIfEmpty(s.AirDate),
                Image(s.PosterPath),
                InLibraryOnly: false,
                match?.Seasons.GetValueOrDefault(s.SeasonNumber) ?? notInLibrary,
                mine?.Seasons.GetValueOrDefault(s.SeasonNumber)))
            .ToList();
        if (match is not null)
        {
            var known = tv.Seasons.Select(s => s.SeasonNumber).ToHashSet();
            seasons.AddRange(match.Seasons.Where(s => !known.Contains(s.Key)).Select(s =>
                new DiscoverSeasonResponse(s.Key, s.Key == 0 ? "Specials" : $"Season {s.Key}", null, null, null, InLibraryOnly: true, s.Value, mine?.Seasons.GetValueOrDefault(s.Key))));
        }

        return new DiscoverSeriesResponse(
            tv.Id,
            tv.ExternalIds?.TvdbId,
            tv.Name ?? tv.OriginalName ?? string.Empty,
            tv.OriginalName,
            YearOf(tv.FirstAirDate),
            NullIfEmpty(tv.FirstAirDate),
            NullIfEmpty(tv.LastAirDate),
            tv.Status,
            tv.EpisodeRunTime.FirstOrDefault() is > 0 and var runtime ? runtime : null,
            NullIfEmpty(tv.Overview),
            NullIfEmpty(tv.Tagline),
            Named(tv.Genres.Select(g => (g.Id, g.Name))),
            Named(tv.Networks.Select(n => (n.Id, n.Name))),
            [.. tv.CreatedBy.Select(c => c.Name ?? string.Empty).Where(n => n.Length > 0)],
            tv.VoteAverage is > 0 ? tv.VoteAverage : null,
            Image(tv.PosterPath),
            Image(tv.BackdropPath),
            Trailer(tv.Videos),
            Cast(tv.Credits),
            seasons,
            WithRequests(Items(tv.Recommendations?.Results ?? [], DiscoverMediaType.Series, null, others, fourK, keepPeople: false).Take(20), null, requests),
            WithRequests(Items(tv.Similar?.Results ?? [], DiscoverMediaType.Series, null, others, fourK, keepPeople: false).Take(20), null, requests),
            match?.Availability ?? notInLibrary,
            admin ? match?.SeriesId : null,
            mine?.Slots);
    }

    public async Task<DiscoverPersonResponse?> PersonAsync(int tmdbId, CancellationToken ct)
    {
        var person = await tmdb.PersonAsync(tmdbId, ct);
        if (person is null)
        {
            return null;
        }

        // Cast and crew credits together, one entry per title (the role first), most popular first.
        var credits = (person.CombinedCredits?.Cast ?? []).Select(c => (Item: c, Role: c.Character))
            .Concat((person.CombinedCredits?.Crew ?? []).Select(c => (Item: c, Role: c.Job)))
            .Where(c => c.Item.MediaType is "movie" or "tv" && !c.Item.Adult)
            .GroupBy(c => (c.Item.MediaType, c.Item.Id))
            .Select(g => g.First())
            .OrderByDescending(c => c.Item.Popularity ?? 0)
            .Take(60)
            .ToList();

        var page = await PageAsync(new TmdbPage { Page = 1, TotalPages = 1, TotalResults = credits.Count, Results = credits.Select(c => c.Item).ToList() }, null, keepPeople: false, ct);
        var roles = credits.ToDictionary(c => (c.Item.MediaType == "movie" ? DiscoverMediaType.Movie : DiscoverMediaType.Series, c.Item.Id), c => NullIfEmpty(c.Role));
        return new DiscoverPersonResponse(
            person.Id,
            person.Name ?? string.Empty,
            NullIfEmpty(person.Biography),
            NullIfEmpty(person.Birthday),
            NullIfEmpty(person.Deathday),
            NullIfEmpty(person.PlaceOfBirth),
            person.KnownForDepartment,
            Image(person.ProfilePath),
            [.. page.Results.Select(i => i with { Subtitle = roles.GetValueOrDefault((i.MediaType, i.TmdbId)) })]);
    }

    private static List<DiscoverTile> Tiles(TmdbGenreList? genres) =>
        [.. (genres?.Genres ?? []).Where(g => !string.IsNullOrWhiteSpace(g.Name)).Select(g => new DiscoverTile(g.Id, g.Name!))];

    private async Task<DiscoverPage> PageAsync(TmdbPage? answer, DiscoverMediaType? type, bool keepPeople, CancellationToken ct)
    {
        if (answer is null)
        {
            return new DiscoverPage(1, 0, 0, []);
        }

        var films = await library.FilmsAsync(answer.Results.Where(r => TypeOf(r, type) == DiscoverMediaType.Movie).Select(r => r.Id).ToList(), ct);
        var series = await library.SeriesAsync(answer.Results.Where(r => TypeOf(r, type) == DiscoverMediaType.Series).Select(r => r.Id).ToList(), ct);
        var fourK = await library.FourKVersionAsync(ct) is not null;
        var items = Items(answer.Results, type, films, series, fourK, keepPeople).ToList();
        var filmRequests = await badges.ForAsync(Domain.Requests.RequestMediaType.Movie, [.. items.Where(i => i.MediaType == DiscoverMediaType.Movie).Select(i => i.TmdbId)], ct);
        var seriesRequests = await badges.ForAsync(Domain.Requests.RequestMediaType.Series, [.. items.Where(i => i.MediaType == DiscoverMediaType.Series).Select(i => i.TmdbId)], ct);
        return new DiscoverPage(
            answer.Page,
            Math.Min(answer.TotalPages, TmdbDiscoverClient.MaxPage),
            answer.TotalResults,
            WithRequests(items, filmRequests, seriesRequests));
    }

    private static List<DiscoverItem> WithRequests(IEnumerable<DiscoverItem> items, IReadOnlyDictionary<int, Requests.TitleRequests>? films, IReadOnlyDictionary<int, Requests.TitleRequests>? series) =>
        [.. items.Select(i => i.MediaType switch
        {
            DiscoverMediaType.Movie when films?.GetValueOrDefault(i.TmdbId) is { } r => i with { Requests = r.Slots },
            DiscoverMediaType.Series when series?.GetValueOrDefault(i.TmdbId) is { } r => i with { Requests = r.Slots },
            _ => i,
        })];

    private IEnumerable<DiscoverItem> Items(
        IEnumerable<TmdbListItem> results,
        DiscoverMediaType? type,
        IReadOnlyDictionary<int, FilmMatch>? films,
        IReadOnlyDictionary<int, SeriesMatch>? series,
        bool fourK,
        bool keepPeople)
    {
        foreach (var r in results)
        {
            var kind = TypeOf(r, type);
            if (kind is null || r.Adult || (kind == DiscoverMediaType.Person && !keepPeople))
            {
                continue;
            }

            var date = kind == DiscoverMediaType.Series ? r.FirstAirDate : r.ReleaseDate;
            var availability = kind switch
            {
                DiscoverMediaType.Movie => films?.GetValueOrDefault(r.Id)?.Availability ?? NotInLibrary(fourK),
                DiscoverMediaType.Series => series?.GetValueOrDefault(r.Id)?.Availability ?? NotInLibrary(fourK),
                _ => null,
            };
            yield return new DiscoverItem(
                kind.Value,
                r.Id,
                r.Title ?? r.Name ?? string.Empty,
                YearOf(date),
                NullIfEmpty(date),
                Image(kind == DiscoverMediaType.Person ? r.ProfilePath : r.PosterPath),
                NullIfEmpty(r.Overview),
                r.VoteAverage is > 0 ? r.VoteAverage : null,
                kind == DiscoverMediaType.Person ? r.KnownForDepartment : null,
                availability);
        }
    }

    private static DiscoverMediaType? TypeOf(TmdbListItem item, DiscoverMediaType? type) => item.MediaType switch
    {
        "movie" => DiscoverMediaType.Movie,
        "tv" => DiscoverMediaType.Series,
        "person" => DiscoverMediaType.Person,
        null => type,
        _ => null,
    };

    private static AvailabilityResponse NotInLibrary(bool fourK) => new(AvailabilityState.NotInLibrary, fourK ? AvailabilityState.NotInLibrary : null);

    private List<DiscoverCastMember> Cast(TmdbCredits? credits) =>
        [.. (credits?.Cast ?? []).OrderBy(c => c.Order).Take(20).Select(c => new DiscoverCastMember(c.Id, c.Name ?? string.Empty, NullIfEmpty(c.Character), Image(c.ProfilePath)))];

    /// <summary>The official YouTube trailer first, then any YouTube trailer, then a teaser.</summary>
    private static string? Trailer(TmdbVideos? videos)
    {
        var video = (videos?.Results ?? [])
            .Where(v => v.Site == "YouTube" && !string.IsNullOrWhiteSpace(v.Key) && v.Type is "Trailer" or "Teaser")
            .OrderBy(v => v.Type == "Trailer" ? 0 : 1)
            .ThenBy(v => v.Official ? 0 : 1)
            .FirstOrDefault();
        return video is null ? null : "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(video.Key!);
    }

    private static List<DiscoverRef> Named(IEnumerable<(int Id, string? Name)> items) =>
        [.. items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Select(i => new DiscoverRef(i.Id, i.Name!))];

    /// <summary>The <c>original</c> size, which the web's poster picks a smaller TMDB width from.</summary>
    private string? Image(string? path) => string.IsNullOrWhiteSpace(path) ? null : metadata.Value.ImageBaseUrl.TrimEnd('/') + "/original" + path;

    private static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
