using System.Net;
using System.Text;
using System.Text.Json;
using Bigarr.Api.Features.SeriesFilms;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Metadata.Tvdb;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.SeriesFilms;

/// <summary>Detection of specials that are films (S8) on recorded TVDB/TMDB data: flagged films, a likely one, false positives, placement.</summary>
public sealed class SeriesFilmDetectionTests
{
    private const int DemonSlayer = 348545;
    private const int Firefly = 78874;

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    // ---- the live provider on recorded TVDB answers ----------------------------------------------------------------

    [Fact]
    public async Task Tvdb_flags_and_linked_movie_reach_the_episode_metadata_with_tmdb_and_imdb_ids()
    {
        var stub = new SeriesMetadataProviderTests.Stub(request => request.RequestUri!.AbsolutePath switch
        {
            "/v4/login" => Json(JsonSerializer.Serialize(new { status = "success", data = new { token = "t" } })),
            "/v4/series/348545/extended" => Json(SpecialFilmsRecorded.SeriesExtended348545),
            "/v4/series/348545/episodes/default/eng" => Json(SpecialFilmsRecorded.Episodes348545),
            "/v4/movies/131963/extended" => Json(SpecialFilmsRecorded.Movie131963),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") },
        });

        var show = await Provider(stub).GetByTvdbIdAsync(DemonSlayer, withEpisodes: true, CancellationToken.None);

        var mugen = show!.Episodes.Single(e => e.TvdbId == 7766161);
        Assert.True(mugen.IsMovie);
        Assert.Equal(1, mugen.AirsAfterSeason);
        Assert.Equal(635302, mugen.MovieTmdbId);
        Assert.Equal("tt11032374", mugen.MovieImdbId);

        // Unflagged specials carry no film ids: detection judges them (title search), the provider does not.
        var bond = show.Episodes.Single(e => e.TvdbId == 7766030);
        Assert.False(bond.IsMovie);
        Assert.Null(bond.MovieTmdbId);
        Assert.False(show.Episodes.Single(e => e.TvdbId == 8863223).IsMovie);

        // Only the linked film record costs a request.
        Assert.Single(stub.Requests, r => r.Path.StartsWith("/v4/movies/", StringComparison.Ordinal));
    }

    // ---- the rules ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_anime_with_films_in_season_0_has_its_flagged_films_as_candidates_placed_by_tvdb()
    {
        var episodes = await EpisodesAsync(DemonSlayer);

        var flagged = episodes.Where(e => SeriesFilmDetection.Confidence(e) == FilmConfidence.Flagged).OrderBy(e => e.EpisodeNumber).ToList();
        Assert.Equal([2, 15, 16], flagged.Select(e => e.EpisodeNumber));
        Assert.Equal([635302, 1216221, 1311031], flagged.Select(e => e.MovieTmdbId!.Value));

        var mugen = flagged[0];
        Assert.Equal(new FilmPlacement(1, null, FilmPlacementSource.Tvdb), SeriesFilmDetection.Place(mugen, episodes));
        Assert.Equal("after season 1", SeriesFilmDetection.Label(1, null));
        Assert.Contains("TVDB flags it as a movie", SeriesFilmDetection.Evidence(mugen));
        Assert.Equal(new FilmPlacement(5, null, FilmPlacementSource.Tvdb), SeriesFilmDetection.Place(flagged[2], episodes));
    }

    [Fact]
    public async Task A_long_unflagged_special_is_a_likely_film_only_when_tmdb_has_its_title_year_and_runtime()
    {
        var episodes = await EpisodesAsync(DemonSlayer);
        var movies = new FixtureMetadataProvider();
        const string title = "Demon Slayer: Kimetsu no Yaiba";

        // "Movie: Sibling's Bond" (105 min, 2019): TMDB 820232 "Demon Slayer: Kimetsu no Yaiba - Sibling's Bond", 105 min.
        var bond = episodes.Single(e => e.EpisodeNumber == 1 && e.SeasonNumber == 0);
        Assert.True(SeriesFilmDetection.WorthTitleSearch(bond));
        Assert.Contains($"{title} Sibling's Bond", SeriesFilmDetection.TitleQueries(bond, title));
        Assert.True(SeriesFilmDetection.Matches(bond, title, (await movies.GetMovieAsync(820232, CancellationToken.None))!));

        // Placed by date: it played in cinemas a week before season 1 aired.
        Assert.Equal(new FilmPlacement(null, 1, FilmPlacementSource.AirDate), SeriesFilmDetection.Place(bond, episodes));

        // A same-titled film from another decade, or far longer, is not it.
        var film = (await movies.GetMovieAsync(820232, CancellationToken.None))!;
        Assert.False(SeriesFilmDetection.Matches(bond, title, film with { Year = 2012 }));
        Assert.False(SeriesFilmDetection.Matches(bond, title, film with { Runtime = 160 }));
    }

