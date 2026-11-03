using System.Net;
using Axis.Admin;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class CellStartTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly (string Key, string Value)[] _cellSettings =
    [
        ("Tenancy:Source", "directory"),
        ("Cell:Id", "vn-1"),
        ("Cell:Region", "vn"),
        ("Cell:TenantDomain", "vn.example.test"),
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_server_without_a_cell_id_is_not_in_a_cell()
    {
        var platform = await database.CreateDatabaseAsync("cell_none_platform", CancellationToken);
        await using var factory = CreateFactory(platform, []);

        await AssertReadyAsync(factory);

        Assert.False(factory.Services.GetRequiredService<CellOptions>().IsCell);
    }

    [Fact]
    public async Task The_server_with_a_cell_id_and_its_settings_is_in_a_cell()
    {
        var platform = await database.CreateDatabaseAsync("cell_server_platform", CancellationToken);
        await using var factory = CreateFactory(platform, _cellSettings);

        await AssertReadyAsync(factory);

        var cell = factory.Services.GetRequiredService<CellOptions>();
        Assert.True(cell.IsCell);
        Assert.Equal("vn-1", cell.Id);
        Assert.Equal("vn", cell.Region);
        Assert.Equal("vn.example.test", cell.TenantDomain);
        Assert.Equal("axis-operators", cell.OperatorRealm);
        Assert.Equal("cell-operator", cell.OperatorRole);
    }

    [Fact]
    public async Task The_server_in_a_cell_without_a_region_stops_at_start_naming_the_setting()
    {
        var platform = await database.CreateDatabaseAsync("cell_noregion_platform", CancellationToken);
        await using var factory = CreateFactory(platform, _cellSettings.Where(setting => setting.Key != "Cell:Region").ToArray());

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Cell:Region", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_worker_starts_with_and_without_a_cell_id()
    {
        var platform = await database.CreateDatabaseAsync("cell_worker_platform", CancellationToken);
        await using (var factory = CreateFactory(platform, _cellSettings))
        {
            await AssertReadyAsync(factory);
        }

        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(CancellationToken);
            await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
        }

        await using (var worker = BuildWorker(platform, []))
        {
            await worker.StartAsync(CancellationToken);
            Assert.False(worker.Services.GetRequiredService<CellOptions>().IsCell);
            await worker.StopAsync(CancellationToken);
        }

        await using (var worker = BuildWorker(platform, _cellSettings.Where(setting => setting.Key != "Tenancy:Source").ToArray()))
        {
            await worker.StartAsync(CancellationToken);
            var cell = worker.Services.GetRequiredService<CellOptions>();
            Assert.True(cell.IsCell);
            Assert.Equal("vn-1", cell.Id);
            await worker.StopAsync(CancellationToken);
        }
    }

    [Fact]
    public async Task The_worker_in_a_cell_without_a_tenant_domain_stops_at_start_naming_the_setting()
    {
        var platform = await database.CreateDatabaseAsync("cell_worker_bad_platform", CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildWorker(platform, _cellSettings.Where(setting => setting.Key != "Cell:TenantDomain").ToArray()));

        Assert.Contains("Cell:TenantDomain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Axis_Admin_refuses_every_command_when_the_cell_settings_are_wrong()
    {
        var refused = await RunAdminAsync(_cellSettings.Where(setting => setting.Key != "Cell:Region"));
        var accepted = await RunAdminAsync(_cellSettings);

        Assert.Equal(2, refused.Exit);
        Assert.Contains("Cell:Region", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, accepted.Exit);
    }

    [Fact]
    public async Task The_server_in_a_cell_with_a_bad_runtime_database_port_stops_at_start_naming_the_setting()
    {
        var platform = await database.CreateDatabaseAsync("cell_bad_port_platform", CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(() => CreateFactory(platform, BadRuntimePort).CreateClient());

        Assert.Contains("Cell:RuntimeDatabasePort", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_worker_in_a_cell_with_a_bad_runtime_database_port_stops_at_start_naming_the_setting()
    {
        var platform = await database.CreateDatabaseAsync("cell_worker_bad_port_platform", CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(() => BuildWorker(platform, BadRuntimePort));

        Assert.Contains("Cell:RuntimeDatabasePort", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Axis_Admin_never_reads_the_runtime_database_address()
    {
        var result = await RunAdminAsync(BadRuntimePort);

        Assert.Equal(0, result.Exit);
    }

    private static (string Key, string Value)[] BadRuntimePort =>
    [
        .. _cellSettings,
        ("Cell:RuntimeDatabaseHost", "pgbouncer.invalid"),
        ("Cell:RuntimeDatabasePort", "many"),
    ];

    private static async Task<(int Exit, string Error)> RunAdminAsync(IEnumerable<(string Key, string Value)> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await AdminCommands.RunAsync(["version"], configuration, output, error, CancellationToken);
        return (exit, error.ToString());
    }

    private static async Task AssertReadyAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplication BuildWorker(string platform, (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
            ["Platform:SecretKey"] = TestPlatform.SecretKey,
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Worker:PollInterval"] = "00:00:00.05",
        });
        builder.Configuration.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));
        builder.Services.AddAxisWorker(builder.Configuration, new HostingEnvironment { EnvironmentName = Environments.Development });
        return builder.Build();
    }

    // The tenant stays on the class database. Only the platform database is a separate one.
    private WebApplicationFactory<Program> CreateFactory(string platformSchemaConnectionString, (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platformSchemaConnectionString);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseTestOidc("default");
        });
}
