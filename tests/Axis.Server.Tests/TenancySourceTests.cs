using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class TenancySourceTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    [Fact]
    public async Task An_unknown_source_stops_the_start_naming_the_setting()
    {
        await using var factory = CreateFactory(("Tenancy:Source", "other"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Tenancy:Source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_directory_source_without_a_platform_schema_connection_string_stops_the_start_naming_it()
    {
        await using var factory = CreateFactory(("Tenancy:Source", "directory"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("PlatformSchema", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_directory_source_with_one_user_for_both_platform_connection_strings_stops_the_start()
    {
        await using var factory = CreateFactory(
            ("Tenancy:Source", "directory"),
            ("ConnectionStrings:PlatformSchema", UnreachableDatabase));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("same user", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_directory_source_with_an_ActivateOnStartup_folder_stops_the_start_naming_it()
    {
        await using var factory = CreateFactory(
            ("Tenancy:Source", "directory"),
            ("ConnectionStrings:PlatformSchema", UnreachableDatabase.Replace("Username=axis", "Username=axis_schema", StringComparison.Ordinal)),
            ("Platform:SecretKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="),
            ("ActivateOnStartup:0", "/x"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ActivateOnStartup", exception.Message, StringComparison.Ordinal);
    }

    // No ActivateOnStartup folder is set, so the check does not depend on startup activation.
    private static WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "default.example.test");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.UseTestOidc("default");
        });
}
