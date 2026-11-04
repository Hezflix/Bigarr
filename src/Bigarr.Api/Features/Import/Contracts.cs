using Bigarr.Domain.Library;

namespace Bigarr.Api.Features.Import;

public sealed record NamingTokenResponse(string Token, string Description, string Example, bool Folder);

public sealed record MediaManagementResponse(
    bool RenameFiles,
    string FileNameTemplate,
    string FolderNameTemplate,
    TransferMode TransferMode,
    string? RecycleBinPath,
    int RecycleBinRetentionDays,
    bool ImportExtraFiles,
    string ExtraFileExtensions,
    /// <summary>ffprobe was found: imported files get media info, the backfill fills the older ones.</summary>
    bool MediaInfoAvailable,
    string? FfprobePath,
    string FileNamePreview,
    string FolderNamePreview,
    List<NamingTokenResponse> Tokens);

public sealed record MediaManagementRequest(
    bool RenameFiles,
    string FileNameTemplate,
    string FolderNameTemplate,
    TransferMode TransferMode,
    string? RecycleBinPath,
    int RecycleBinRetentionDays,
    /// <summary>Null leaves the setting as it is.</summary>
    bool? ImportExtraFiles = null,
    /// <summary>Comma list (<c>srt,ass</c>); null leaves it as it is, empty imports no extra.</summary>
    string? ExtraFileExtensions = null);

public sealed record NamingPreviewRequest(string? FileNameTemplate, string? FolderNameTemplate);

public sealed record NamingPreviewResponse(string? FileName, List<string> FileErrors, string? FolderName, List<string> FolderErrors);

public sealed record BackfillResponse(bool MediaInfoAvailable, int Probed, int Remaining);

/// <summary>One version's verdict on a file offered for import; the manual import lets the user override it.</summary>
public sealed record ImportVersionOption(int VersionId, string VersionName, bool IsDefault, bool Approved, List<string> Rejections);

public sealed record ManualImportFileResponse(
    string Path,
    string RelativePath,
    string Name,
    long Size,
    bool IsSample,
    string? ParsedTitle,
    int? ParsedYear,
    int QualityId,
    string Quality,
    int RevisionVersion,
    bool IsRepack,
    string? Edition,
    string? ReleaseGroup,
    List<string> Languages,
    int? MovieId,
    string? MovieTitle,
    int? MovieYear,
    /// <summary>Why no movie was found, when none was.</summary>
    string? MatchReason,
    /// <summary>The version the import would pick on its own, when one accepts the file.</summary>
    int? VersionId,
    List<ImportVersionOption> Versions);

public sealed record ManualImportResponse(
    string Path,
    int? GrabId,
    string? ReleaseTitle,
    TransferMode DefaultTransferMode,
    List<ManualImportFileResponse> Files);

public sealed record ManualImportItemRequest(string Path, int VersionId, int? QualityId = null, int? GrabId = null);

public sealed record ManualImportRequest(List<ManualImportItemRequest> Files, TransferMode? TransferMode = null);

public sealed record ManualImportResultResponse(string Path, int VersionId, bool Imported, string? Reason, int? MediaFileId, string? DestinationPath, bool Upgraded);

public sealed record ExtraRenamePreviewResponse(int ExtraFileId, string CurrentRelativePath, string NewRelativePath);

/// <summary><see cref="Extras"/> follow the file: renamed with it when it changes.</summary>
public sealed record RenamePreviewResponse(int VersionId, string VersionName, int MediaFileId, string CurrentRelativePath, string NewRelativePath, bool Changed, List<ExtraRenamePreviewResponse> Extras);

public sealed record RenameRequest(int MovieId, List<int>? MediaFileIds = null);

public sealed record RenameResultResponse(int MediaFileId, string RelativePath, bool Renamed, string? Error);
