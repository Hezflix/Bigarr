namespace Bigarr.Domain.Shadow;

/// <summary>
/// What Bigarr's parser and decision engine (automatic rules, the RSS ones) made of a release Radarr grabbed, for the
/// version that follows that Radarr, at the time the grab was read: the version's file was still the one before Radarr's
/// download.
/// </summary>
public sealed record BigarrVerdict(
    /// <summary>Null when the title parses and is this film; otherwise why the release would not even reach the engine.</summary>
    string? Unmatched,
    bool Approved,
    List<string> Rejections,
    int CustomFormatScore,
    List<string> CustomFormats,
    string? Quality,
    /// <summary>The version the release would go to among the film's versions; null when none accepts it.</summary>
    string? AttributedVersion,
    /// <summary>The version's file when the release was judged, and its score.</summary>
    string? CurrentQuality,
    int? CurrentScore,
    /// <summary>The "file" was Bigarr's own latest pick, made before Radarr grabbed: the file Bigarr would have had.</summary>
    bool CurrentIsPick = false);

/// <summary>
/// What a Radarr made of a release Bigarr picked: its own parse (<c>GET /api/v3/parse</c>: the film it maps the title
/// to, the quality, its custom formats and score) and the state of the film in Radarr (monitored, current file).
/// </summary>
public sealed record RadarrView(
    /// <summary>TMDB id of the Radarr film the title maps to; null when Radarr maps it to none.</summary>
    int? TmdbId,
    string? Quality,
    List<string> CustomFormats,
    int CustomFormatScore,
    bool? Monitored,
    string? FileQuality,
    int? FileScore,
    /// <summary>Set when Radarr could not be asked; the rest is then empty.</summary>
    string? Error = null);

/// <summary>One version's verdict on a release the RSS pass saw.</summary>
public sealed record SightingVerdict(string VersionName, bool Approved, List<string> Rejections, int CustomFormatScore);
