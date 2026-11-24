using System.Security.Cryptography;
using Axis.Admin;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// Outside Development and Testing, an admin command that reads the installation key exits with code 2 when <c>KeyService:Kind</c> is not
/// set, and names the setting and never the key, see <c>docs/reference/keys-and-data-protection.md#key-policy</c>. A command that reads
/// no key, such as <c>version</c>, does not need a key service. The strings point at port 1, so no database is needed: the key loads
/// before any command opens a connection.
/// </summary>
public sealed class AdminKeyServiceTests : IDisposable
{
    private const string Connection = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=db-p4ssw0rd;SSL Mode=VerifyFull";

    private readonly string _folder = Directory.CreateTempSubdirectory("axis-admin-key-service-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_production_command_that_reads_the_key_exits_2_naming_the_kind_setting_when_there_is_no_key_service()
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var result = await RunAsync(["platform", "rekey"], Settings(plain, environment: null));

        Assert.Equal(2, result.Exit);
        Assert.Equal("The setting KeyService:Kind is required outside Development and Testing." + Environment.NewLine, result.Error);
        Assert.DoesNotContain(plain, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_production_command_with_a_key_service_and_a_plain_key_exits_2_naming_the_key_setting()
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var settings = Settings(plain, environment: null);
        WriteKeyService(settings);

        var result = await RunAsync(["platform", "rekey"], settings);

        Assert.Equal(2, result.Exit);
        Assert.Contains("Platform:SecretKey", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(plain, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_production_command_with_a_wrapped_key_gets_past_the_key_and_stops_at_the_missing_previous_key()
    {
        var settings = Settings(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), environment: null);
        var keyService = WriteKeyService(settings);
        settings[TenantSecretCipher.KeySetting] = keyService.Wrap(KeyService.InstallationKey, settings[TenantSecretCipher.KeySetting]!);

        var result = await RunAsync(["platform", "rekey"], settings);

        Assert.Equal(2, result.Exit);
        Assert.Equal("The setting Platform:PreviousSecretKey is required." + Environment.NewLine, result.Error);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task A_development_or_testing_command_accepts_a_plain_key_with_no_key_service(string environment)
    {
        var result = await RunAsync(["platform", "rekey"], Settings(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), environment));

        Assert.Equal(2, result.Exit);
        Assert.Equal("The setting Platform:PreviousSecretKey is required." + Environment.NewLine, result.Error);
    }

    [Fact]
    public async Task A_production_command_that_reads_no_key_does_not_need_a_key_service()
    {
        var result = await RunAsync(["version"], Settings(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), environment: null));

        Assert.Equal(0, result.Exit);
    }

    private static Dictionary<string, string?> Settings(string key, string? environment)
    {
        var settings = new Dictionary<string, string?>
        {
            [AdminCommands.PlatformSetting] = Connection,
            [TenantSecretCipher.KeySetting] = key,
        };
        if (environment is not null)
        {
            settings[AdminCommands.DotnetEnvironmentSetting] = environment;
        }

        return settings;
    }

    private IKeyService WriteKeyService(Dictionary<string, string?> settings)
    {
        var keyFile = Path.Combine(_folder, "key");
        File.WriteAllText(keyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");
        settings[KeyService.KindSetting] = "file";
        settings[KeyService.KeyFileSetting] = keyFile;
        return KeyService.Load(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())!;
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
