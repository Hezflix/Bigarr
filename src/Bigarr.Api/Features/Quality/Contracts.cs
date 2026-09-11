using System.Text.Json;

namespace Bigarr.Api.Features.Quality;

public sealed record QualityProfileSummary(int Id, string Name, int Cutoff, bool UpgradeAllowed, bool IsSystem, string PreferredLanguage);

public sealed record QualityDefinitionResponse(int Id, string Name, string? Title, string Source, int Resolution, string Modifier, double? MinSize, double? PreferredSize, double? MaxSize);

/// <summary>Sizes are MB per minute of runtime; null max means unlimited.</summary>
public sealed record QualityDefinitionUpdateRequest(string? Title, double? MinSize, double? PreferredSize, double? MaxSize);

/// <summary>One entry of the ordered items list. A group has a <c>GroupId</c> (1000+) and several qualities ranked together.</summary>
public sealed record QualityProfileItemDto(int? GroupId, string? GroupName, List<int> QualityIds, bool Allowed);

public sealed record FormatScoreDto(int CustomFormatId, string CustomFormat, int Score);

public sealed record FormatScoreRequest(int CustomFormatId, int Score);

public sealed record QualityProfileResponse(
    int Id,
    string Name,
    /// <summary>Worst first: the position is the rank (§3.6).</summary>
    List<QualityProfileItemDto> Items,
    /// <summary>Quality id or group id of an allowed item.</summary>
    int Cutoff,
    bool UpgradeAllowed,
    int MinFormatScore,
    int CutoffFormatScore,
    int MinUpgradeFormatScore,
    string PreferredLanguage,
    bool IsSystem,
    List<FormatScoreDto> FormatScores,
    int VersionCount);

public sealed record QualityProfileRequest(
    string Name,
    List<QualityProfileItemDto> Items,
    int Cutoff,
    bool UpgradeAllowed = true,
    int MinFormatScore = 0,
    int CutoffFormatScore = 0,
    int MinUpgradeFormatScore = 0,
    string PreferredLanguage = "en",
    List<FormatScoreRequest>? FormatScores = null);

public sealed record CustomFormatSpecificationDto(string Name, string Implementation, bool Negate, bool Required, Dictionary<string, JsonElement> Fields);

public sealed record CustomFormatScoreDto(int QualityProfileId, string QualityProfile, int Score);

public sealed record CustomFormatResponse(
    int Id,
    string Name,
    bool IncludeWhenRenaming,
    List<CustomFormatSpecificationDto> Specifications,
    string? TrashId,
    /// <summary>The guide's suggestions by profile family, e.g. <c>default</c>; informational.</summary>
    Dictionary<string, int>? TrashScores,
    List<CustomFormatScoreDto> Scores);

public sealed record CustomFormatScoreRequest(int QualityProfileId, int Score);

/// <summary>
/// <paramref name="Scores"/> is the full list of profile scores: a profile left out or scored zero drops its score.
/// <c>null</c> leaves the scores untouched, for clients that only edit the definition.
/// </summary>
public sealed record CustomFormatRequest(string Name, bool IncludeWhenRenaming, List<CustomFormatSpecificationDto> Specifications, List<CustomFormatScoreRequest>? Scores = null);

/// <summary>A choice of a select field: the numeric value the engine compares and a display name.</summary>
public sealed record SpecificationOptionDto(int Value, string Label);

/// <summary>One field of a condition type. <c>Type</c> is <c>regex</c>, <c>number</c>, <c>select</c> or <c>boolean</c>.</summary>
public sealed record SpecificationFieldSchemaDto(string Name, string Type, bool Required, List<SpecificationOptionDto>? Options = null);

public sealed record SpecificationSchemaDto(string Implementation, List<SpecificationFieldSchemaDto> Fields);

/// <summary>What the editor needs to build a condition by hand: the known implementations and their fields.</summary>
public sealed record CustomFormatSchemaResponse(List<SpecificationSchemaDto> Specifications);

/// <summary>A draft judged before it is saved: the editor's test button.</summary>
public sealed record CustomFormatTestRequest(string Title, List<CustomFormatSpecificationDto> Specifications, string? OriginalLanguage = null, long? Size = null);

public sealed record SpecificationVerdictDto(string Name, string Implementation, bool Satisfied);

public sealed record CustomFormatTestResponse(
    bool Matches,
    string? ParsedTitle,
    int? Year,
    string Quality,
    string? Edition,
    string? ReleaseGroup,
    List<string> Languages,
    List<SpecificationVerdictDto> Specifications);

public sealed record CustomFormatImportResponse(List<CustomFormatResponse> Added, List<CustomFormatResponse> Updated, List<string> ScoresApplied);

public sealed record MatchedFormatDto(int Id, string Name);

public sealed record CustomFormatEvaluationResponse(
    string Title,
    string? ParsedTitle,
    int? Year,
    string Quality,
    string? Edition,
    string? ReleaseGroup,
    List<string> Languages,
    List<MatchedFormatDto> Formats,
    /// <summary>The score every profile would give.</summary>
    List<CustomFormatScoreDto> Scores);
