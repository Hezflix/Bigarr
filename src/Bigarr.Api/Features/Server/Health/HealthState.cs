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

    /// <summary>The same issue from one check to the next: its source and its wording.</summary>
    public static bool Same(string source, string message, HealthIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return string.Equals(source, issue.Source, StringComparison.Ordinal) && string.Equals(message, issue.Message, StringComparison.Ordinal);
    }
}
