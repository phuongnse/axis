using System.Net;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PlatformMigrationTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string PermissionDenied = "42501";
    private const string UniqueViolation = "23505";

    [Fact]
    public async Task The_directory_source_creates_the_platform_tables_at_start()
    {
        var platform = await StartDirectoryServerAsync();

        var tables = await ScalarListAsync(
            platform,
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'platform' ORDER BY table_name");

        Assert.Equal(["__platform_migrations", "tenant_bindings", "tenant_hosts", "tenants"], tables);
    }

    [Fact]
    public async Task The_platform_reader_role_can_select_but_cannot_insert_or_create()
    {
        var platform = await StartDirectoryServerAsync();
        var reader = PostgreSqlServer.RuntimeConnectionString(platform);

        Assert.Equal(0L, await ScalarAsync(reader, "SELECT count(*) FROM platform.tenants"));
        await AssertSqlStateAsync(reader, InsertTenant("reader"), PermissionDenied);
        await AssertSqlStateAsync(reader, "CREATE TABLE platform.x (id int)", PermissionDenied);
    }

    [Fact]
    public async Task Two_hosts_that_differ_only_in_letter_case_cannot_both_be_stored()
    {
        var platform = await StartDirectoryServerAsync();
        await ExecuteAsync(platform, InsertTenant("one"));
        await ExecuteAsync(platform, "INSERT INTO platform.tenant_hosts (tenant_id, host) VALUES ('one', 'a.example.test')");

        await AssertSqlStateAsync(
            platform,
            "INSERT INTO platform.tenant_hosts (tenant_id, host) VALUES ('one', 'A.Example.TEST')",
            UniqueViolation);
    }

    [Fact]
    public async Task Two_binding_names_of_one_tenant_that_differ_only_in_letter_case_cannot_both_be_stored()
    {
        var platform = await StartDirectoryServerAsync();
        await ExecuteAsync(platform, InsertTenant("one"));
        await ExecuteAsync(platform, "INSERT INTO platform.tenant_bindings (tenant_id, name, kind, url) VALUES ('one', 'PurchasingUrl', 'url', 'http://a.test')");

        await AssertSqlStateAsync(
            platform,
            "INSERT INTO platform.tenant_bindings (tenant_id, name, kind, url) VALUES ('one', 'purchasingurl', 'url', 'http://b.test')",
            UniqueViolation);
    }

    [Fact]
    public async Task Without_a_tenancy_source_the_server_starts_and_creates_no_platform_schema()
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(platform, directory: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await ScalarAsync(platform, "SELECT to_regnamespace('platform')::text"));
    }

    private async Task<string> StartDirectoryServerAsync()
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(platform, directory: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return platform;
    }

    // The tenant stays on the class database. Only the platform database is a separate one.
    private WebApplicationFactory<Program> CreateFactory(string platformSchemaConnectionString, bool directory) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(platformSchemaConnectionString));
            if (directory)
            {
                builder.UseSetting("Tenancy:Source", "directory");
                builder.UseSetting("ConnectionStrings:PlatformSchema", platformSchemaConnectionString);
            }

            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseTestOidc("default");
        });

    private static string InsertTenant(string id) =>
        $"""
        INSERT INTO platform.tenants (
            id, status, database_host, database_port, database_name, runtime_user, schema_user,
            runtime_password, schema_password, oidc_authority, oidc_client_id, oidc_client_secret)
        VALUES ('{id}', 'active', 'localhost', 5432, 'db', 'r', 's', '\x00', '\x00', 'http://idp.test', 'axis', '\x00')
        """;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull ? null : value;
    }

    private static async Task<List<string>> ScalarListAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task AssertSqlStateAsync(string connectionString, string sql, string sqlState)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, sql));

        Assert.Equal(sqlState, exception.SqlState);
    }
}
