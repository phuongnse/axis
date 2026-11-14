using Axis.Admin;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>activate</c> and the publisher read <c>Extensions:ApplicationPackages</c> for the compile that stores a release, see
/// docs/reference/extension-packages.md#an-applications-packages. Unset is off, and a value that is not <c>true</c> or <c>false</c>
/// is a problem that names the setting. A publisher with the setting on also needs <c>Extensions:Runners:Application</c>, the
/// host that builds the packages, and stops at start without it.
/// </summary>
public sealed class AdminApplicationPackagesSettingTests
{
    private static ReleaseSettings? Read(string? value, out string problem) =>
        AdminCommands.ReadReleaseSettings(
            new ConfigurationBuilder()
                .AddInMemoryCollection(value is null ? [] : new Dictionary<string, string?> { ["Extensions:ApplicationPackages"] = value })
                .Build(),
            TextWriter.Null,
            out problem);

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void The_release_settings_hold_the_setting(string? value, bool expected)
    {
        var settings = Read(value, out var problem);

        Assert.NotNull(settings);
        Assert.Equal(string.Empty, problem);
        Assert.Equal(expected, settings.ApplicationPackages);
    }

    [Fact]
    public void A_value_that_is_not_true_or_false_is_a_problem_that_names_the_setting()
    {
        var settings = Read("sometimes", out var problem);

        Assert.Null(settings);
        Assert.Contains("Extensions:ApplicationPackages", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("sometimes", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_publisher_with_the_setting_on_and_no_package_host_stops_with_code_2_naming_the_setting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Extensions:ApplicationPackages"] = "true" })
            .Build();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await AdminCommands.RunAsync(
            ["publisher"],
            configuration,
            TextReader.Null,
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("Extensions:Runners:Application", error.ToString(), StringComparison.Ordinal);
    }
}
