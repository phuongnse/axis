using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;

namespace Axis.Server.Tests;

/// <summary>
/// Gives test hosts the platform settings every environment supplies: the platform connection string, the
/// platform schema connection string and the secret key. It also removes the platform migration step, so a host
/// starts without a database.
/// </summary>
internal static class TestPlatform
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    /// <summary>The platform schema connection string. Its user differs from the user of the platform connection string.</summary>
    public const string UnreachableSchemaDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis_schema;Password=axis;Timeout=2";

    /// <summary>32 zero bytes in base64. It is a test key only.</summary>
    public const string SecretKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>
    /// Sets the platform settings. A <see langword="null"/> <paramref name="connectionString"/> leaves out
    /// <c>ConnectionStrings:Platform</c>. A test that needs another value sets it after this call.
    /// </summary>
    public static IWebHostBuilder UseTestPlatform(this IWebHostBuilder builder, string? connectionString = UnreachableDatabase)
    {
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Platform", connectionString);
        }

        builder.UseSetting("ConnectionStrings:PlatformSchema", UnreachableSchemaDatabase);
        builder.UseSetting(TenantSecretCipher.KeySetting, SecretKey);
        return builder.WithoutPlatformMigration();
    }
}
