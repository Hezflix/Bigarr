namespace Bigarr.Api.Tests.Metadata;

/// <summary>A clock the test moves by hand. Timers never fire on their own: only the wall-clock readings advance.</summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
