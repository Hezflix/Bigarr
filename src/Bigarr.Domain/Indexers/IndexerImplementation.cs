namespace Bigarr.Domain.Indexers;

public enum IndexerImplementation
{
    /// <summary>Torznab endpoint, typically one exposed by Prowlarr or Jackett.</summary>
    Torznab = 1,

    /// <summary>In-process indexer that fabricates plausible releases for the movie searched. Development and tests only.</summary>
    Mock = 2,
}
