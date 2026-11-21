using Bigarr.Domain.Events;

namespace Bigarr.Api.Features.Server.Health;

/// <summary>One thing the health check has to say, *arr style: a source, a severity, a sentence.</summary>
/// <param name="Key">What makes it the same issue from one check to the next when its wording varies (a count, a
/// time). Null when the wording itself is stable. Never sent to clients.</param>
public sealed record HealthIssue(string Source, HealthIssueType Type, string Message, [property: System.Text.Json.Serialization.JsonIgnore] string? Key = null)
{
    /// <summary>The identity compared between checks.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Identity => Key ?? Message;
}

public sealed record HealthResponse(DateTime? CheckedAt, List<HealthIssue> Issues);
