using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The region directory's own database on a real PostgreSQL. Each test class instance has a container of its own.
/// </summary>
public sealed class DirectoryDatabaseTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private const string DirectoryDatabase = "axis_directory";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {DirectoryDatabase}", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task The_start_creates_the_directory_tables_in_the_directory_database_only()
    {
        await StartAsync();

        Assert.Equal(["cells", "held", "hosts", "regions", "tenants"], await TablesAsync(DirectoryConnectionString, "directory"));
        Assert.Empty(await TablesAsync(_container.GetConnectionString(), "directory"));
    }

    [Fact]
    public async Task A_tenant_id_cannot_exist_in_two_regions()
    {
        await StartAsync();
        await using var connection = new NpgsqlConnection(DirectoryConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connection, """
            INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES
                ('vn', 'vn.example.com', 'https://api.vn.example.com'),
                ('th', 'th.example.com', 'https://api.th.example.com');
            INSERT INTO directory.cells (id, region_code, internal_url, cap) VALUES
                ('vn-1', 'vn', 'https://vn-1.internal', 100),
                ('th-1', 'th', 'https://th-1.internal', 100);
            INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('acme', 'vn', 'vn-1');
            """);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, "INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('acme', 'th', 'th-1')"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_new_cell_starts_with_no_tenants_and_takes_new_tenants()
    {
        await StartAsync();
        await using var connection = new NpgsqlConnection(DirectoryConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connection, """
            INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES ('vn', 'vn.example.com', 'https://api.vn.example.com');
            INSERT INTO directory.cells (id, region_code, internal_url, cap) VALUES ('vn-1', 'vn', 'https://vn-1.internal', 100);
            """);

        await using var command = new NpgsqlCommand("SELECT tenant_count, takes_new_tenants FROM directory.cells WHERE id = 'vn-1'", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, reader.GetInt32(0));
        Assert.True(reader.GetBoolean(1));
    }

    private string ControlPlaneConnectionString => $"{_container.GetConnectionString()};Timeout=5;Command Timeout=5";

    private string DirectoryConnectionString =>
        new NpgsqlConnectionStringBuilder(ControlPlaneConnectionString) { Database = DirectoryDatabase, Pooling = false }.ConnectionString;

    private async Task StartAsync()
    {
        var host = await mailpit.SmtpHostAsync();
        var port = await mailpit.SmtpPortAsync();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "vn.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", ControlPlaneConnectionString);
            builder.UseSetting("ControlPlane:DirectoryConnectionString", DirectoryConnectionString);
            builder.UseSetting("ControlPlane:Dns:Provider", "InMemory");
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", host);
            builder.UseSetting("ControlPlane:Mail:Port", port.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
        });
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<IReadOnlyList<string>> TablesAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_name NOT LIKE '\\_\\_%' ORDER BY table_name", connection);
        command.Parameters.AddWithValue("schema", schema);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
