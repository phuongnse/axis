using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;

namespace Axis.PackageHost.Tests;

/// <summary>
/// How <see cref="PackageIsolation.Load"/> reads <c>Extensions:Runners:Python</c>, see
/// docs/reference/extension-packages.md#package-runners.
/// </summary>
public sealed class PackageIsolationTests
{
    [Fact]
    public void The_python_runner_setting_is_read_into_a_host_and_a_port()
    {
        var isolation = Load(new Dictionary<string, string?> { [PackageIsolation.PythonRunnerSetting] = "python-host:5312" });

        Assert.Equal(("python-host", 5312), (isolation.PythonHost, isolation.PythonPort));
        Assert.Null(isolation.DotnetHost);
    }

    [Fact]
    public void An_unset_python_runner_setting_has_no_host()
    {
        var isolation = Load(new Dictionary<string, string?> { [PackageIsolation.DotnetRunnerSetting] = "dotnet-host:5310" });

        Assert.Equal((null, 0), (isolation.PythonHost, isolation.PythonPort));
        Assert.Equal(("dotnet-host", 5310), (isolation.DotnetHost, isolation.DotnetPort));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:0")]
    [InlineData("localhost:65536")]
    [InlineData(":5310")]
    public void A_python_runner_setting_that_is_not_a_host_and_a_port_is_refused_naming_the_setting(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Load(new Dictionary<string, string?> { [PackageIsolation.PythonRunnerSetting] = value }));

        Assert.Contains(PackageIsolation.PythonRunnerSetting, exception.Message, StringComparison.Ordinal);
    }

    private static PackageIsolation Load(Dictionary<string, string?> values) =>
        PackageIsolation.Load(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new HostingEnvironment { EnvironmentName = "Production" });
}
