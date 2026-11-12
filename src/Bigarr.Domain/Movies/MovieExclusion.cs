namespace Bigarr.Domain.Movies;

/// <summary>Films the user never wants added again (list imports, Seerr's <c>addImportExclusion</c>).</summary>
public sealed class MovieExclusion
{
    public int Id { get; set; }

    public int TmdbId { get; set; }

    public required string Title { get; set; }

    public int? Year { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }
}
