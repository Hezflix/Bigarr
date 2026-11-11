using Bigarr.Domain.Naming;

namespace Bigarr.Domain.Library;

/// <summary>How a file gets from the download client into the library (blueprint §3.7).</summary>
public enum TransferMode
{
    /// <summary>Hardlink when the two paths share a filesystem, copy otherwise. The torrent keeps seeding either way.</summary>
    HardlinkOrCopy = 1,

    /// <summary>Always copy, even when a hardlink would do.</summary>
    Copy = 2,

    /// <summary>Move the file: the torrent loses its data. Only sensible once seeding is over, or for usenet later.</summary>
    Move = 3,
}

/// <summary>
/// When a season pack is an upgrade over the files it would replace (Sonarr's media management "Season Pack Upgrade").
/// Episodes without a file always count as upgraded.
/// </summary>
public enum SeasonPackUpgrade
{
    /// <summary>Every episode of the pack must be missing or upgraded (Sonarr's default).</summary>
    All = 0,

    /// <summary>At least <c>SeasonPackUpgradeThreshold</c> percent of the episodes.</summary>
    Threshold = 1,

    /// <summary>One episode upgraded is enough, even if the others would not gain.</summary>
    Any = 2,
}

/// <summary>
/// Single row (id 1): naming templates, renaming on/off, transfer mode and recycle bin (blueprint §3.7). Read by the
/// import, the rename command, version creation and file deletion.
/// </summary>
public sealed class MediaManagementConfig
{
    public const int SingletonId = 1;

    public const string DefaultFileNameTemplate = "{Movie Title} {(Release Year)} {edition-{Edition Tags}} {[Quality Full]}";

    public const string DefaultFolderNameTemplate = "{Movie Title} {(Release Year)}";

    public const string DefaultExtraFileExtensions = "srt,ass,ssa,sub,idx,sup,vtt";

    public int Id { get; set; } = SingletonId;

    /// <summary>Off: imported files keep the name they had in the download.</summary>
    public bool RenameFiles { get; set; } = true;

    public string FileNameTemplate { get; set; } = DefaultFileNameTemplate;

    /// <summary>Applied when a version is created; the folder name is then frozen (§3.3).</summary>
    public string FolderNameTemplate { get; set; } = DefaultFolderNameTemplate;

    public TransferMode TransferMode { get; set; } = TransferMode.HardlinkOrCopy;

    /// <summary>Replaced and deleted files go here instead of being deleted. Null: delete for good.</summary>
    public string? RecycleBinPath { get; set; }

    /// <summary>Days a file stays in the recycle bin. 0 keeps everything until emptied by hand.</summary>
    public int RecycleBinRetentionDays { get; set; } = 7;

    /// <summary>Radarr's "Import Extra Files": side files of the release whose extension is listed come along with the movie.</summary>
    public bool ImportExtraFiles { get; set; } = true;

    /// <summary>Comma list without dots, lower case: <c>srt,ass</c>.</summary>
    public string ExtraFileExtensions { get; set; } = DefaultExtraFileExtensions;

    /// <summary>
    /// Radarr's "Replace Illegal Characters": on, <c>/</c> becomes <c>+</c>, <c>?</c> becomes <c>!</c> and so on; off, they are
    /// removed. Films and series have their own, as Radarr and Sonarr are configured apart.
    /// </summary>
    public bool ReplaceIllegalCharacters { get; set; } = true;

    /// <summary>Radarr's "Colon Replacement" for film names (never <see cref="ColonReplacement.Custom"/>, which Radarr has not).</summary>
    public ColonReplacement ColonReplacement { get; set; } = ColonReplacement.Smart;

    public SeasonPackUpgrade SeasonPackUpgrade { get; set; } = SeasonPackUpgrade.All;

    /// <summary>Percent of the pack's episodes that must be upgraded in <see cref="SeasonPackUpgrade.Threshold"/> mode.</summary>
    public double SeasonPackUpgradeThreshold { get; set; } = 100;

    // ---- series naming (Sonarr's media management, defaults from Sonarr v4) -------------------------------------------

    public const string DefaultSeriesFolderTemplate = "{Series TitleYear}";

    public const string DefaultSeasonFolderTemplate = "Season {season}";

    public const string DefaultSpecialsFolderName = "Specials";

    public const string DefaultStandardEpisodeTemplate = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}";

    public const string DefaultDailyEpisodeTemplate = "{Series Title} - {Air-Date} - {Episode Title} {Quality Full}";

    public const string DefaultAnimeEpisodeTemplate = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}";

    /// <summary>Off: imported episodes keep the name they had in the download (Sonarr's "Rename Episodes").</summary>
    public bool RenameEpisodes { get; set; } = true;

    /// <summary>Applied when a series version is created; the folder name is then frozen, like a film's.</summary>
    public string SeriesFolderTemplate { get; set; } = DefaultSeriesFolderTemplate;

    public string SeasonFolderTemplate { get; set; } = DefaultSeasonFolderTemplate;

    /// <summary>Season 0's folder (Sonarr's "Specials Folder Format").</summary>
    public string SpecialsFolderName { get; set; } = DefaultSpecialsFolderName;

    public string StandardEpisodeTemplate { get; set; } = DefaultStandardEpisodeTemplate;

    public string DailyEpisodeTemplate { get; set; } = DefaultDailyEpisodeTemplate;

    public string AnimeEpisodeTemplate { get; set; } = DefaultAnimeEpisodeTemplate;

    public MultiEpisodeStyle MultiEpisodeStyle { get; set; } = MultiEpisodeStyle.PrefixedRange;

    /// <summary>Sonarr's "Replace Illegal Characters", for series, season and episode names.</summary>
    public bool SeriesReplaceIllegalCharacters { get; set; } = true;

    /// <summary>Sonarr's "Colon Replacement" for series names.</summary>
    public ColonReplacement SeriesColonReplacement { get; set; } = ColonReplacement.Smart;

    /// <summary>What <see cref="ColonReplacement.Custom"/> prints for a colon in a series name.</summary>
    public string SeriesCustomColonReplacement { get; set; } = string.Empty;

    /// <summary>The cleaning of film names.</summary>
    public NameCleaning FilmNameCleaning => new(ReplaceIllegalCharacters, ColonReplacement);

    /// <summary>The cleaning of series, season and episode names.</summary>
    public NameCleaning SeriesNameCleaning => new(SeriesReplaceIllegalCharacters, SeriesColonReplacement, SeriesCustomColonReplacement);

    public static MediaManagementConfig Default() => new();
}

/// <summary>How a file holding several episodes is numbered (Sonarr's values and order).</summary>
public enum MultiEpisodeStyle
{
    /// <summary><c>S01E01-02-03</c>.</summary>
    Extend = 0,

    /// <summary><c>S01E01 - S01E02 - S01E03</c>.</summary>
    Duplicate = 1,

    /// <summary><c>S01E01E02E03</c>.</summary>
    Repeat = 2,

    /// <summary><c>S01E01-E02-E03</c>.</summary>
    Scene = 3,

    /// <summary><c>S01E01-03</c>.</summary>
    Range = 4,

    /// <summary><c>S01E01-E03</c> (Sonarr's default).</summary>
    PrefixedRange = 5,
}
