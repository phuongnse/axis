using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Server.Tests;

/// <summary>
/// Outside Development the server stops at start when <c>Extensions:Isolation</c> is <c>inProcess</c> or any value other than
/// <c>process</c>, and the error names the setting. Development is not refused with <c>inProcess</c>. No database is needed: the host
/// stops before it opens one.
/// </summary>
public sealed class PackageIsolationStartTests
{
    [Theory]
    [InlineData("inProcess")]
    [InlineData("sandbox")]
    public void A_production_server_stops_at_start_naming_an_isolation_it_does_not_allow(string value)
    {
        using var factory = CreateFactory("Production", value);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Extensions:Isolation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_development_server_is_not_refused_with_in_process_and_goes_on_to_open_the_database()
    {
        using var factory = CreateFactory("Development", "inProcess");

        // Development activates the sample applications at start, so the host reaches the database, which is not there.
        // Reaching it shows that the check let the setting through.
        Assert.ThrowsAny<NpgsqlException>(() => factory.CreateClient());
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, string isolation) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseTestPlatform();
            builder.UseSetting("Extensions:Isolation", isolation);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseTestOidc("default");
        });
}
