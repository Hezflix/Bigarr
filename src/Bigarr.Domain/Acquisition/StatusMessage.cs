namespace Bigarr.Domain.Acquisition;

public enum StatusSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>Human-readable warning or error attached to a grab (feeds the queue UI and the compat <c>statusMessages</c>).</summary>
public sealed class StatusMessage
{
    public required string Title { get; set; }

    public List<string> Messages { get; set; } = [];

    public StatusSeverity Severity { get; set; } = StatusSeverity.Warning;
}
