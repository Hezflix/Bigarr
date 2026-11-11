namespace Bigarr.Domain.Shadow;

/// <summary>
/// A Sonarr Bigarr runs next to and compares itself with, the series twin of <see cref="FollowedRadarr"/>. Each pass reads
/// its history since <see cref="HistoryCheckpoint"/> (its grabs are kept as <see cref="SonarrGrab"/> rows) and brings the
/// series version named <see cref="VersionName"/> in line with its library: series added since, monitoring, profile, tags
/// and episode files, through the mapping of the first import. Bigarr only ever reads from it, and writes nothing to disk
/// for it.
/// </summary>
public sealed class FollowedSonarr
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Url { get; set; }

    /// <summary>Stored like an indexer's key; the API only ever sends it back masked.</summary>
    public required string ApiKey { get; set; }

    /// <summary>The series version this Sonarr's shows are: <c>1080p</c>, <c>4k</c>.</summary>
    public required string VersionName { get; set; }

    /// <summary>Sonarr quality profile id → Bigarr quality profile id, as the import asks for it.</summary>
    public Dictionary<int, int> ProfileMap { get; set; } = [];

    /// <summary>Sonarr root folder path → Bigarr root folder id (a series root folder).</summary>
    public Dictionary<string, int> RootFolderMap { get; set; } = [];

    /// <summary>
    /// Sonarr custom format id → Bigarr custom format id, written by the import and kept by every pass, so a format
    /// renamed in Sonarr is still the same one here.
    /// </summary>
    public Dictionary<int, int> FormatMap { get; set; } = [];

    /// <summary>
    /// The Bigarr formats this Sonarr brought in (created, or its "(Sonarr)" copies): the follow keeps their definition in
    /// line with Sonarr's. A format it only reused (same name, same definition) is never rewritten by it.
    /// </summary>
    public List<int> OwnedFormats { get; set; } = [];

    public bool ImportTags { get; set; } = true;

    public bool KeepMonitored { get; set; } = true;

    public bool Enabled { get; set; } = true;

    /// <summary>Date of the newest history line read; the next pass asks Sonarr for everything since (a little earlier, to be safe).</summary>
    public DateTime HistoryCheckpoint { get; set; }

    public DateTime? LastSyncAt { get; set; }

    /// <summary>One line about the last pass that went through.</summary>
    public string? LastSummary { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastErrorAt { get; set; }

    /// <summary>The last call of Sonarr's webhook (Settings → Connect in Sonarr): any event wakes a pass at once.</summary>
    public DateTime? LastWebhookAt { get; set; }

    /// <summary>Sonarr's <c>eventType</c> of that call: <c>SeriesAdd</c>, <c>Grab</c>, <c>Download</c>, <c>Test</c>…</summary>
    public string? LastWebhookEvent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
