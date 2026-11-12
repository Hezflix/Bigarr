namespace Bigarr.Api.Features.Server;

/// <summary>When this process started, read once from the application clock. Resolved at startup so the moment is the right one.</summary>
public sealed class ProcessStart(TimeProvider time)
{
    public DateTime At { get; } = time.GetUtcNow().UtcDateTime;
}
