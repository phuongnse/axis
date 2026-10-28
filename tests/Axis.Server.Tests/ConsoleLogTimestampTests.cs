using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Axis.Server.Tests;

/// <summary>
/// A console log line of the server starts with a UTC timestamp in ISO 8601 form, so a line can be placed in time without the
/// container runtime's stamp. The test runs the console formatter that the server registers and writes to a string.
/// </summary>
public sealed partial class ConsoleLogTimestampTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z ")]
    private static partial Regex TimestampPattern();

    [Fact]
    public async Task A_console_entry_starts_with_a_utc_timestamp_ending_in_Z()
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
        var name = services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName
            ?? ConsoleFormatterNames.Simple;
        var formatter = services.GetServices<ConsoleFormatter>().Single(f => f.Name == name);
        using var writer = new StringWriter();

        formatter.Write(
            new LogEntry<string>(LogLevel.Warning, "Axis.Test", default, "A warning", null, (state, _) => state),
            null,
            writer);

        var line = writer.ToString().Split('\n')[0].TrimEnd('\r');
        Assert.Matches(TimestampPattern(), line);
    }
}
