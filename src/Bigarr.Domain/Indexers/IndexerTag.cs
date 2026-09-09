using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Indexers;

public sealed class IndexerTag
{
    public int IndexerId { get; set; }

    public Indexer Indexer { get; set; } = null!;

    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
