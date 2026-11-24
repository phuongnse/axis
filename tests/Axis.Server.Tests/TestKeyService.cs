using System.Security.Cryptography;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

/// <summary>
/// Gives a test host the file key service and a wrapped installation key that a host outside Development and Testing needs, see
/// <c>docs/reference/keys-and-data-protection.md#key-policy</c>.
/// </summary>
internal static class TestKeyService
{
    // One key file for the whole run, in a folder of its own. The key is random, so a key wrapped in one run does not unwrap in another.
    private static readonly Lazy<(string KeyFile, IKeyService KeyService)> _service = new(() =>
    {
        var folder = Directory.CreateTempSubdirectory("axis-test-key-service-").FullName;
        var keyFile = Path.Combine(folder, "key");
        File.WriteAllText(keyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Directory.Delete(folder, recursive: true);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(Settings(keyFile)).Build();
        return (keyFile, KeyService.Load(settings)!);
    });

    /// <summary>
    /// The settings of the file key service and <c>Platform:SecretKey</c> set to the plain <paramref name="key"/> wrapped by it. A test that
    /// needs another value sets it after this call.
    /// </summary>
    public static Dictionary<string, string?> WrappedKeySettings(string key)
    {
        var (keyFile, keyService) = _service.Value;
        var settings = Settings(keyFile);
        settings[TenantSecretCipher.KeySetting] = keyService.Wrap(KeyService.InstallationKey, key);
        return settings;
    }

    /// <summary>
    /// Gives a web host the file key service and sets <c>Platform:SecretKey</c> to <paramref name="key"/> wrapped by it. Call it after the
    /// call that sets the platform, for a host that runs in Production.
    /// </summary>
    public static IWebHostBuilder UseTestKeyService(this IWebHostBuilder builder, string key)
    {
        foreach (var (name, value) in WrappedKeySettings(key))
        {
            builder.UseSetting(name, value);
        }

        return builder;
    }

    private static Dictionary<string, string?> Settings(string keyFile) => new()
    {
        [KeyService.KindSetting] = "file",
        [KeyService.KeyFileSetting] = keyFile,
    };
}
