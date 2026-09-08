using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Movies;

public sealed class MovieTag
{
    public int MovieId { get; set; }

    public Movie Movie { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
