namespace Bigarr.Domain.Shadow;

/// <summary>
/// A Radarr Bigarr runs next to and compares itself with. Each pass reads its history since <see cref="HistoryCheckpoint"/>
/// (its grabs are kept as <see cref="RadarrGrab"/> rows) and brings the version named <see cref="VersionName"/> in line
/// with its library: films added since, monitoring, profile, tags and files, through the mapping of the first import.
/// Bigarr only ever reads from it, and writes nothing to disk for it.
/// </summary>
public sealed class FollowedRadarr
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Url { get; set; }

    /// <summary>Stored like an indexer's key; the API only ever sends it back masked.</summary>
    public required string ApiKey { get; set; }

    /// <summary>The version this Radarr's films are: <c>1080p</c>, <c>4k</c>.</summary>
    public required string VersionName { get; set; }

    /// <summary>Radarr quality profile id → Bigarr quality profile id, as the import asks for it.</summary>
    public Dictionary<int, int> ProfileMap { get; set; } = [];

    /// <summary>Radarr root folder path → Bigarr root folder id.</summary>
    public Dictionary<string, int> RootFolderMap { get; set; } = [];

    public bool ImportTags { get; set; } = true;

    public bool KeepMonitored { get; set; } = true;

    public bool IncludeMoviesWithoutFile { get; set; } = true;

    public bool Enabled { get; set; } = true;

    /// <summary>Date of the newest history line read; the next pass asks Radarr for everything since (Radarr includes it again).</summary>
    public DateTime HistoryCheckpoint { get; set; }

    public DateTime? LastSyncAt { get; set; }

    /// <summary>One line about the last pass that went through.</summary>
    public string? LastSummary { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastErrorAt { get; set; }

    /// <summary>The last call of Radarr's webhook (Settings → Connect in Radarr): any event wakes a pass at once.</summary>
    public DateTime? LastWebhookAt { get; set; }

    /// <summary>Radarr's <c>eventType</c> of that call: <c>MovieAdded</c>, <c>Grab</c>, <c>Download</c>, <c>Test</c>…</summary>
    public string? LastWebhookEvent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
