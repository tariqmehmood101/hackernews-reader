using Microsoft.Extensions.Logging;

namespace HackerNews.UnitTests.TestSupport;

/// <summary>
/// Captures what was logged so tests can assert on it. Simpler and sturdier than substituting
/// <see cref="ILogger{T}"/>, whose generic <c>Log</c> signature is awkward to match on.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }
    }

    public bool Logged(LogLevel level, string containing) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(containing, StringComparison.OrdinalIgnoreCase));

    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);
}
