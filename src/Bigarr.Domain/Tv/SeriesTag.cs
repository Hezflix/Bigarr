using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Tv;

public sealed class SeriesTag
{
    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}

public sealed class SeriesVersionTag
{
    public int VersionId { get; set; }

    public SeriesVersion Version { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
