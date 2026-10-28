using Axis.Admin;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// Outside Development and Testing, every admin command, <c>publisher</c> included, exits with code 2 and names the connection
/// setting that sets an SSL mode below <c>VerifyFull</c> or none. An unset <c>DOTNET_ENVIRONMENT</c> is Production. The strings
/// point at port 1, so no database is needed: the check runs before any command opens a connection.
/// </summary>
public sealed class AdminDatabaseTlsTests
{
    private const string Password = "db-p4ssw0rd";
    private const string Secure = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=" + Password + ";SSL Mode=VerifyFull";
    private const string Prefer = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=" + Password + ";SSL Mode=Prefer";
    private const string NoMode = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=" + Password;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Refusals() => new()
    {
        { AdminCommands.PlatformSetting, Prefer },
        { AdminCommands.PlatformSetting, NoMode },
        { AdminCommands.AdminSetting, Prefer },
        { AdminCommands.AdminSetting, NoMode },
        { AdminCommands.ScenarioServerSetting, Prefer },
        { AdminCommands.ScenarioServerSetting, NoMode },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_command_without_an_environment_exits_2_naming_a_connection_setting_without_full_certificate_checks(string setting, string value)
    {
        foreach (var command in new[] { new[] { "tenant", "list" }, ["publisher"], ["version"] })
        {
            var result = await RunAsync(command, new Dictionary<string, string?> { [setting] = value });

            Assert.Equal(2, result.Exit);
            Assert.Contains(setting, result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Tenant_create_names_the_admin_setting_when_only_that_one_is_weak()
    {
        var result = await RunAsync(
            ["tenant", "create", "acme", "--host", "acme.example.test", "--oidc-authority", "https://idp.example.test", "--oidc-client-id", "axis"],
            new Dictionary<string, string?>
            {
                [AdminCommands.AdminSetting] = Prefer,
                [AdminCommands.PlatformSetting] = Secure,
            });

        Assert.Equal(2, result.Exit);
        Assert.Contains(AdminCommands.AdminSetting, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminCommands.PlatformSetting, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Development_and_testing_do_not_refuse_the_same_strings(string environment)
    {
        var result = await RunAsync(
            ["version"],
            new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = environment,
                [AdminCommands.AdminSetting] = Prefer,
                [AdminCommands.PlatformSetting] = NoMode,
            });

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain("VerifyFull", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_certificate_checks_are_not_refused_in_production()
    {
        var result = await RunAsync(["version"], new Dictionary<string, string?> { [AdminCommands.PlatformSetting] = Secure });

        Assert.Equal(0, result.Exit);
    }

    private static async Task<(int Exit, string Error)> RunAsync(string[] args, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await AdminCommands.RunAsync(args, configuration, output, error, CancellationToken);
        return (exit, error.ToString());
    }
}
