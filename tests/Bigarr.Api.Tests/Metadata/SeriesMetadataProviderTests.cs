using System.Net;
using System.Text;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Metadata.Tvdb;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Metadata;

public sealed class SeriesMetadataProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    // ---- TVDB client ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Episodes_are_paged_by_counting_pages_not_by_following_links_next()
    {
        var stub = new Stub(request => request.RequestUri!.AbsolutePath switch
        {
            "/v4/login" => Json(Login("token-1")),
            "/v4/series/1/episodes/default/eng" => Query(request, "page") switch
            {
                // links.next deliberately points somewhere else: it must not be followed.
                "0" => Json(EpisodePage(0, 500, total: 537, next: "https://api4.thetvdb.com/v4/series/1/episodes/default/eng?page=7")),
                "1" => Json(EpisodePage(500, 37, total: 537, next: null)),
                _ => Json(EpisodePage(0, 0, total: 537, next: null)),
            },
            _ => NotFound(),
        });
        var (tvdb, _) = Tvdb(stub);

        var episodes = await tvdb.GetEpisodesAsync(1, CancellationToken.None);

        Assert.Equal(537, episodes.Count);
        Assert.Equal(537, episodes.Select(e => e.Id).Distinct().Count());
        Assert.Equal(["0", "1"], stub.Requests.Where(r => r.Path.Contains("/episodes/")).Select(r => r.Query["page"]));
    }

    [Fact]
    public async Task Paging_stops_at_total_items_even_when_the_last_page_is_full()
    {
        var stub = new Stub(request => request.RequestUri!.AbsolutePath switch
        {
            "/v4/login" => Json(Login("token-1")),
            "/v4/series/2/episodes/default/eng" => Json(EpisodePage(int.Parse(Query(request, "page")!) * 500, 500, total: 1000, next: "https://example.invalid/next")),
            _ => NotFound(),
        });
        var (tvdb, _) = Tvdb(stub);

        var episodes = await tvdb.GetEpisodesAsync(2, CancellationToken.None);

        Assert.Equal(1000, episodes.Count);
        Assert.Equal(2, stub.Requests.Count(r => r.Path.Contains("/episodes/")));
    }

    [Fact]
    public async Task Logs_in_once_with_the_key_in_the_body_and_reuses_the_token()
    {
        var stub = new Stub(request => request.RequestUri!.AbsolutePath switch
        {
            "/v4/login" => Json(Login("token-1")),
            "/v4/series/81189/extended" => Json(TvdbRecorded.SeriesExtended81189),
            _ => NotFound(),
        });
        var (tvdb, _) = Tvdb(stub);

        await tvdb.GetSeriesAsync(81189, CancellationToken.None);
        await tvdb.GetSeriesAsync(81189, CancellationToken.None);

        var login = Assert.Single(stub.Requests, r => r.Path == "/v4/login");
        Assert.Equal("POST", login.Method);
        Assert.Contains("\"apikey\":\"tvdb-test-key\"", login.Body);
        Assert.All(stub.Requests, r => Assert.DoesNotContain("tvdb-test-key", r.Uri.ToString()));
        Assert.All(stub.Requests.Where(r => r.Path != "/v4/login"), r => Assert.Equal("Bearer token-1", r.Authorization));
    }

    [Fact]
    public async Task An_expired_token_is_replaced_once_on_401()
    {
        var logins = 0;
        var stub = new Stub(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v4/login")
            {
                logins++;
                return Json(Login($"token-{logins}"));
            }

            return request.Headers.Authorization?.Parameter == "token-1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(TvdbRecorded.SeriesExtended81189);
        });
        var (tvdb, _) = Tvdb(stub);

        var series = await tvdb.GetSeriesAsync(81189, CancellationToken.None);

        Assert.Equal("Breaking Bad", series!.Name);
        Assert.Equal(2, logins);
    }

    // ---- TVDB + TMDB ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tvdb_numbering_is_joined_with_tmdb_ids_and_images_through_the_remote_id()
    {
        var provider = Provider(BreakingBadRoutes());

        var series = await provider.GetByTvdbIdAsync(81189, withEpisodes: true, CancellationToken.None);

        Assert.NotNull(series);
        Assert.Equal("tvdb+tmdb", series.Source);
        Assert.Equal(81189, series.TvdbId);
        Assert.Equal(1396, series.TmdbId);
        Assert.Equal("tt0903747", series.ImdbId);
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal(2008, series.Year);
        Assert.Equal(SeriesStatus.Ended, series.Status);
        Assert.Equal("AMC", series.Network);
        Assert.Equal("21:00", series.AirTime);
        Assert.Equal("us", series.OriginalCountry);
        Assert.Equal("en", series.OriginalLanguage);
        Assert.Equal(SeriesType.Standard, series.SuggestedType);
        Assert.StartsWith("https://image.tmdb.org/t/p/original/", series.Images.Single(i => i.Type == ImageType.Poster).RemoteUrl);
        Assert.StartsWith("Walter White, a New Mexico chemistry teacher", series.Overview);

        Assert.Equal([0, 1, 2], series.Seasons.Select(s => s.SeasonNumber));
        Assert.Equal("Specials", series.Seasons[0].Title);
        Assert.Equal(
            [(0, 1, 3859781, (int?)62131), (1, 1, 349232, 62085), (1, 2, 349233, 62086), (2, 1, 438912, 972873)],
            series.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber, e.TvdbId!.Value, e.TmdbId)));

        var pilot = series.Episodes.Single(e => e.TvdbId == 349232);
        Assert.Equal("Pilot", pilot.Title);
        Assert.Equal(new DateOnly(2008, 1, 20), pilot.AirDate);
        Assert.StartsWith("https://image.tmdb.org/t/p/original/", pilot.ImageUrl);
        Assert.Null(series.Episodes.Single(e => e.SeasonNumber == 0).AbsoluteEpisodeNumber);
    }

    [Fact]
    public async Task Without_episodes_only_the_show_is_fetched()
    {
        var routes = BreakingBadRoutes();
        var provider = Provider(routes);

        var series = await provider.GetByTvdbIdAsync(81189, withEpisodes: false, CancellationToken.None);

        Assert.Empty(series!.Episodes);
        Assert.DoesNotContain(routes.Requests, r => r.Path.Contains("/episodes/") || (r.Query["append_to_response"] ?? string.Empty).Contains("season/"));
    }

    [Fact]
    public async Task A_tmdb_id_resolves_to_tvdb_numbering_through_external_ids()
    {
        var provider = Provider(BreakingBadRoutes());

        var series = await provider.GetByTmdbIdAsync(1396, withEpisodes: true, CancellationToken.None);

        Assert.Equal("tvdb+tmdb", series!.Source);
        Assert.Equal(81189, series.TvdbId);
        Assert.Equal(349232, series.Episodes.Single(e => e.SeasonNumber == 1 && e.EpisodeNumber == 1).TvdbId);
    }

    [Fact]
    public async Task A_show_tvdb_lacks_is_numbered_by_tmdb_alone()
    {
        var routes = BreakingBadRoutes(tvdbKnowsTheShow: false);
        var provider = Provider(routes);

        var series = await provider.GetByTmdbIdAsync(1396, withEpisodes: true, CancellationToken.None);

        Assert.Equal("tmdb", series!.Source);
        Assert.Equal(1396, series.TmdbId);
        Assert.Equal([0, 1, 2], series.Seasons.Select(s => s.SeasonNumber));
        Assert.Equal([62131, 62085, 62086, 972873], series.Episodes.Select(e => e.TmdbId!.Value));
        Assert.All(series.Episodes, e => Assert.Null(e.TvdbId));
    }

    [Fact]
    public async Task Search_keeps_tmdb_ranking_bridged_to_tvdb_ids_then_adds_tvdb_only_hits()
    {
        var stub = new Stub(request => request.RequestUri!.AbsolutePath switch
        {
            "/v4/login" => Json(Login("token-1")),
            "/v4/search" => Json(TvdbRecorded.TvdbSearchOnePiece),
            "/3/search/tv" => Json(TvdbRecorded.TmdbSearchOnePiece),
            "/3/tv/37854/external_ids" => Json("""{"id":37854,"imdb_id":"tt0388629","tvdb_id":81797}"""),
            "/3/tv/111110/external_ids" => Json("""{"id":111110,"imdb_id":"tt11737520","tvdb_id":392276}"""),
            _ => NotFound(),
        });
        var provider = Provider(stub);

        var results = await provider.SearchAsync("one piece", CancellationToken.None);

        Assert.Equal([(81797, 37854), (392276, 111110), (464521, 241709)], results.Select(r => (r.TvdbId!.Value, r.TmdbId!.Value)));
        Assert.Equal(SeriesType.Anime, results[0].SuggestedType);
        Assert.Equal("tt0388629", results[0].ImdbId);
    }

    [Theory]
    [InlineData(new[] { "Animation", "Action" }, "ja", null, SeriesType.Anime)]
    [InlineData(new[] { "Anime" }, "jpn", null, SeriesType.Anime)]
    [InlineData(new[] { "Animation" }, "en", null, SeriesType.Standard)]
    [InlineData(new[] { "Comedy" }, "en", "Talk Show", SeriesType.Daily)]
    [InlineData(new[] { "Talk Show" }, "en", null, SeriesType.Daily)]
    [InlineData(new[] { "Drama" }, "en", "Scripted", SeriesType.Standard)]
    public void Suggests_a_series_type(string[] genres, string language, string? tmdbType, SeriesType expected) =>
        Assert.Equal(expected, SeriesMetadataProvider.Suggest(genres, language, tmdbType));

    // ---- plumbing ---------------------------------------------------------------------------------------------------

    private static Stub BreakingBadRoutes(bool tvdbKnowsTheShow = true) => new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        var appends = Query(request, "append_to_response") ?? string.Empty;
        return path switch
        {
            "/v4/login" => Json(Login("token-1")),
            "/v4/series/81189/extended" => tvdbKnowsTheShow ? Json(TvdbRecorded.SeriesExtended81189) : NotFound(TvdbRecorded.NotFound),
            "/v4/series/81189/episodes/default/eng" => Json(TvdbRecorded.Episodes81189),
            "/3/tv/1396" when appends.Contains("season/", StringComparison.Ordinal) => Json(TvdbRecorded.TmdbTv1396Seasons),
            "/3/tv/1396" => Json(TvdbRecorded.TmdbTv1396),
            _ => NotFound(),
        };
    });

    private static (TvdbClient Client, Stub Stub) Tvdb(Stub stub)
    {
        var options = Options.Create(new TvdbOptions { ApiKey = "tvdb-test-key" });
        var auth = new TvdbAuthHandler(options, new TvdbTokenCache(new FixedTimeProvider(Now))) { InnerHandler = stub };
        var http = new HttpClient(auth) { BaseAddress = new Uri(options.Value.BaseUrl) };
        return (new TvdbClient(http, options, NullLogger<TvdbClient>.Instance), stub);
    }

    private static SeriesMetadataProvider Provider(Stub stub)
    {
        var tmdbOptions = Options.Create(new MetadataOptions { ApiKey = "tmdb-test-key" });
        var tmdbHttp = new HttpClient(new TmdbAuthHandler(tmdbOptions) { InnerHandler = stub }) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
        var (tvdb, _) = Tvdb(stub);
        return new SeriesMetadataProvider(
            tvdb,
            new TmdbClient(tmdbHttp, tmdbOptions),
            Options.Create(new TvdbOptions { ApiKey = "tvdb-test-key" }),
            tmdbOptions,
            NullLogger<SeriesMetadataProvider>.Instance);
    }

    private static string Login(string token) => JsonSerializer.Serialize(new { status = "success", data = new { token } });

    private static string EpisodePage(int firstId, int count, int total, string? next) => JsonSerializer.Serialize(new
    {
        status = "success",
        data = new
        {
            episodes = Enumerable.Range(firstId + 1, count).Select(id => new { id, name = $"Episode {id}", number = id, seasonNumber = 1, aired = "2020-01-01" }),
        },
        links = new Dictionary<string, object?> { ["next"] = next, ["total_items"] = total, ["page_size"] = 500 },
    });

    private static string? Query(HttpRequestMessage request, string name) =>
        System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)[name];

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage NotFound(string body = "{}") =>
        new(HttpStatusCode.NotFound) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public sealed record SeenRequest(string Method, Uri Uri, string Path, System.Collections.Specialized.NameValueCollection Query, string? Authorization, string? Body);

    public sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add(new SeenRequest(
                    request.Method.Method,
                    request.RequestUri!,
                    request.RequestUri!.AbsolutePath,
                    System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query),
                    request.Headers.Authorization?.ToString(),
                    body));
            }

            return respond(request);
        }
    }
}
