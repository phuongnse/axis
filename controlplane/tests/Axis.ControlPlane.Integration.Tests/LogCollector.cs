using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>Collects every log entry with its level and formatted message.</summary>
internal sealed class LogCollector : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCollector collector) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            collector._entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
    }
}

internal sealed record LogEntry(LogLevel Level, string Message);
