using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class SchemaConnectionStringTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task A_missing_or_blank_schema_connection_string_stops_the_start_naming_the_tenant_and_the_setting(string? value)
    {
        await using var factory = CreateFactory(("default", value));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("The schema connection configuration is invalid.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Tenant 'default' has no SchemaConnectionString.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_names_only_the_tenants_with_a_problem()
    {
        await using var factory = CreateFactory(("a", UnreachableDatabase), ("b", null));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Tenant 'b' has no SchemaConnectionString.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'a'", exception.Message, StringComparison.Ordinal);
    }

    // No ActivateOnStartup folder is set, so the check does not depend on startup activation.
    private static WebApplicationFactory<Program> CreateFactory(params (string Tenant, string? SchemaConnectionString)[] tenants) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            foreach (var (tenant, schemaConnectionString) in tenants)
            {
                builder.UseSetting($"Tenants:{tenant}:Hosts:0", $"{tenant}.example.test");
                builder.UseSetting($"Tenants:{tenant}:ConnectionString", UnreachableDatabase);
                if (schemaConnectionString is not null)
                {
                    builder.UseSetting($"Tenants:{tenant}:SchemaConnectionString", schemaConnectionString);
                }
            }

            builder.UseTestOidc(tenants.Select(tenant => tenant.Tenant).ToArray());
        });
}
