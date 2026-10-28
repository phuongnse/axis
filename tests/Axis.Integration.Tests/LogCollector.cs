using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Collects every log entry: <see cref="Entries"/> holds the formatted message with the exception text
/// when there is one, and <see cref="Records"/> holds the message with the key and value pairs of its scopes.
/// </summary>
internal sealed class LogCollector : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly ConcurrentQueue<LogRecordEntry> _records = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<string> Entries => _entries;

    public IReadOnlyCollection<LogRecordEntry> Records => _records;

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class Logger(LogCollector collector) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => collector._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            collector._entries.Enqueue($"{logLevel}: {message}{(exception is null ? "" : $" {exception}")}");

            var scope = new Dictionary<string, object?>();
            collector._scopes.ForEachScope(
                (value, values) =>
                {
                    if (value is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        foreach (var pair in pairs)
                        {
                            values[pair.Key] = pair.Value;
                        }
                    }
                    else if (value is IEnumerable<KeyValuePair<string, object?>> nullablePairs)
                    {
                        foreach (var pair in nullablePairs)
                        {
                            values[pair.Key] = pair.Value;
                        }
                    }
                },
                scope);
            collector._records.Enqueue(new LogRecordEntry(logLevel, message, scope));
        }
    }
}

/// <summary>A log message with its level and the key and value pairs of the scopes it was written in, such as <c>TraceId</c> and <c>TenantId</c>.</summary>
internal sealed record LogRecordEntry(LogLevel Level, string Message, Dictionary<string, object?> Scope);
