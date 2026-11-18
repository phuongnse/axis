using Axis.Configuration.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.PackageHost.Tests;

/// <summary>
/// The settings of the <c>connect</c> command: a missing or malformed one stops it with exit code 2 and a message that names the
/// setting, and <c>http</c> is allowed only in Development, see docs/reference/extension-packages.md#customer-hosted-runners.
/// </summary>
public sealed class ConnectCommandTests
{
    [Fact]
    public async Task A_missing_tenant_address_stops_the_command_with_exit_code_2()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var error = new StringWriter();

        var exitCode = await Run(extensions, error, development: false, ("Connect:Url", null));

        Assert.Equal(2, exitCode);
        Assert.Contains("Connect:Url", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_http_token_address_outside_development_stops_the_command_with_exit_code_2()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var error = new StringWriter();

        var exitCode = await Run(extensions, error, development: false, ("Connect:TokenUrl", "http://login.example.test/token"));

        Assert.Equal(2, exitCode);
        Assert.Contains("Connect:TokenUrl", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Connect:Url", "tenant.example.test", "Connect:Url")]
    [InlineData("Connect:Url", "http://tenant.example.test", "Connect:Url")]
    [InlineData("Connect:TokenUrl", null, "Connect:TokenUrl")]
    [InlineData("Connect:TokenUrl", "not an address", "Connect:TokenUrl")]
    [InlineData("Connect:ClientId", null, "Connect:ClientId")]
    [InlineData("Connect:ClientSecret", "", "Connect:ClientSecret")]
    [InlineData("Extensions:Path", null, "Extensions:Path")]
    [InlineData("Extensions:Path", "/does/not/exist", "Extensions:Path")]
    [InlineData("Connect:Urls:TestUrl", null, "Connect:Urls:TestUrl")]
    [InlineData("Connect:Urls:TestUrl", "http://service.example.test", "Connect:Urls:TestUrl")]
    [InlineData("Connect:Secrets:TestToken", null, "Connect:Secrets:TestToken")]
    public async Task A_missing_or_malformed_setting_is_named(string setting, string? value, string named)
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var error = new StringWriter();

        var exitCode = await Run(extensions, error, development: false, (setting, value));

        Assert.Equal(2, exitCode);
        Assert.Contains(named, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_folder_with_no_package_stops_the_command_and_names_the_extensions_setting()
    {
        using var extensions = new TemporaryFolder();
        var error = new StringWriter();

        var exitCode = await Run(extensions, error, development: false);

        Assert.Equal(2, exitCode);
        Assert.Contains("Extensions:Path", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_package_that_does_not_load_stops_the_command_and_names_the_extensions_setting()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0").EditManifest("1.0.0", manifest => manifest["id"] = "other-package");
        var error = new StringWriter();

        var exitCode = await Run(extensions, error, development: false);

        Assert.Equal(2, exitCode);
        Assert.Contains("Extensions:Path", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_addresses_are_allowed_in_development()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var error = new StringWriter();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var exitCode = await Run(
            extensions,
            error,
            development: true,
            cancelled.Token,
            ("Connect:Url", "http://tenant.example.test"),
            ("Connect:TokenUrl", "http://login.example.test/token"),
            ("Connect:Urls:TestUrl", "http://service.example.test"));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
    }

    private static Task<int> Run(TemporaryFolder extensions, StringWriter error, bool development, params (string Key, string? Value)[] changes) =>
        Run(extensions, error, development, CancellationToken.None, changes);

    private static Task<int> Run(
        TemporaryFolder extensions, StringWriter error, bool development, CancellationToken cancellationToken, params (string Key, string? Value)[] changes) =>
        ConnectCommand.RunAsync(
            ConnectTestSettings.Create(extensions.Path, changes),
            development,
            TestRunnerCommand.Create(),
            error,
            NullLogger.Instance,
            cancellationToken,
            new FakeTenant(),
            new FakeTenant(),
            TimeSpan.FromMilliseconds(50));
}

/// <summary>A valid configuration of the <c>connect</c> command for the test package, with the changes applied.</summary>
internal static class ConnectTestSettings
{
    public const string TenantHost = "tenant.example.test";
    public const string TokenHost = "login.example.test";

    public static IConfiguration Create(string extensions, params (string Key, string? Value)[] changes)
    {
        var values = new Dictionary<string, string?>
        {
            ["Connect:Url"] = $"https://{TenantHost}",
            ["Connect:TokenUrl"] = $"https://{TokenHost}/token",
            ["Connect:ClientId"] = "runner",
            ["Connect:ClientSecret"] = "runner-secret",
            ["Extensions:Path"] = extensions,
            ["Connect:Urls:TestUrl"] = "https://service.example.test/api",
            ["Connect:Secrets:TestToken"] = "service-token",
        };
        foreach (var (key, value) in changes)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
