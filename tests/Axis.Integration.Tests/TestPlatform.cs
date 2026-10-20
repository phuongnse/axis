using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;

namespace Axis.Integration.Tests;

/// <summary>
/// Gives test servers the platform settings every environment supplies: the platform connection string, the
/// platform schema connection string and the secret key.
/// </summary>
internal static class TestPlatform
{
    /// <summary>32 zero bytes in base64. It is a test key only.</summary>
    public const string SecretKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>
    /// Sets <c>ConnectionStrings:PlatformSchema</c> to <paramref name="schemaConnectionString"/>,
    /// <c>ConnectionStrings:Platform</c> to the same database on the runtime role, and the secret key.
    /// A test that needs another value sets it after this call.
    /// </summary>
    public static IWebHostBuilder UseTestPlatform(this IWebHostBuilder builder, string schemaConnectionString)
    {
        builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(schemaConnectionString));
        builder.UseSetting("ConnectionStrings:PlatformSchema", schemaConnectionString);
        builder.UseSetting(TenantSecretCipher.KeySetting, SecretKey);
        return builder;
    }
}
