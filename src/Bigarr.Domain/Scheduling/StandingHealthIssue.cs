using Bigarr.Domain.Events;

namespace Bigarr.Domain.Scheduling;

/// <summary>
/// One issue of the last health check, kept so the next check, even after a restart, knows what was already standing:
/// only an issue that appears raises an event, only one that went away resolves.
/// </summary>
public sealed class StandingHealthIssue
{
    public int Id { get; set; }

    public required string Source { get; set; }

    public HealthIssueType Type { get; set; }

    public required string Message { get; set; }

    public DateTime RaisedAt { get; set; }
}
