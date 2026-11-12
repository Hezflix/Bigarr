namespace Bigarr.Domain.MediaServers;

public enum MediaServerKind
{
    Plex = 1,
    Jellyfin = 2,
    Emby = 3,
}

/// <summary>A path prefix as Bigarr sees it and the same folder as the media server mounts it.</summary>
public sealed record PathMapping(string From, string To);

/// <summary>
/// A Plex, Jellyfin or Emby server told about library changes: a file imported, upgraded, renamed or deleted
/// triggers a scan of its folder, so the film shows up without waiting for the server's own schedule. Phase 3
/// reuses this connection for sessions and statistics.
/// </summary>
public sealed class MediaServer
{
    private static readonly char[] Separators = ['/', '\\'];

    public int Id { get; set; }

    public required string Name { get; set; }

    public MediaServerKind Kind { get; set; } = MediaServerKind.Plex;

    public bool Enabled { get; set; } = true;

    /// <summary>Scheme, host, port and any URL base, e.g. <c>http://plex:32400</c>.</summary>
    public required string Url { get; set; }

    /// <summary>Plex token, or Jellyfin/Emby API key.</summary>
    public required string Token { get; set; }

    /// <summary>When the server mounts the library under another path than Bigarr does.</summary>
    public List<PathMapping> PathMappings { get; set; } = [];

    public DateTime? LastRefreshAt { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Plex's watch history has been compared with the plays up to here (the playback backstop's checkpoint).</summary>
    public DateTime? PlayHistoryCheckedUntil { get; set; }

    public string BaseUrl => Url.Trim().TrimEnd('/');

    /// <summary>The server has been failing since its last success.</summary>
    public bool IsFailing => LastErrorAt is not null && (LastRefreshAt is null || LastErrorAt > LastRefreshAt);

    /// <summary>A Bigarr path as the media server knows it: the first matching mapping wins, the rest of the path keeps its shape.</summary>
    public string Translate(string path) => Map(path, PathMappings.Select(m => (m.From, m.To)));

    /// <summary>The other way: a path the media server reports (a played file) as Bigarr sees it.</summary>
    public string TranslateFromServer(string path) => Map(path, PathMappings.Select(m => (m.To, m.From)));

    private static string Map(string path, IEnumerable<(string From, string To)> mappings)
    {
        ArgumentNullException.ThrowIfNull(path);

        foreach (var mapping in mappings)
        {
            var from = mapping.From.TrimEnd(Separators);
            if (from.Length == 0 || !path.StartsWith(from, StringComparison.Ordinal))
            {
                continue;
            }

            if (path.Length != from.Length && Array.IndexOf(Separators, path[from.Length]) < 0)
            {
                continue;
            }

            var rest = path[from.Length..];
            var to = mapping.To.TrimEnd(Separators);
            rest = to.Contains('\\') ? rest.Replace('/', '\\') : rest.Replace('\\', '/');
            return to + rest;
        }

        return path;
    }
}
