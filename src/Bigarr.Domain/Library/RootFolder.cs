namespace Bigarr.Domain.Library;

public sealed class RootFolder
{
    public int Id { get; set; }

    public required string Path { get; set; }

    /// <summary>Films or series; the roots that existed before series came in are film roots.</summary>
    public MediaKind Kind { get; set; } = MediaKind.Movies;
}
