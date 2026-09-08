namespace Bigarr.Domain.Movies;

/// <summary>Proper / repack information, as parsed by the release parser.</summary>
public sealed class Revision
{
    public int Version { get; set; } = 1;

    public int Real { get; set; }

    public bool IsRepack { get; set; }
}
