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

    [Fact]
    public void Unparseable_titles_are_reported()
    {
        Assert.Equal("Release title could not be parsed", ReleaseMatcher.Match(null, Release("???"), Matrix));
    }
}
