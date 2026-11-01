using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// After an employee submits a sick leave to the leave request sample, no text, <c>bytea</c> or JSON column that the tenant's runtime role reads holds the
/// reason, and the tenant's backup restores only with its backup key and carries the reason as the ciphertext it was stored as.
/// </summary>
public sealed class LeaveRequestReasonStorageTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private static readonly Guid _leaveRequestEntity = Guid.Parse("c8278fd1-fab6-42a2-8fb5-5e294e8eaeb3");

    private static readonly string _table = EntityNaming.QualifiedTable(EntityNaming.Table(_leaveRequestEntity));

    private static readonly string _name = $"{EntityNaming.Schema}.{EntityNaming.Table(_leaveRequestEntity)}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_submitted_sick_leave_stores_its_reason_in_no_text_bytea_or_json_column_the_runtime_role_reads()
    {
        var title = $"Sick leave {Guid.NewGuid():N}";
        var reason = $"Flu {Guid.NewGuid():N}";
        using var client = fixture.CreateClient();
        var record = await SubmitSickLeaveAsync(client, title, reason);

        await using var connection = await RuntimeConnectionAsync(fixture.TenantId);
        var byReason = await ScanAsync(connection, reason);
        var byTitle = await ScanAsync(connection, title);

        Assert.Empty(byReason.Hits);
        Assert.Contains($"{_name}.f_title", byTitle.Hits);
        Assert.Contains(byReason.Scanned, column => column == ($"{_name}.f_reason", "bytea"));
        Assert.NotNull(await ReasonAsync(connection, record));

        await fixture.Scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task The_backup_of_a_tenant_with_a_sick_leave_restores_only_with_its_key_and_keeps_the_reason_as_ciphertext()
    {
        var scenario = fixture.Scenario;
        var reason = $"Flu {Guid.NewGuid():N}";
        using var client = fixture.CreateClient();
        var record = await SubmitSickLeaveAsync(client, $"Sick leave {Guid.NewGuid():N}", reason);
        byte[] original;
        await using (var connection = await RuntimeConnectionAsync(fixture.TenantId))
        {
            original = (await ReasonAsync(connection, record))!;
        }

        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var wrong = Path.Combine(folder.Path, "wrong.key");
        await File.WriteAllTextAsync(wrong, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), CancellationToken);
        var id = scenario.NewTenantId();
        var host = $"{id}.example.test";
        var entries = await scenario.CountEntriesAsync();

        var withoutKey = await scenario.RunAsync(["tenant", "restore", file, "--tenant", id, "--host", host], path: fixture.Tools.PathSetting);
        var wrongKey = await scenario.RunAsync(["tenant", "restore", file, "--tenant", id, "--host", host, "--backup-key-file", wrong], path: fixture.Tools.PathSetting);

        Assert.Equal(2, withoutKey.Exit);
        Assert.Equal(2, wrongKey.Exit);
        Assert.Equal(entries, await scenario.CountEntriesAsync());
        Assert.False(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(0, await scenario.CountRolesAsync(id));

        var restore = await scenario.RunAsync(AdminTenantRestoreTests.RestoreArgs(file, id, host), path: fixture.Tools.PathSetting);

        Assert.Equal(0, restore.Exit);
        var originalKeys = await KeysAsync(fixture.TenantId);
        var restoredKeys = await KeysAsync(id);
        Assert.Equal(2, originalKeys.Count);
        Assert.Equal(originalKeys.Select(key => (key.Kind, key.Number)), restoredKeys.Select(key => (key.Kind, key.Number)));
        Assert.Equal(originalKeys, restoredKeys);

        byte[] restored;
        await using (var connection = await RuntimeConnectionAsync(id))
        {
            restored = (await ReasonAsync(connection, record))!;
            Assert.Empty((await ScanAsync(connection, reason)).Hits);
        }

        Assert.Equal(original, restored);
        Assert.True(restored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(reason)) < 0);

        // The associated data of the value names the tenant it was written for, so the restored key opens it under the original id.
        var settings = restoredKeys.ToDictionary(
            key => $"Tenants:{fixture.TenantId}:{(key.Kind == TenantKeyKinds.Data ? "DataKeys" : "LookupKeys")}:{key.Number}",
            key => (string?)key.Key);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var values = new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(configuration)));
        var plain = await values.DecryptTextAsync(new SensitiveValueScope(fixture.TenantId, _leaveRequestEntity, record), "reason", restored, CancellationToken);
        Assert.Equal(reason, plain);

        await scenario.AssertNoSecretsAsync();
    }

    // Creates a draft sick leave as the employee, starts its approval and returns the record id.
    private static async Task<Guid> SubmitSickLeaveAsync(HttpClient client, string title, string reason)
    {
        var department = await OperationsAsync(client);
        var body = JsonSerializer.Serialize(new
        {
            values = new
            {
                title,
                department,
                leaveType = "sick",
                firstDay = "2026-11-02",
                lastDay = "2026-11-03",
                reason,
                status = "draft",
            },
        });
        using var create = Request(HttpMethod.Post, "/api/apps/LeaveRequests/entities/LeaveRequest/records", AdminBackupFixture.Host, body);
        using var created = await client.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var id = json.RootElement.GetProperty("id").GetGuid();

        using var start = Request(
            HttpMethod.Post,
            "/api/apps/LeaveRequests/processes/LeaveRequestApproval/instances",
            AdminBackupFixture.Host,
            $$"""{ "subjectId": "{{id:D}}" }""");
        using var started = await client.SendAsync(start, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        return id;
    }

    // The id of the Operations department that the departments module seeds for the sample.
    private static async Task<Guid> OperationsAsync(HttpClient client)
    {
        using var request = Request("/api/apps/LeaveRequests/entities/departments/Department/records?pageSize=100", AdminBackupFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("values").GetProperty("name").GetString() == "Operations")
            .GetProperty("id").GetGuid();
    }

    // A connection as the runtime role of the tenant.
    private async Task<NpgsqlConnection> RuntimeConnectionAsync(string tenantId)
    {
        var scenario = fixture.Scenario;
        var row = await scenario.RowAsync(tenantId);
        var password = AdminScenario.Cipher.Decrypt(tenantId, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenantId, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private static async Task<byte[]?> ReasonAsync(NpgsqlConnection connection, Guid record)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {EntityNaming.Quote(EntityNaming.Column("reason"))} FROM {_table} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            connection);
        command.Parameters.AddWithValue("id", record);
        var value = await command.ExecuteScalarAsync(CancellationToken);
        return value is DBNull ? null : Assert.IsType<byte[]>(value);
    }

    // Looks for the UTF-8 bytes of the text in every text, varchar, bytea, json and jsonb column that the connection's role may read.
    // Returns the columns that hold it, and every column it looked at with its type, as schema.table.column.
    private static async Task<(List<string> Hits, List<(string Column, string Type)> Scanned)> ScanAsync(NpgsqlConnection connection, string text)
    {
        var columns = new List<(string Schema, string Table, string Column, string Type)>();
        await using (var list = new NpgsqlCommand(
            """
            SELECT n.nspname, c.relname, a.attname, format_type(a.atttypid, NULL)
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND a.attnum > 0 AND NOT a.attisdropped
              AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\_%'
              AND has_table_privilege(c.oid, 'SELECT')
              AND a.atttypid IN ('text'::regtype, 'varchar'::regtype, 'bpchar'::regtype, 'bytea'::regtype, 'json'::regtype, 'jsonb'::regtype)
            ORDER BY n.nspname, c.relname, a.attnum
            """,
            connection))
        await using (var reader = await list.ExecuteReaderAsync(CancellationToken))
        {
            while (await reader.ReadAsync(CancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var hits = new List<string>();
        var scanned = new List<(string, string)>();
        foreach (var (schema, table, column, type) in columns)
        {
            var name = $"{schema}.{table}.{column}";
            await using var command = type == "bytea"
                ? new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {EntityNaming.Quote(schema)}.{EntityNaming.Quote(table)} WHERE position(@bytes IN {EntityNaming.Quote(column)}) > 0)", connection)
                : new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {EntityNaming.Quote(schema)}.{EntityNaming.Quote(table)} WHERE strpos({EntityNaming.Quote(column)}::text, @text) > 0)", connection);
            if (type == "bytea")
            {
                command.Parameters.AddWithValue("bytes", bytes);
            }
            else
            {
                command.Parameters.AddWithValue("text", text);
            }

            scanned.Add((name, type));
            if ((bool)(await command.ExecuteScalarAsync(CancellationToken))!)
            {
                hits.Add(name);
            }
        }

        return (hits, scanned);
    }

    // The plain keys of a tenant in the order of kind and number, each decrypted with the tenant's own id.
    private async Task<List<(string Kind, int Number, string Key)>> KeysAsync(string id) =>
        [.. (await fixture.Scenario.KeysAsync(id)).Select(row => (
            row.Kind,
            row.Number,
            AdminScenario.Cipher.Decrypt(
                id,
                row.Kind == TenantKeyKinds.Data ? TenantSecretFields.DataKey(row.Number) : TenantSecretFields.LookupKey(row.Number),
                row.Key).Reveal()))];
}
