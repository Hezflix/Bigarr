namespace Bigarr.Api.Features.Server.Tasks;

public sealed record ScheduledTaskResponse(
    string Name,
    string DisplayName,
    int IntervalMinutes,
    bool Enabled,
    bool Running,
    DateTime? LastRunAt,
    int? LastDurationMs,
    bool? LastSucceeded,
    string? LastSummary,
    string? LastError,
    DateTime? NextRunAt);

public sealed record TaskRunResponse(string Name, bool Success, int DurationMs, string? Summary, string? Error, DateTime RanAt);
