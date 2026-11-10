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

    public SeasonPackUpgrade SeasonPackUpgrade { get; set; } = SeasonPackUpgrade.All;

    /// <summary>Percent of the pack's episodes that must be upgraded in <see cref="SeasonPackUpgrade.Threshold"/> mode.</summary>
    public double SeasonPackUpgradeThreshold { get; set; } = 100;

    public static MediaManagementConfig Default() => new();
}
