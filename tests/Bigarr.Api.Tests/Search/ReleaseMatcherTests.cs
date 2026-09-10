using Bigarr.Api.Features.Search;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Search;

public sealed class ReleaseMatcherTests
{
    private static readonly Movie Matrix = new()
    {
        TmdbId = 603,
        ImdbId = "tt0133093",
        Title = "The Matrix",
        OriginalTitle = "The Matrix",
        SortTitle = "matrix",
        Year = 1999,
        AlternativeTitles = [new AlternativeTitle("Matrix", "FR")],
    };

    private static ReleaseInfo Release(string title, string? imdb = null, int? tmdb = null) =>
        new() { Guid = title, Title = title, IndexerName = "Mock", ImdbId = imdb, TmdbId = tmdb };

    private static string? Match(string title, string? imdb = null, int? tmdb = null)
    {
        var release = Release(title, imdb, tmdb);
        return ReleaseMatcher.Match(ReleaseMatcher.TryParse(title), release, Matrix);
    }

    [Fact]
    public void Titles_match_through_alternative_titles_and_a_year_within_one()
    {
        Assert.Null(Match("The.Matrix.1999.1080p.BluRay.x264-SPARKS"));
        Assert.Null(Match("Matrix.1999.MULTi.1080p.BluRay.x264-GRP"));
        Assert.Null(Match("The Matrix 2000 1080p WEB-DL-GRP"));
    }

    [Fact]
    public void Wrong_titles_and_years_are_explained()
    {
        Assert.Equal("Title 'The Matrix 2 Reloaded' does not match The Matrix", Match("The.Matrix.2.Reloaded.2003.1080p.BluRay.x264-GRP"));
        Assert.Equal("Year 2006 does not match 1999", Match("The.Matrix.2006.1080p.BluRay.x264-GRP"));
        Assert.StartsWith("Title 'Inception'", Match("Inception.2010.1080p.BluRay.x264-GRP"));
    }

    [Fact]
    public void Identifiers_from_the_indexer_or_the_title_override_titles()
    {
        // A foreign title we do not know, but the indexer says it is the film.
        Assert.Null(Match("La.Matrice.1999.FRENCH.1080p.BluRay.x264-GRP", imdb: "0133093"));
        Assert.Null(Match("La.Matrice.1999.FRENCH.1080p.BluRay.x264-GRP", tmdb: 603));
        Assert.Null(Match("The.Matrix.1999.1080p.BluRay.x264-GRP.[tmdb-603]", tmdb: null));

        // Right title, wrong identifiers: the identifiers win.
        Assert.Equal("Release is TMDB 604, not 603", Match("The.Matrix.1999.1080p.BluRay.x264-GRP", tmdb: 604));
        Assert.Equal("Release is IMDb tt0234215, not tt0133093", Match("The.Matrix.1999.1080p.BluRay.x264-GRP", imdb: "tt0234215"));
    }

    private static Movie LibraryMovie(int id, int tmdbId, string title, int? year, string? imdb = null, params string[] alternatives) => new()
    {
        Id = id,
        TmdbId = tmdbId,
        ImdbId = imdb,
        Title = title,
        SortTitle = title.ToLowerInvariant(),
        Year = year,
        AlternativeTitles = alternatives.Select(a => new AlternativeTitle(a, null)).ToList(),
    };

    private static readonly LibraryIndex Library = LibraryIndex.Build(
    [
        LibraryMovie(1, 603, "The Matrix", 1999, "tt0133093", "Matrix"),
        LibraryMovie(2, 1091, "The Thing", 1982, "tt0084787"),
        LibraryMovie(3, 60935, "The Thing", 2011, "tt0905372"),
        LibraryMovie(4, 194, "Amélie", 2001, "tt0211915", "Le Fabuleux Destin d'Amélie Poulain"),
        LibraryMovie(5, 111, "Twin", 2010),
        LibraryMovie(6, 112, "Twin", 2011),
    ]);

    private static (int? MovieId, string? Reason) Find(string title, string? imdb = null, int? tmdb = null)
    {
        var release = Release(title, imdb, tmdb);
        var (movie, reason) = ReleaseMatcher.FindMovie(ReleaseMatcher.TryParse(title), release, Library);
        return (movie?.Id, reason);
    }

    [Fact]
    public void Library_lookup_uses_identifiers_first()
    {
        Assert.Equal((1, (string?)null), Find("La.Matrice.1999.FRENCH.1080p.BluRay.x264-GRP", tmdb: 603));
        Assert.Equal((3, (string?)null), Find("Some.Weird.Name.2011.1080p.BluRay-GRP", imdb: "tt0905372"));
        Assert.Equal((1, (string?)null), Find("The.Matrix.1999.1080p.BluRay.x264-GRP.{tmdb-603}"));
        Assert.Equal(((int?)null, "TMDB 27205 is not in the library"), Find("Inception.2010.1080p.BluRay.x264-GRP", tmdb: 27205));

        // An unknown IMDb id does not veto a title that matches: some indexers attach wrong ids.
        Assert.Equal((1, (string?)null), Find("The.Matrix.1999.1080p.BluRay.x264-GRP", imdb: "tt9999999"));
        Assert.Equal(((int?)null, "IMDb tt9999999 is not in the library and no title matches 'Inception'"), Find("Inception.2010.1080p.BluRay.x264-GRP", imdb: "tt9999999"));
    }

    [Fact]
    public void Library_lookup_by_title_needs_a_year_when_titles_collide()
    {
        Assert.Equal((2, (string?)null), Find("The.Thing.1982.1080p.BluRay.x264-GRP"));
        Assert.Equal((3, (string?)null), Find("The.Thing.2011.1080p.BluRay.x264-GRP"));
        // Two films a year apart both fit a ±1 year window: nothing to choose from.
        Assert.Equal(((int?)null, "Several movies in the library match 'Twin' (2010)"), Find("Twin.2010.1080p.BluRay.x264-GRP"));
        Assert.Equal(((int?)null, "'The Thing' is in the library but not from 2005"), Find("The.Thing.2005.1080p.BluRay.x264-GRP"));
        Assert.Equal((1, (string?)null), Find("Matrix.1999.MULTi.1080p.BluRay.x264-GRP"));
        Assert.Equal((4, (string?)null), Find("Le.Fabuleux.Destin.dAmelie.Poulain.2001.1080p.BluRay.x264-GRP"));
        Assert.Equal(((int?)null, "No movie in the library matches 'Inception'"), Find("Inception.2010.1080p.BluRay.x264-GRP"));
        Assert.Equal("Release title could not be parsed", ReleaseMatcher.FindMovie(null, Release("???"), Library).Reason);
    }

    [Fact]
    public void Unparseable_titles_are_reported()
    {
        Assert.Equal("Release title could not be parsed", ReleaseMatcher.Match(null, Release("???"), Matrix));
    }
}
