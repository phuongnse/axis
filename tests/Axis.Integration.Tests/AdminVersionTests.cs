using System.Reflection;
using Axis.Admin;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

public sealed class AdminVersionTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Version_prints_the_informational_version_on_one_line_and_needs_no_settings()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["version"], new ConfigurationBuilder().Build(), output, error, CancellationToken);

        var expected = typeof(AdminCommands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(0, exit);
        Assert.Equal(expected + Environment.NewLine, output.ToString());
        Assert.DoesNotContain('+', expected);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task Version_with_an_extra_argument_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(["version", "--x"], new ConfigurationBuilder().Build(), output, error, CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
        Assert.StartsWith("Usage:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("version", error.ToString(), StringComparison.Ordinal);
    }
}
