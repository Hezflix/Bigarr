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
    bool CurrentIsPick = false,
    /// <summary>Series: the episodes Bigarr's parser and mapper read in the title (<c>S01E05</c>, a season's range).</summary>
    string? Episodes = null);

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
/// <param name="Transient">
/// Turned down only for reasons that pass with time or with the state of the queue, never for the release itself (not
/// enough seeders yet, minimum availability not reached, a download already active): a later pass that sees the release
/// again may judge it otherwise. False on rows written before this was kept.
/// </param>
public sealed record SightingVerdict(string VersionName, bool Approved, List<string> Rejections, int CustomFormatScore, bool Transient = false);

/// <summary>
/// What a Sonarr made of a release Bigarr picked for a series: its own parse (<c>GET /api/v3/parse</c>: the series and
/// episodes it maps the title to, the quality, its custom formats and score) and the state of those episodes in Sonarr.
/// </summary>
public sealed record SonarrView(
    /// <summary>TVDB id of the Sonarr series the title maps to; null when Sonarr maps it to none.</summary>
    int? TvdbId,
    /// <summary>The episodes Sonarr maps it to, as <c>S01E05</c> or <c>S01E01–S01E10 (10 episodes)</c>.</summary>
    string? Episodes,
    string? Quality,
    List<string> CustomFormats,
    int CustomFormatScore,
    /// <summary>Every episode it maps to is monitored in Sonarr.</summary>
    bool? Monitored,
    /// <summary>How many of those episodes already have a file in Sonarr.</summary>
    int? EpisodesWithFile,
    /// <summary>Set when Sonarr could not be asked; the rest is then empty.</summary>
    string? Error = null);
