using Bigarr.Domain.Scheduling;

namespace Bigarr.Api.Features.Server.Health;

/// <summary>The last health check, in memory, for the pages. What was standing before it is in the database (<c>StandingHealthIssues</c>).</summary>
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

    /// <summary>The issues of the check that just committed. The diff with the one before is made on the stored baseline, not here.</summary>
    public void Replace(List<HealthIssue> issues, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(issues);

        lock (_lock)
        {
            _issues = [.. issues];
            CheckedAt = now;
        }
    }

    /// <summary>The same issue from one check to the next: its source and its key, or its wording when it has no key.</summary>
    public static bool Same(StandingHealthIssue standing, HealthIssue issue)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(issue);
        return string.Equals(standing.Source, issue.Source, StringComparison.Ordinal) && string.Equals(standing.Key ?? standing.Message, issue.Identity, StringComparison.Ordinal);
    }
}
