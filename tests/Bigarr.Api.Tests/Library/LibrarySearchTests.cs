using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Library;

public sealed class LibraryTitleQueryTests
{
    private static LibraryTitleRow Row(int id, string title, int? year = null, params string[] alternatives) =>
        new(id, title, year, [], [.. alternatives]);

    private static readonly LibraryTitleRow[] Rows =
    [
        Row(1, "The Matrix", 1999, "Matrix"),
        Row(2, "The Matrix Reloaded", 2003),
        Row(3, "Blade Runner", 1982),
        Row(4, "Blade Runner 2049", 2017),
        Row(5, "Amélie", 2001),
        Row(6, "Spirited Away", 2001, "Le Voyage de Chihiro"),
        Row(7, "Schindler's List", 1993),
        Row(8, "Alien", 1979),
        Row(9, "Alien", 1992),
        Row(10, "Aliens", 1986),
    ];

    private static List<int> Ids(string term, int limit = 10) =>
        [.. LibraryTitleQuery.Parse(term)!.Rank(Rows, LibraryItemKind.Movie, limit).Select(r => r.Id)];

    [Fact]
    public void An_exact_title_comes_before_titles_that_start_with_it()
    {
        Assert.Equal([1, 2], Ids("the matrix"));
        Assert.Equal([1, 2], Ids("matrix"));
    }

    [Fact]
    public void Case_accents_and_punctuation_do_not_count()
    {
        Assert.Equal([5], Ids("AMELIE"));
        Assert.Equal([7], Ids("schindlers"));
        Assert.Equal([7], Ids("schindler's list"));
    }

    [Fact]
    public void Every_word_must_start_a_word_of_the_title_in_any_order()
    {
        Assert.Equal([3, 4], Ids("bla run"));
        Assert.Equal([3, 4], Ids("runner blade"));
        Assert.Empty(Ids("blade matrix"));
    }

    [Fact]
    public void A_trailing_year_narrows_unless_it_is_part_of_the_title()
    {
        Assert.Equal([8], Ids("alien 1979"));
        Assert.Equal([9], Ids("alien (1992)"));
        Assert.Equal([4], Ids("blade runner 2049"));
        Assert.Empty(Ids("alien 2001"));
    }

    [Fact]
    public void An_alternative_title_matches_and_says_which_one()
    {
        var result = Assert.Single(LibraryTitleQuery.Parse("voyage chihiro")!.Rank(Rows, LibraryItemKind.Movie, 10));
        Assert.Equal(6, result.Id);
        Assert.Equal("Le Voyage de Chihiro", result.MatchedTitle);

        // The main title wins when both match: no alternative is reported.
        Assert.Null(LibraryTitleQuery.Parse("matrix")!.Rank(Rows, LibraryItemKind.Movie, 10)[0].MatchedTitle);
    }

    [Fact]
    public void Same_titles_rank_by_title_then_newest_first_and_the_limit_applies()
    {
        Assert.Equal([9, 8, 10], Ids("alien"));
        Assert.Equal([9, 8], Ids("alien", limit: 2));
    }

    [Fact]
    public void A_blank_term_is_no_query()
    {
        Assert.Null(LibraryTitleQuery.Parse(null));
        Assert.Null(LibraryTitleQuery.Parse("  - "));
    }
}

public sealed class LibrarySearchEndpointTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Finds_library_films_by_title_and_alternative_title_and_nothing_outside_the_library()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var profiles = await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json);
        var profileId = profiles!.Single(p => p.Name == QualitySeeder.HdProfileName).Id;
        var root = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(Directory.CreateTempSubdirectory("bigarr-search-").FullName));
        var rootId = (await root.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!.Id;
        foreach (var tmdbId in new[] { 129, 194 })
        {
            var added = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(tmdbId, profileId, rootId));
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        var chihiro = await client.GetFromJsonAsync<LibrarySearchResponse>("/api/v1/library/search?term=chihiro", Json);
        var spirited = Assert.Single(chihiro!.Movies);
        Assert.Equal(("Spirited Away", 2001, LibraryItemKind.Movie), (spirited.Title, spirited.Year, spirited.Kind));
        Assert.Equal("Le Voyage de Chihiro", spirited.MatchedTitle);
        Assert.Empty(chihiro.Series);

        var amelie = await client.GetFromJsonAsync<LibrarySearchResponse>("/api/v1/library/search?term=amelie%202001", Json);
        Assert.Equal("Amélie", Assert.Single(amelie!.Movies).Title);

        // In the catalogue, not in the library.
        var matrix = await client.GetFromJsonAsync<LibrarySearchResponse>("/api/v1/library/search?term=matrix", Json);
        Assert.Empty(matrix!.Movies);

        var blank = await client.GetFromJsonAsync<LibrarySearchResponse>("/api/v1/library/search?term=", Json);
        Assert.Empty(blank!.Movies);
    }
}
