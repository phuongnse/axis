using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Axis.Server.Tests;

/// <summary>
/// A value with a line break, logged through a message template, cannot start a new console log entry. The test runs
/// the console formatter that the server registers and writes to a string, so it does not capture the process console.
/// </summary>
public sealed partial class LogInjectionTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    [Theory]
    [InlineData("Desk\nfail: Axis.Forged[0]")]
    [InlineData("Desk\r\nfail: Axis.Forged[0]")]
    public async Task A_value_with_a_line_break_does_not_start_a_new_log_entry(string value)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            builder.UseTestOidc("default");
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
        });
        var services = factory.Services;
        Assert.Contains(services.GetServices<ILoggerProvider>(), provider => provider is ConsoleLoggerProvider);
        var name = services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName
            ?? ConsoleFormatterNames.Simple;
        var formatter = services.GetServices<ConsoleFormatter>().Single(f => f.Name == name);
        using var writer = new StringWriter();

        Failed(new FormatterLogger(formatter, writer), value);

        var lines = writer.ToString().Split('\n').SkipLast(1).Select(line => line.TrimEnd('\r')).ToList();
        Assert.Contains(lines, line => line.Contains("Axis.Forged", StringComparison.Ordinal));
        Assert.Single(lines, line => line.Length > 0 && !char.IsWhiteSpace(line[0]));
        Assert.All(
            lines.Where(line => line.Contains("Axis.Forged", StringComparison.Ordinal)),
            line => Assert.True(char.IsWhiteSpace(line[0]), $"The forged line starts a new entry: {line}"));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Record {Name} could not be saved.")]
    private static partial void Failed(ILogger logger, string name);

    private sealed class FormatterLogger(ConsoleFormatter formatter, TextWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatterFunction) =>
            formatter.Write(new LogEntry<TState>(logLevel, "Axis.Test", eventId, state, exception, formatterFunction), null, writer);
    }
}
