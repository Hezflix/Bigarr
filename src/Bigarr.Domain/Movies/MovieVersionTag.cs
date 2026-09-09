using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Movies;

public sealed class MovieVersionTag
{
    public int VersionId { get; set; }

    public MovieVersion Version { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
