using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Server.Tests;

/// <summary>
/// Outside Development and Testing, the server stops at start when <c>ConnectionStrings:Platform</c> or
/// <c>ConnectionStrings:PlatformSchema</c> sets an SSL mode below <c>VerifyFull</c> or none. The error names the setting and never
/// shows the password. Development is not refused with the same strings. No database is needed: the host stops before it opens one.
/// </summary>
public sealed class DatabaseTlsStartTests
{
    private const string Password = "db-p4ssw0rd";

    public static TheoryData<string, string> WeakConnections() => new()
    {
        { "ConnectionStrings:Platform", "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=" + Password + ";SSL Mode=Prefer" },
        { "ConnectionStrings:Platform", "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=" + Password },
        { "ConnectionStrings:PlatformSchema", "Host=127.0.0.1;Port=1;Database=axis;Username=axis_schema;Password=" + Password + ";SSL Mode=Prefer" },
        { "ConnectionStrings:PlatformSchema", "Host=127.0.0.1;Port=1;Database=axis;Username=axis_schema;Password=" + Password },
    };

    [Theory]
    [MemberData(nameof(WeakConnections))]
    public void A_production_server_stops_at_start_naming_a_connection_string_without_full_certificate_checks(string setting, string value)
    {
        using var factory = CreateFactory("Production", setting, value);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(WeakConnections))]
    public void A_development_server_is_not_refused_and_goes_on_to_open_the_database(string setting, string value)
    {
        using var factory = CreateFactory("Development", setting, value);

        // Development activates the sample applications at start, so the host reaches the database, which is not there.
        // Reaching it shows that the check let the connection strings through. The integration tests start hosts on PostgreSQL without TLS.
        Assert.ThrowsAny<NpgsqlException>(() => factory.CreateClient());
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, string setting, string value) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseTestPlatform();
            builder.UseSetting(setting, value);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseTestOidc("default");
        });
}
