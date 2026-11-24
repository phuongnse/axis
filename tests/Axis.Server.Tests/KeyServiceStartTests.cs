using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Server.Tests;

/// <summary>
/// Outside Development and Testing, the server stops at start when <c>KeyService:Kind</c> is not set, even with a plain
/// <c>Platform:SecretKey</c>, see <c>docs/reference/keys-and-data-protection.md#key-policy</c>. The error names the setting and never shows
/// the key. Development is not refused with the same settings. No database is needed: the host stops before it opens one.
/// </summary>
public sealed class KeyServiceStartTests
{
    [Fact]
    public void A_production_server_with_a_plain_key_and_no_key_service_stops_at_start_naming_the_kind_setting()
    {
        using var factory = CreateFactory("Production");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("KeyService:Kind", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPlatform.SecretKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_production_server_with_a_key_service_and_a_plain_key_stops_at_start_naming_the_key_setting()
    {
        using var factory = CreateFactory("Production", keyService: true, plainKey: true);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Platform:SecretKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPlatform.SecretKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_development_server_accepts_the_plain_key_with_no_key_service_and_goes_on_to_open_the_database()
    {
        using var factory = CreateFactory("Development");

        // Development activates the sample applications at start, so the host reaches the database, which is not there.
        // Reaching it shows that the key load let the plain key through.
        Assert.ThrowsAny<NpgsqlException>(() => factory.CreateClient());
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, bool keyService = false, bool plainKey = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseTestPlatform();
            if (keyService)
            {
                builder.UseTestKeyService(TestPlatform.SecretKey);
            }

            if (plainKey)
            {
                builder.UseSetting("Platform:SecretKey", TestPlatform.SecretKey);
            }

            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseTestOidc("default");
        });
}
