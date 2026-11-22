using Microsoft.Extensions.Logging;

namespace Moltaro.OnecOutpost.Server.Tests.Purge;

// A logging provider that records the category, the level and the exception of every entry, for the tests of what the
// library writes in the log of its application. Its loggers may write from any thread.
internal sealed class RecordedLog : ILoggerProvider
{
    // Guards _entries.
    private readonly Lock _lock = new();

    private readonly List<(string Category, LogLevel Level, Exception? Exception)> _entries = [];

    // A copy of the entries recorded so far, in the order they were written.
    public IReadOnlyList<(string Category, LogLevel Level, Exception? Exception)> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new RecordedLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    internal void Add(string category, LogLevel level, Exception? exception)
    {
        lock (_lock)
        {
            _entries.Add((category, level, exception));
        }
    }
}
