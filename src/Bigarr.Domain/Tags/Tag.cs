namespace Bigarr.Domain.Tags;

/// <summary>One join table per tagged entity, never a polymorphic relation.</summary>
public sealed class Tag
{
    public int Id { get; set; }

    public required string Label { get; set; }
}
