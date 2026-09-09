using System.Text.Json.Serialization;

namespace Bigarr.Api.Infrastructure.Indexers.Prowlarr;

/// <summary>The subset of Prowlarr's <c>GET /api/v1/indexer</c> resource that the sync reads.</summary>
public sealed class ProwlarrIndexer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enable { get; set; }

    /// <summary><c>torrent</c> or <c>usenet</c>.</summary>
    public string Protocol { get; set; } = "torrent";

    public int Priority { get; set; } = 25;

    /// <summary><c>public</c>, <c>semiPrivate</c> or <c>private</c>.</summary>
    public string? Privacy { get; set; }

    public string? Implementation { get; set; }

    public bool SupportsRss { get; set; } = true;

    public bool SupportsSearch { get; set; } = true;

    public ProwlarrCapabilities? Capabilities { get; set; }

    [JsonIgnore]
    public bool IsTorrent => string.Equals(Protocol, "torrent", StringComparison.OrdinalIgnoreCase);
}

public sealed class ProwlarrCapabilities
{
    public List<string> MovieSearchParams { get; set; } = [];

    public List<ProwlarrCategory> Categories { get; set; } = [];
}

public sealed class ProwlarrCategory
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public List<ProwlarrCategory> SubCategories { get; set; } = [];
}

public sealed class ProwlarrSystemStatus
{
    public string? AppName { get; set; }

    public string? Version { get; set; }
}
