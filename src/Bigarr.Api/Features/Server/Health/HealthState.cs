namespace Bigarr.Api.Features.Server.Health;

/// <summary>The last health check, in memory, and the diff with the one before: what the events are about.</summary>
public sealed class HealthState
{
    private readonly Lock _lock = new();
    private List<HealthIssue> _issues = [];

    public DateTime? CheckedAt { get; private set; }

    public HealthResponse Snapshot()
    {
        lock (_lock)
        {
            return new HealthResponse(CheckedAt, [.. _issues]);
        }
    }

    /// <summary>Replaces the issues and returns what appeared and what went away since the previous check.</summary>
    public (List<HealthIssue> Raised, List<HealthIssue> Resolved) Update(List<HealthIssue> issues, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(issues);

        lock (_lock)
        {
            var previous = _issues;
            var raised = issues.Where(i => !previous.Any(p => Same(p, i))).ToList();
            var resolved = previous.Where(p => !issues.Any(i => Same(p, i))).ToList();
            _issues = [.. issues];
            CheckedAt = now;
            return (raised, resolved);
        }
    }

    private static bool Same(HealthIssue a, HealthIssue b) =>
        string.Equals(a.Source, b.Source, StringComparison.Ordinal) && string.Equals(a.Message, b.Message, StringComparison.Ordinal);
}
