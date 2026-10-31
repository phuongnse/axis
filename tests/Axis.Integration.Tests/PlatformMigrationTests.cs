using System.Net;
using Axis.Administration;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PlatformMigrationTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string PermissionDenied = "42501";
    private const string UniqueViolation = "23505";
    private const string RestrictViolation = "23001";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_server_creates_the_platform_tables_at_start(bool directory)
    {
        var platform = await StartServerAsync(directory);

        var tables = await ScalarListAsync(
            platform,
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'platform' ORDER BY table_name");

        Assert.Equal(["__platform_migrations", "audit_records", "data_protection_keys", "tenant_bindings", "tenant_data_keys", "tenant_hosts", "tenant_platform_providers", "tenants"], tables);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_platform_reader_role_can_select_but_cannot_insert_or_create(bool directory)
    {
        var platform = await StartServerAsync(directory);
        var reader = PostgreSqlServer.RuntimeConnectionString(platform);

        Assert.Equal(0L, await ScalarAsync(reader, "SELECT count(*) FROM platform.tenants"));
        await AssertSqlStateAsync(reader, InsertTenant("reader"), PermissionDenied);
        await AssertSqlStateAsync(reader, "CREATE TABLE platform.x (id int)", PermissionDenied);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_platform_reader_role_can_insert_data_protection_keys_but_nothing_else_is_writable(bool directory)
    {
        var platform = await StartServerAsync(directory);
        var reader = PostgreSqlServer.RuntimeConnectionString(platform);
        var readerRole = new NpgsqlConnectionStringBuilder(reader).Username;
        var id = Guid.NewGuid();

        await ExecuteAsync(reader, $"INSERT INTO platform.data_protection_keys (id, friendly_name, xml) VALUES ('{id}', 'key-{id}', '\\x00')");
        await AssertSqlStateAsync(reader, "UPDATE platform.data_protection_keys SET friendly_name = 'x'", PermissionDenied);
        await AssertSqlStateAsync(reader, "DELETE FROM platform.data_protection_keys", PermissionDenied);
        Assert.Equal(
            0L,
            await ScalarAsync(
                platform,
                $"""
                SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'platform' AND c.relkind IN ('r', 'p') AND c.relname <> 'data_protection_keys'
                AND has_table_privilege('{readerRole}', c.oid, 'INSERT,UPDATE,DELETE,TRUNCATE')
                """));
    }

    [Fact]
    public async Task Platform_audit_records_cannot_be_updated_deleted_or_truncated_even_by_the_owner()
    {
        var platform = await StartServerAsync(directory: true);
        await ExecuteAsync(
            platform,
            $"INSERT INTO platform.audit_records (id, occurred_at, command, os_user, machine, changed) VALUES ('{Guid.NewGuid()}', now(), 'tenant binding set', 'u', 'm', ARRAY['x'])");

        await AssertSqlStateAsync(platform, "UPDATE platform.audit_records SET command = 'x'", RestrictViolation);
        await AssertSqlStateAsync(platform, "DELETE FROM platform.audit_records", RestrictViolation);
        await AssertSqlStateAsync(platform, "TRUNCATE platform.audit_records", RestrictViolation);
        Assert.Equal(1L, await ScalarAsync(platform, "SELECT count(*) FROM platform.audit_records"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_platform_reader_role_cannot_read_platform_audit_records(bool directory)
    {
        var platform = await StartServerAsync(directory);

        await AssertSqlStateAsync(
            PostgreSqlServer.RuntimeConnectionString(platform),
            "SELECT count(*) FROM platform.audit_records",
            PermissionDenied);
    }

    [Fact]
    public async Task The_audit_migration_revokes_the_rights_that_default_privileges_gave_the_reader()
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        var readerRole = new NpgsqlConnectionStringBuilder(PostgreSqlServer.RuntimeConnectionString(platform)).Username!;
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.GetService<IMigrator>().MigrateAsync("20261020024803_AddDataProtectionKeys", TestContext.Current.CancellationToken);
        // What the grant step set on an installation from before the audit table: SELECT on every new table.
        await ExecuteAsync(platform, $"ALTER DEFAULT PRIVILEGES IN SCHEMA platform GRANT SELECT ON TABLES TO \"{readerRole}\"");

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            false,
            await ScalarAsync(
                platform,
                $"SELECT has_table_privilege('{readerRole}', 'platform.audit_records', 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')"));
    }

    [Fact]
    public async Task Writing_a_platform_audit_record_stores_the_command_tenant_user_machine_and_changed_names()
    {
        var platform = await StartServerAsync(directory: true);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);

        await PlatformAuditRecords.WriteAsync(context, "tenant binding set", "one", ["PurchasingUrl"], TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT command, tenant_id, os_user, machine, changed FROM platform.audit_records", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("tenant binding set", reader.GetString(0));
        Assert.Equal("one", reader.GetString(1));
        Assert.Equal(Environment.UserName, reader.GetString(2));
        Assert.Equal(Environment.MachineName, reader.GetString(3));
        Assert.Equal(["PurchasingUrl"], reader.GetFieldValue<string[]>(4));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_platform_sign_in_migration_turns_accounts_on_for_a_tenant_that_exists()
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.GetService<IMigrator>().MigrateAsync("20261028164050_AddTenantRealmClientSecrets", TestContext.Current.CancellationToken);
        await ExecuteAsync(platform, InsertTenant("one"));

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(true, await ScalarAsync(platform, "SELECT platform_accounts FROM platform.tenants WHERE id = 'one'"));
    }

    [Fact]
    public async Task Two_hosts_that_differ_only_in_letter_case_cannot_both_be_stored()
    {
        var platform = await StartServerAsync(directory: true);
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
        var platform = await StartServerAsync(directory: true);
        await ExecuteAsync(platform, InsertTenant("one"));
        await ExecuteAsync(platform, "INSERT INTO platform.tenant_bindings (tenant_id, name, kind, url) VALUES ('one', 'PurchasingUrl', 'url', 'http://a.test')");

        await AssertSqlStateAsync(
            platform,
            "INSERT INTO platform.tenant_bindings (tenant_id, name, kind, url) VALUES ('one', 'purchasingurl', 'url', 'http://b.test')",
            UniqueViolation);
    }

    [Fact]
    public async Task A_tenant_key_needs_a_known_kind_a_number_from_1_and_a_tenant_and_leaves_with_its_tenant()
    {
        var platform = await StartServerAsync(directory: true);
        await ExecuteAsync(platform, InsertTenant("one"));
        await ExecuteAsync(platform, "INSERT INTO platform.tenant_data_keys (tenant_id, kind, number, key) VALUES ('one', 'data', 1, '\\x00')");

        await AssertSqlStateAsync(platform, "INSERT INTO platform.tenant_data_keys (tenant_id, kind, number, key) VALUES ('one', 'data', 1, '\\x00')", UniqueViolation);
        await AssertSqlStateAsync(platform, "INSERT INTO platform.tenant_data_keys (tenant_id, kind, number, key) VALUES ('one', 'other', 1, '\\x00')", PostgresErrorCodes.CheckViolation);
        await AssertSqlStateAsync(platform, "INSERT INTO platform.tenant_data_keys (tenant_id, kind, number, key) VALUES ('one', 'lookup', 0, '\\x00')", PostgresErrorCodes.CheckViolation);
        await AssertSqlStateAsync(platform, "INSERT INTO platform.tenant_data_keys (tenant_id, kind, number, key) VALUES ('two', 'lookup', 1, '\\x00')", PostgresErrorCodes.ForeignKeyViolation);

        await ExecuteAsync(platform, "DELETE FROM platform.tenants WHERE id = 'one'");

        Assert.Equal(0L, await ScalarAsync(platform, "SELECT count(*) FROM platform.tenant_data_keys"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Without_a_secret_key_the_server_start_fails_naming_the_setting(bool directory)
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(platform, directory, (TenantSecretCipher.KeySetting, ""));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Platform:SecretKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Under_configuration_without_a_platform_schema_connection_string_the_server_start_fails_naming_it()
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(platform, directory: false, ("ConnectionStrings:PlatformSchema", ""));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("PlatformSchema", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Under_configuration_the_platform_tables_exist_before_the_next_service_starts()
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        var probe = new TablesProbe(platform);
        await using var factory = CreateFactory(platform, directory: false).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // Lifecycle services run StartingAsync in registration order, and startup activation is one of them.
                // The probe sits right behind the migration, so it sees the tables only when the migration ran in
                // StartingAsync, before startup activation.
                var index = services.ToList().FindIndex(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PlatformMigrationStart));
                Assert.True(index >= 0);
                services.Insert(index + 1, ServiceDescriptor.Singleton<IHostedService>(probe));
            }));
        using var client = factory.CreateClient();

        Assert.Equal(["__platform_migrations", "audit_records", "data_protection_keys", "tenant_bindings", "tenant_data_keys", "tenant_hosts", "tenant_platform_providers", "tenants"], probe.Tables);
    }

    private async Task<string> StartServerAsync(bool directory)
    {
        var platform = await database.CreateDatabaseAsync("platform", TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(platform, directory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return platform;
    }

    // The tenant stays on the class database. Only the platform database is a separate one.
    private WebApplicationFactory<Program> CreateFactory(
        string platformSchemaConnectionString,
        bool directory,
        params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platformSchemaConnectionString);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            if (directory)
            {
                builder.UseSetting("Tenancy:Source", "directory");
            }

            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseTestOidc("default");
        });

    private sealed class TablesProbe(string platformConnectionString) : IHostedLifecycleService
    {
        public List<string> Tables { get; private set; } = [];

        public async Task StartingAsync(CancellationToken cancellationToken) =>
            Tables = await ScalarListAsync(
                platformConnectionString,
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'platform' ORDER BY table_name");

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

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
