namespace Bigarr.Api.Tests.Indexers;

/// <summary>A clock the test moves by hand. Timers still run on the real clock, which the gateway tests avoid with a zero rate limit.</summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
