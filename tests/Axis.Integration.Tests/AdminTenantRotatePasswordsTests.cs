using System.Net;
using Axis.Admin;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminTenantRotatePasswordsTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string SecretBinding = "PurchasingSystemToken";
    private const string Departments = "/api/apps/PurchaseRequests/entities/Department/records";
    private const string PurchaseRequests = "/api/apps/PurchaseRequests/entities/PurchaseRequest/records";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static readonly Dictionary<string, string?> _directory = new() { ["Tenancy:Source"] = "directory" };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rotated_tenant_is_served_after_the_next_refresh_and_the_old_runtime_password_is_refused()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateServedTenantAsync(scenario);
        var logs = new LogCollector();
        await using var factory = CreateFactory(scenario.Platform, logs);
        using var client = factory.CreateClient().SignedInAs("requester");
        await AssertStatusAsync(client, id, HttpStatusCode.OK);
        var before = await scenario.RowAsync(id);
        var oldRuntime = Decrypt(before, TenantSecretFields.RuntimePassword, before.RuntimePassword);

        var result = await Rotate(scenario, id);
        await RefreshAsync(factory);

        Assert.Equal(0, result.Exit);
        await AssertStatusAsync(client, id, HttpStatusCode.OK);
        using var create = Request(
            HttpMethod.Post,
            PurchaseRequests,
            Host(id),
            $$"""{ "values": { "title": "Chairs", "department": "{{SampleAppFixture.Operations:D}}", "status": "draft", "lineItems": [{ "description": "Chair", "quantity": 4, "unitPrice": 50 }] } }""");
        using var created = await client.SendAsync(create, CancellationToken);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync(CancellationToken));
        var refused = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(NoPool(scenario.TenantConnectionString(id, before.RuntimeUser, oldRuntime)));
            await connection.OpenAsync(CancellationToken);
        });
        Assert.Equal(PostgresErrorCodes.InvalidPassword, refused.SqlState);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task The_new_passwords_are_stored_encrypted_and_differ_from_the_old_and_no_output_log_or_record_holds_one()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        var logs = new LogCollector();
        await using var factory = CreateFactory(scenario.Platform, logs);
        using var client = factory.CreateClient().SignedInAs("requester");
        await AssertStatusAsync(client, id, HttpStatusCode.OK, "/api/me");
        var before = await scenario.RowAsync(id);
        var oldRuntime = Decrypt(before, TenantSecretFields.RuntimePassword, before.RuntimePassword);
        var oldSchema = Decrypt(before, TenantSecretFields.SchemaPassword, before.SchemaPassword);

        var result = await Rotate(scenario, id);
        await RefreshAsync(factory);
        await AssertStatusAsync(client, id, HttpStatusCode.OK, "/api/me");

        Assert.Equal(0, result.Exit);
        var after = await scenario.RowAsync(id);
        var newRuntime = Decrypt(after, TenantSecretFields.RuntimePassword, after.RuntimePassword);
        var newSchema = Decrypt(after, TenantSecretFields.SchemaPassword, after.SchemaPassword);
        Assert.NotEqual(oldRuntime, newRuntime);
        Assert.NotEqual(oldSchema, newSchema);
        Assert.NotEqual(before.RuntimePassword, after.RuntimePassword);
        Assert.NotEqual(before.SchemaPassword, after.SchemaPassword);
        Assert.DoesNotContain(newRuntime, Convert.ToHexString(after.RuntimePassword), StringComparison.OrdinalIgnoreCase);

        // The new passwords open the roles and the old ones do not.
        await OpenAsync(scenario, id, after.RuntimeUser, newRuntime);
        await OpenAsync(scenario, id, after.SchemaUser, newSchema);
        await AssertRefusedAsync(scenario, id, after.RuntimeUser, oldRuntime);
        await AssertRefusedAsync(scenario, id, after.SchemaUser, oldSchema);

        var texts = new List<string>([.. result.Output, .. result.Error, .. logs.Entries]);
        texts.AddRange(await AuditTextsAsync(scenario));
        Assert.All(
            new[] { oldRuntime, oldSchema, newRuntime, newSchema },
            secret => Assert.All(texts, text => Assert.DoesNotContain(secret, text, StringComparison.Ordinal)));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_rotation_raises_the_version_by_one_for_each_role_and_writes_one_record_that_names_both_roles()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        var before = await scenario.RowAsync(id);
        var records = (await AuditRecordsAsync(scenario)).Count;

        var result = await Rotate(scenario, id);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} runtime-password ok", $"{id} schema-password ok"], result.Output);
        Assert.Equal(before.Version + 2, (await scenario.RowAsync(id)).Version);
        var all = await AuditRecordsAsync(scenario);
        var record = Assert.Single(all.Skip(records));
        Assert.Equal(("tenant rotate-passwords", id), (record.Command, record.TenantId));
        Assert.Equal([$"axis_{id}_runtime", $"axis_{id}_schema"], record.Changed);
        Assert.Equal(Environment.UserName, record.OsUser);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_role_step_that_fails_keeps_its_stored_password_and_a_rerun_completes_the_rotation()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        var before = await scenario.RowAsync(id);
        var oldRuntime = Decrypt(before, TenantSecretFields.RuntimePassword, before.RuntimePassword);
        var oldSchema = Decrypt(before, TenantSecretFields.SchemaPassword, before.SchemaPassword);

        // This admin role may create roles, but it administers neither tenant role, so ALTER ROLE is refused.
        var stranger = await scenario.NewAdminRoleAsync("CREATEDB CREATEROLE");
        var failed = await Rotate(scenario, id, stranger);

        Assert.Equal(1, failed.Exit);
        var line = Assert.Single(failed.Output, output => output.StartsWith($"{id} runtime-password failed: ", StringComparison.Ordinal));
        Assert.Contains("permission denied", line, StringComparison.Ordinal);
        Assert.DoesNotContain(failed.Output, output => output.Contains("schema-password", StringComparison.Ordinal));
        var unchanged = await scenario.RowAsync(id);
        Assert.Equal(before.RuntimePassword, unchanged.RuntimePassword);
        Assert.Equal(before.SchemaPassword, unchanged.SchemaPassword);
        Assert.Equal(before.Version, unchanged.Version);
        await OpenAsync(scenario, id, before.RuntimeUser, oldRuntime);
        await OpenAsync(scenario, id, before.SchemaUser, oldSchema);

        var rerun = await Rotate(scenario, id);

        Assert.Equal(0, rerun.Exit);
        Assert.Equal([$"{id} runtime-password ok", $"{id} schema-password ok"], rerun.Output);
        var after = await scenario.RowAsync(id);
        await OpenAsync(scenario, id, after.RuntimeUser, Decrypt(after, TenantSecretFields.RuntimePassword, after.RuntimePassword));
        await OpenAsync(scenario, id, after.SchemaUser, Decrypt(after, TenantSecretFields.SchemaPassword, after.SchemaPassword));
        await AssertRefusedAsync(scenario, id, after.RuntimeUser, oldRuntime);
        await AssertRefusedAsync(scenario, id, after.SchemaUser, oldSchema);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_schema_role_step_that_fails_leaves_the_rotated_runtime_password_and_the_old_schema_password()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        var before = await scenario.RowAsync(id);
        var oldSchema = Decrypt(before, TenantSecretFields.SchemaPassword, before.SchemaPassword);

        // A superuser makes the schema role unreachable for the admin role by removing the grants of that role to it.
        var admin = new NpgsqlConnectionStringBuilder(scenario.AdminConnection).Username!;
        await PostgreSqlServer.AdminScalarAsync(scenario.Platform, $"REVOKE \"{before.SchemaUser}\" FROM \"{admin}\" CASCADE", CancellationToken);

        var failed = await Rotate(scenario, id);

        Assert.Equal(1, failed.Exit);
        Assert.Equal($"{id} runtime-password ok", failed.Output[0]);
        Assert.StartsWith($"{id} schema-password failed: ", failed.Output[1], StringComparison.Ordinal);
        Assert.Equal(2, failed.Output.Length);
        var after = await scenario.RowAsync(id);
        Assert.NotEqual(before.RuntimePassword, after.RuntimePassword);
        Assert.Equal(before.SchemaPassword, after.SchemaPassword);
        Assert.Equal(before.Version + 1, after.Version);
        await OpenAsync(scenario, id, after.SchemaUser, oldSchema);

        await PostgreSqlServer.AdminScalarAsync(scenario.Platform, $"GRANT \"{before.SchemaUser}\" TO \"{admin}\" WITH ADMIN OPTION", CancellationToken);
        var rerun = await Rotate(scenario, id);

        Assert.Equal(0, rerun.Exit);
        await AssertRefusedAsync(scenario, id, after.SchemaUser, oldSchema);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task The_configuration_source_an_unknown_tenant_a_provisioning_tenant_and_a_missing_admin_exit_2_and_change_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        var provisioning = scenario.NewTenantId();
        using var stop = new CancellationTokenSource();
        await scenario.RunAsync(AdminScenario.CreateArgs(provisioning, Host(provisioning)), new CancelOnLineWriter(stop), stop);
        Assert.Equal("provisioning", (await scenario.RowAsync(provisioning)).Status);
        var row = await scenario.RowAsync(id);
        var snapshot = await scenario.SnapshotAsync();
        var records = (await AuditRecordsAsync(scenario)).Count;

        var configuration = await scenario.RunAsync(["tenant", "rotate-passwords", id]);
        var unknown = await Rotate(scenario, "t-unknown");
        var notReady = await Rotate(scenario, provisioning);
        var noAdmin = await Rotate(scenario, id, adminConnectionString: "");
        var usage = await scenario.RunAsync(["tenant", "rotate-passwords"], extra: _directory);

        Assert.All([configuration, unknown, notReady, noAdmin, usage], result => Assert.Equal(2, result.Exit));
        Assert.Equal(["This command needs Tenancy:Source=directory."], configuration.Error);
        Assert.Equal(["The tenant does not exist."], unknown.Error);
        Assert.Contains("still provisioning", Assert.Single(notReady.Error), StringComparison.Ordinal);
        Assert.Equal(["The setting Admin:ConnectionString is required."], noAdmin.Error);
        Assert.StartsWith("Usage:", usage.Error[0], StringComparison.Ordinal);
        Assert.All([configuration, unknown, notReady, noAdmin, usage], result => Assert.Empty(result.Output));
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await AuditRecordsAsync(scenario)).Count);
        await OpenAsync(scenario, id, row.RuntimeUser, Decrypt(row, TenantSecretFields.RuntimePassword, row.RuntimePassword));
        await OpenAsync(scenario, id, row.SchemaUser, Decrypt(row, TenantSecretFields.SchemaPassword, row.SchemaPassword));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task An_admin_role_that_is_not_suitable_exits_2_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        var snapshot = await scenario.SnapshotAsync();
        var records = (await AuditRecordsAsync(scenario)).Count;
        var weak = await scenario.NewAdminRoleAsync("NOCREATEDB");

        var result = await Rotate(scenario, id, weak);

        Assert.Equal(2, result.Exit);
        Assert.Contains("CREATEDB and CREATEROLE", Assert.Single(result.Error), StringComparison.Ordinal);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await AuditRecordsAsync(scenario)).Count);
    }

    private static Task<AdminResult> Rotate(AdminScenario scenario, string id, string? adminConnectionString = null) =>
        scenario.RunAsync(["tenant", "rotate-passwords", id], adminConnectionString: adminConnectionString, extra: _directory);

    private static string Host(string id) => $"{id}.example.test";

    private static string Decrypt(TenantRow row, string field, byte[] stored) => AdminScenario.Cipher.Decrypt(row.Id, field, stored).Reveal();

    private static WebApplicationFactory<Program> CreateFactory(string platform, LogCollector logs) =>
        AdminScenario.CreateServer(platform).WithWebHostBuilder(builder =>
        {
            // An hour, so the timer never races the refresh that the test triggers itself.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        });

    private static Task RefreshAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);

    private static async Task AssertStatusAsync(HttpClient client, string id, HttpStatusCode expected, string? path = null)
    {
        using var response = await client.SendAsync(
            Request(path ?? $"{Departments}/{SampleAppFixture.Operations:D}", Host(id)),
            CancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }

    // A tenant with the purchase request sample's bindings and the sample active.
    private static async Task<string> CreateServedTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, Host(id))).Exit);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            context.TenantBindings.AddRange(
                new TenantBindingRow { TenantId = id, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
                new TenantBindingRow
                {
                    TenantId = id,
                    Name = SecretBinding,
                    Kind = "secret",
                    SecretValue = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.Binding(SecretBinding), "binding-secret-value"),
                });
            await context.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", id])).Exit);
        return id;
    }

    private static async Task OpenAsync(AdminScenario scenario, string id, string user, string password)
    {
        await using var connection = new NpgsqlConnection(NoPool(scenario.TenantConnectionString(id, user, password)));
        await connection.OpenAsync(CancellationToken);
    }

    // A pooled connection that opened with the old password would hide that PostgreSQL now refuses it.
    private static string NoPool(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    private static async Task AssertRefusedAsync(AdminScenario scenario, string id, string user, string password)
    {
        var refused = await Assert.ThrowsAsync<PostgresException>(() => OpenAsync(scenario, id, user, password));
        Assert.Equal(PostgresErrorCodes.InvalidPassword, refused.SqlState);
    }

    private static async Task<List<string>> AuditTextsAsync(AdminScenario scenario) =>
        [.. (await AuditRecordsAsync(scenario)).Select(record => record.Text)];

    private static async Task<List<AuditRow>> AuditRecordsAsync(AdminScenario scenario)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, os_user, changed, t::text FROM platform.audit_records t ORDER BY occurred_at, id",
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<AuditRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<string[]>(3),
                reader.GetString(4)));
        }

        return rows;
    }

    private sealed record AuditRow(string Command, string? TenantId, string OsUser, string[] Changed, string Text);
}
