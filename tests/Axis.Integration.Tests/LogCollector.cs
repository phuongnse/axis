using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>Collects the formatted message of every log entry, with the exception text when there is one.</summary>
internal sealed class LogCollector : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(_entries);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : $" {exception}")}");
    }
}
