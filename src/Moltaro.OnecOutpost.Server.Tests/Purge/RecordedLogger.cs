using Microsoft.Extensions.Logging;

namespace Moltaro.OnecOutpost.Server.Tests.Purge;

// A logger of a RecordedLog for one category: every entry, at every level, goes to the log.
internal sealed class RecordedLogger(RecordedLog log, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        log.Add(category, logLevel, exception);
    }
}
