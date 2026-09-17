using Bigarr.Domain.Events;

namespace Bigarr.Api.Features.Server.Health;

/// <summary>One thing the health check has to say, *arr style: a source, a severity, a sentence.</summary>
public sealed record HealthIssue(string Source, HealthIssueType Type, string Message);

public sealed record HealthResponse(DateTime? CheckedAt, List<HealthIssue> Issues);
