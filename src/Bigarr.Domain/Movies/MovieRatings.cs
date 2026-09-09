namespace Bigarr.Domain.Movies;

public sealed record RatingValue(double Value, int Votes);

public sealed class MovieRatings
{
    public RatingValue? Tmdb { get; set; }

    public RatingValue? Imdb { get; set; }
}
