using System.Net;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Metadata;

public sealed class TmdbMetadataProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Maps_movie_details_release_dates_and_titles()
    {
        var (provider, _) = Create(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/3/movie/603"] = (HttpStatusCode.OK, TmdbRecorded.Movie603),
        });

        var movie = await provider.GetMovieAsync(603, CancellationToken.None);

        Assert.NotNull(movie);
        Assert.Equal(603, movie.TmdbId);
        Assert.Equal("tt0133093", movie.ImdbId);
        Assert.Equal("The Matrix", movie.Title);
        Assert.Equal(1999, movie.Year);
        Assert.Equal(136, movie.Runtime);
        Assert.Equal("R", movie.Certification);
        Assert.Equal("en", movie.OriginalLanguage);
        Assert.Equal(["Action", "Science Fiction"], movie.Genres);
        Assert.Equal(MovieStatus.Released, movie.Status);
        Assert.Equal(new DateOnly(1999, 3, 31), movie.InCinemasDate);
        Assert.Equal(new DateOnly(2010, 12, 1), movie.DigitalReleaseDate);
        Assert.Equal(new DateOnly(1999, 9, 21), movie.PhysicalReleaseDate);
        Assert.Equal(2344, movie.CollectionTmdbId);
        Assert.Equal(8.2, movie.Ratings!.Tmdb!.Value);
        Assert.Equal(25000, movie.Ratings.Tmdb.Votes);

        // Working title duplicate of the main title is dropped; the others are kept with their country.
        Assert.Equal(["Matrix", "Матрица"], movie.AlternativeTitles.Select(a => a.Title));
        Assert.Equal("FR", movie.AlternativeTitles[0].Language);

        Assert.Equal("https://image.tmdb.org/t/p/original/p96dm7sCMn4VYAStA6siNz30G1r.jpg", movie.Images.Single(i => i.Type == ImageType.Poster).RemoteUrl);
        Assert.Equal("https://image.tmdb.org/t/p/original/icmmSD4vTTDKOq2vvdulafOGw93.jpg", movie.Images.Single(i => i.Type == ImageType.Fanart).RemoteUrl);
    }

    [Fact]
    public async Task Unknown_movie_is_null()
    {
        var (provider, _) = Create(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/3/movie/1"] = (HttpStatusCode.NotFound, TmdbRecorded.NotFound),
        });

        Assert.Null(await provider.GetMovieAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Search_maps_results_and_approximates_status_from_the_release_date()
    {
        var (provider, requests) = Create(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/3/search/movie"] = (HttpStatusCode.OK, TmdbRecorded.SearchMatrix),
        });

        var results = await provider.SearchAsync("the matrix", 1999, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Contains("query=the%20matrix", requests.Single().Query);
        Assert.Contains("year=1999", requests.Single().Query);

        Assert.Equal("The Matrix", results[0].Title);
        Assert.Equal(1999, results[0].Year);
        Assert.Equal(["Action", "Science Fiction"], results[0].Genres);
        Assert.Equal(MovieStatus.Released, results[0].Status);

        Assert.Equal(MovieStatus.Announced, results[1].Status);
        Assert.Null(results[1].Ratings);
        Assert.Empty(results[1].Images);
    }

    [Fact]
    public async Task Imdb_lookup_goes_through_find_then_details()
    {
        var (provider, requests) = Create(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/3/find/tt0133093"] = (HttpStatusCode.OK, TmdbRecorded.FindTt0133093),
            ["/3/movie/603"] = (HttpStatusCode.OK, TmdbRecorded.Movie603),
        });

        var movie = await provider.GetMovieByImdbIdAsync("tt0133093", CancellationToken.None);

        Assert.NotNull(movie);
        Assert.Equal(603, movie.TmdbId);
        Assert.Equal(["/3/find/tt0133093", "/3/movie/603"], requests.Select(r => r.AbsolutePath));
    }

    [Fact]
    public async Task A_v3_key_goes_in_the_query_string_and_a_v4_token_in_the_header()
    {
        var (v3, v3Requests, v3Headers) = CreateRaw("abc123", new Dictionary<string, (HttpStatusCode, string)> { ["/3/movie/603"] = (HttpStatusCode.OK, TmdbRecorded.Movie603) });
        var (v4, v4Requests, v4Headers) = CreateRaw("eyJhbGciOiJIUzI1NiJ9.payload.signature", new Dictionary<string, (HttpStatusCode, string)> { ["/3/movie/603"] = (HttpStatusCode.OK, TmdbRecorded.Movie603) });

        await v3.GetMovieAsync(603, CancellationToken.None);
        await v4.GetMovieAsync(603, CancellationToken.None);

        Assert.Contains("api_key=abc123", v3Requests.Single().Query);
        Assert.Null(v3Headers.Single());

        Assert.DoesNotContain("api_key", v4Requests.Single().Query);
        Assert.Equal("Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature", v4Headers.Single());
    }

    private static (TmdbMetadataProvider Provider, List<Uri> Requests) Create(Dictionary<string, (HttpStatusCode Status, string Body)> routes)
    {
        var (provider, requests, _) = CreateRaw("test-key", routes);
        return (provider, requests);
    }

    private static (TmdbMetadataProvider Provider, List<Uri> Requests, List<string?> AuthHeaders) CreateRaw(
        string apiKey, Dictionary<string, (HttpStatusCode Status, string Body)> routes)
    {
        var options = Options.Create(new MetadataOptions { ApiKey = apiKey });
        var requests = new List<Uri>();
        var authHeaders = new List<string?>();
        var stub = new StubHandler(routes, requests, authHeaders);
        var auth = new TmdbAuthHandler(options) { InnerHandler = stub };
        var http = new HttpClient(auth) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
        var client = new TmdbClient(http, options);
        var provider = new TmdbMetadataProvider(client, options, new FixedTimeProvider(Now));
        return (provider, requests, authHeaders);
    }

    private sealed class StubHandler(
        Dictionary<string, (HttpStatusCode Status, string Body)> routes,
        List<Uri> requests,
        List<string?> authHeaders) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            authHeaders.Add(request.Headers.Authorization?.ToString());

            var path = request.RequestUri!.AbsolutePath;
            if (!routes.TryGetValue(path, out var route))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(TmdbRecorded.NotFound) });
            }

            return Task.FromResult(new HttpResponseMessage(route.Status)
            {
                Content = new StringContent(route.Body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
