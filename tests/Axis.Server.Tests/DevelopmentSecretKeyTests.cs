using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class DevelopmentSecretKeyTests
{
    [Fact]
    public void The_development_settings_and_the_E2E_server_hold_the_same_key_and_the_cipher_accepts_it()
    {
        var root = RepositoryRoot();
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Axis.Server", "appsettings.Development.json")));
        var developmentKey = settings.RootElement.GetProperty("Platform").GetProperty("SecretKey").GetString();

        var e2eSettings = File.ReadAllText(Path.Combine(root, "tests", "e2e", "server-settings.ts"));
        var match = Regex.Match(e2eSettings, @"platformSecretKey\s*=\s*'([^']+)'");

        Assert.True(match.Success, "tests/e2e/server-settings.ts holds no platformSecretKey.");
        Assert.Equal(developmentKey, match.Groups[1].Value);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = developmentKey })
            .Build();
        var cipher = TenantSecretCipher.Load(configuration);

        Assert.Equal("value", cipher.Decrypt("default", "field", cipher.Encrypt("default", "field", "value")).Reveal());
        Assert.NotEqual(TestPlatform.SecretKey, developmentKey);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
