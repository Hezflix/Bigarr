using Microsoft.Extensions.Logging;

namespace Bigarr.Api.Tests.Playback;

/// <summary>Keeps every line logged at Information or above, for tests that check what an operator gets to read.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (IsEnabled(logLevel))
        {
            lock (_lines)
            {
                _lines.Add(formatter(state, exception));
            }
        }
    }
}