    [Fact]
    public async Task A_long_recap_special_is_not_a_candidate()
    {
        var episodes = await EpisodesAsync(DemonSlayer);
        var movies = new FixtureMetadataProvider();
        const string title = "Demon Slayer: Kimetsu no Yaiba";

        // "First Night: Brother and Sister's Bond": 110 minutes, a TV recap; no TMDB film has its title.
        var recap = episodes.Single(e => e.SeasonNumber == 0 && e.EpisodeNumber == 7);
        Assert.False(SeriesFilmDetection.IsCandidate(recap));
        Assert.True(SeriesFilmDetection.WorthTitleSearch(recap));
        foreach (var query in SeriesFilmDetection.TitleQueries(recap, title))
        {
            var hits = await movies.SearchAsync(query, recap.AirDate!.Value.Year, CancellationToken.None);
            Assert.DoesNotContain(hits, h => SeriesFilmDetection.Matches(recap, title, h));
        }

        // Even a TMDB film found by a loose search does not pass: the title must agree.
        var unrelated = (await movies.GetMovieAsync(635302, CancellationToken.None))!;
        Assert.False(SeriesFilmDetection.Matches(recap, title, unrelated));

        // Short specials are never searched.
        Assert.False(SeriesFilmDetection.WorthTitleSearch(episodes.Single(e => e.SeasonNumber == 0 && e.EpisodeNumber == 3)));
    }

    [Fact]
    public async Task A_non_anime_show_has_its_film_too()
    {
        var episodes = await EpisodesAsync(Firefly);

        var serenity = Assert.Single(episodes, SeriesFilmDetection.IsCandidate);
        Assert.Equal("Serenity", serenity.Title);
        Assert.Equal(16320, serenity.MovieTmdbId);
        Assert.Equal(new FilmPlacement(1, null, FilmPlacementSource.Tvdb), SeriesFilmDetection.Place(serenity, episodes));
    }

    [Fact]
    public void Air_date_placement_puts_a_film_after_the_last_season_that_had_ended()
    {
        Episode Ep(int season, int number, string date) => new() { Title = $"{season}x{number}", SeasonNumber = season, EpisodeNumber = number, AirDate = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture) };
        var show = new List<Episode> { Ep(1, 1, "2019-04-06"), Ep(1, 26, "2019-09-28"), Ep(2, 1, "2021-10-10"), Ep(2, 18, "2022-02-13") };

        Assert.Equal(new FilmPlacement(1, null, FilmPlacementSource.AirDate), SeriesFilmDetection.Place(Ep(0, 2, "2020-10-16"), show));
        Assert.Equal(new FilmPlacement(1, null, FilmPlacementSource.AirDate), SeriesFilmDetection.Place(Ep(0, 3, "2021-11-01"), show)); // during season 2
        Assert.Equal(new FilmPlacement(2, null, FilmPlacementSource.AirDate), SeriesFilmDetection.Place(Ep(0, 4, "2023-01-01"), show));
        Assert.Equal(FilmPlacement.Unknown, SeriesFilmDetection.Place(new Episode { Title = "x", SeasonNumber = 0, EpisodeNumber = 9 }, show));
    }

    [Fact]
    public void A_refresh_that_drops_the_flag_drops_the_film_but_keeps_a_title_match()
    {
        var episode = new Episode { Title = "x", SeasonNumber = 0, EpisodeNumber = 1 };
        SeriesFilmDetection.ApplyMetadata(episode, new EpisodeMetadata { Title = "x", IsMovie = true, MovieTmdbId = 1 });
        Assert.Equal(SpecialMovieMatch.TvdbMovie, episode.MovieMatch);

        SeriesFilmDetection.ApplyMetadata(episode, new EpisodeMetadata { Title = "x" });
        Assert.Equal(SpecialMovieMatch.None, episode.MovieMatch);
        Assert.Null(episode.MovieTmdbId);

        episode.MovieMatch = SpecialMovieMatch.TitleAndRuntime;
        episode.MovieTmdbId = 2;
        SeriesFilmDetection.ApplyMetadata(episode, new EpisodeMetadata { Title = "x" });
        Assert.Equal(2, episode.MovieTmdbId);
    }

    /// <summary>The fixture show's episodes as the catalogue sync stores them.</summary>
    private static async Task<List<Episode>> EpisodesAsync(int tvdbId)
    {
        var show = (await new FixtureSeriesMetadataProvider().GetByTvdbIdAsync(tvdbId, withEpisodes: true, CancellationToken.None))!;
        return show.Episodes.Select(info =>
        {
            var episode = new Episode
            {
                Title = info.Title,
                TvdbId = info.TvdbId,
                SeasonNumber = info.SeasonNumber,
                EpisodeNumber = info.EpisodeNumber,
                AirDate = info.AirDate,
                Runtime = info.Runtime,
            };
            SeriesFilmDetection.ApplyMetadata(episode, info);
            return episode;
        }).ToList();
    }

    private static SeriesMetadataProvider Provider(HttpMessageHandler stub)
    {
        var tvdbOptions = Options.Create(new TvdbOptions { ApiKey = "tvdb-test-key" });
        var auth = new TvdbAuthHandler(tvdbOptions, new TvdbTokenCache(new FixedTimeProvider(Now))) { InnerHandler = stub };
        var tvdb = new TvdbClient(new HttpClient(auth) { BaseAddress = new Uri(tvdbOptions.Value.BaseUrl) }, tvdbOptions, NullLogger<TvdbClient>.Instance);
        var tmdbOptions = Options.Create(new MetadataOptions { ApiKey = "tmdb-test-key" });
        var tmdbHttp = new HttpClient(new TmdbAuthHandler(tmdbOptions) { InnerHandler = stub }) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
        return new SeriesMetadataProvider(tvdb, new TmdbClient(tmdbHttp, tmdbOptions), tvdbOptions, tmdbOptions, NullLogger<SeriesMetadataProvider>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
