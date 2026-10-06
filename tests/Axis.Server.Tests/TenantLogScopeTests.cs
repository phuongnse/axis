using System.Collections.Concurrent;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axis.Server.Tests;

public sealed class TenantLogScopeTests
{
    private static readonly Action<ILogger, Exception?> _logInsideRequest =
        LoggerMessage.Define(LogLevel.Information, new EventId(1), "Inside the request");

    [Fact]
    public async Task Log_entries_written_while_a_request_runs_carry_the_tenant_id()
    {
        var logs = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:tenant-a:Hosts:0"] = "a.example.test",
            ["Tenants:tenant-a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });
        builder.Logging.ClearProviders().AddProvider(logs);
        builder.Services.AddTenancy(builder.Configuration);
        await using var app = builder.Build();
        app.UseTenantResolution();
        app.MapGet("/log", (ILogger<TenantLogScopeTests> logger) =>
        {
            _logInsideRequest(logger, null);
            return Results.NoContent();
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/log", UriKind.Relative));
        request.Headers.Host = "a.example.test";

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var entry = Assert.Single(logs.Entries, entry => entry.Message == "Inside the request");
        Assert.Contains(new KeyValuePair<string, object?>("TenantId", "tenant-a"), entry.Scope);
    }

    private sealed record LogEntry(string Message, IReadOnlyList<KeyValuePair<string, object?>> Scope);

    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => provider._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var scope = new List<KeyValuePair<string, object?>>();
                provider._scopes.ForEachScope(
                    (value, values) =>
                    {
                        if (value is IEnumerable<KeyValuePair<string, object>> pairs)
                        {
                            values.AddRange(pairs.Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value)));
                        }
                        else if (value is IEnumerable<KeyValuePair<string, object?>> nullablePairs)
                        {
                            values.AddRange(nullablePairs);
                        }
                    },
                    scope);
                provider.Entries.Enqueue(new LogEntry(formatter(state, exception), scope));
            }
        }
    }
}
