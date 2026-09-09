using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Metadata;

public sealed class FixtureMetadataProviderTests
{
    private readonly FixtureMetadataProvider _provider = new();

    [Fact]
    public void Loads_the_embedded_catalogue()
    {
        Assert.True(_provider.All.Count >= 10);
        Assert.Equal("fixtures", _provider.Name);
        Assert.All(_provider.All, m => Assert.True(m.TmdbId > 0 && m.Title.Length > 0));
    }

    [Fact]
    public async Task Finds_by_tmdb_and_imdb_id()
    {
        var byId = await _provider.GetMovieAsync(603, CancellationToken.None);
        var byImdb = await _provider.GetMovieByImdbIdAsync("tt0133093", CancellationToken.None);

        Assert.Equal("The Matrix", byId!.Title);
        Assert.Equal(MovieStatus.Released, byId.Status);
        Assert.Equal(603, byImdb!.TmdbId);
        Assert.Null(await _provider.GetMovieAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Searches_titles_original_titles_and_alternative_titles_without_accents()
    {
        var matrix = await _provider.SearchAsync("matrix", null, CancellationToken.None);
        var amelie = await _provider.SearchAsync("amelie", null, CancellationToken.None);
        var korean = await _provider.SearchAsync("Gisaengchung", null, CancellationToken.None);
        var byYear = await _provider.SearchAsync("matrix", 2003, CancellationToken.None);

        Assert.Equal([603, 604], matrix.Select(m => m.TmdbId));
        Assert.Equal(194, Assert.Single(amelie).TmdbId);
        Assert.Equal(496243, Assert.Single(korean).TmdbId);
        Assert.Equal([604], byYear.Select(m => m.TmdbId));
        Assert.Empty(await _provider.SearchAsync("no such film", null, CancellationToken.None));
    }
}
