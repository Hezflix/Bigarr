using Bigarr.Api.Features.Playback;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Playback;

/// <summary>Which series an episode's show is: TVDB first, then TMDB, then IMDb; the season's poster when the series has none.</summary>
public sealed class SeriesArtworkTests
{
    private static MediaImage Poster(string name) => new(ImageType.Poster, $"https://image.tmdb.org/t/p/original/{name}.jpg");

    [Fact]
    public void The_tvdb_id_wins_then_tmdb_then_imdb_and_an_unknown_show_gets_nothing()
    {
        var artwork = new SeriesArtwork(
        [
            new SeriesArtwork.Entry(100, null, null, [Poster("by-tvdb")], []),
            new SeriesArtwork.Entry(null, 200, null, [Poster("by-tmdb")], []),
            new SeriesArtwork.Entry(null, null, "tt0000300", [Poster("by-imdb"), new MediaImage(ImageType.Fanart, "https://image.tmdb.org/t/p/original/fanart.jpg")], []),
        ]);

        // Ids pointing at two series: TVDB's decides.
        Assert.EndsWith("by-tvdb.jpg", artwork.For(new PlaybackService.ShowIds(100, 200, "tt0000300"), 1).Poster);
        Assert.EndsWith("by-tmdb.jpg", artwork.For(new PlaybackService.ShowIds(999, 200, "tt0000300"), 1).Poster);
        var byImdb = artwork.For(new PlaybackService.ShowIds(null, null, "tt0000300"), 1);
        Assert.EndsWith("by-imdb.jpg", byImdb.Poster);
        Assert.EndsWith("fanart.jpg", byImdb.Fanart);
        Assert.Equal(PlaybackService.Artwork.None, artwork.For(new PlaybackService.ShowIds(999, 998, "tt9999999"), 1));
    }

    [Fact]
    public void A_series_without_a_poster_falls_back_to_the_season_poster()
    {
        var artwork = new SeriesArtwork([new SeriesArtwork.Entry(100, null, null, [], [new SeriesArtwork.SeasonImages(2, [Poster("season-2")])])]);
        Assert.EndsWith("season-2.jpg", artwork.For(new PlaybackService.ShowIds(100, null, null), 2).Poster);
        Assert.Null(artwork.For(new PlaybackService.ShowIds(100, null, null), 3).Poster);
    }
}
