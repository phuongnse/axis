using Axis.Admin;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class AdminTenantSettingTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string EnvironmentSecret = "token-from-the-environment";
    private const string InputSecret = "token-from-standard-input";

    private static readonly string[] _everyCommand =
    [
        "tenant binding list {id}",
        "tenant binding set {id} Alpha --url https://alpha.example.test",
        "tenant binding set {id} Beta --secret",
        "tenant binding remove {id} Alpha",
        "tenant host add {id} extra.example.test",
        "tenant host remove {id} {id}.example.test",
        "tenant host move {id}.example.test --to {other}",
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Url_and_secret_bindings_are_set_listed_stored_encrypted_never_printed_and_resolved_by_a_server_after_one_refresh()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var main = scenario.NewTenantId();
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, $"{main}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, $"{preview}.example.test"), withClientSecret: false)).Exit);
        var mainBefore = (await scenario.RunAsync(["tenant", "binding", "list", main], extra: AdminScenario.Directory)).Output;
        var mainSnapshot = await scenario.SnapshotAsync();

        var url = await scenario.RunAsync(["tenant", "binding", "set", preview, "Alpha", "--url", "https://alpha.example.test/api"], extra: AdminScenario.Directory);
        var environment = await scenario.RunAsync(
            ["tenant", "binding", "set", preview, "Beta", "--secret"],
            extra: Settings(("AXIS_BINDING_SECRET", EnvironmentSecret)));
        var input = await scenario.RunAsync(
            ["tenant", "binding", "set", preview, "Gamma", "--secret"],
            extra: AdminScenario.Directory,
            input: InputSecret + "\r\n");

        Assert.All([url, environment, input], result =>
        {
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Equal([$"{preview} binding ok"], result.Output);
        });

        var list = await scenario.RunAsync(["tenant", "binding", "list", preview], extra: AdminScenario.Directory);
        Assert.Equal(0, list.Exit);
        Assert.Equal(
            [
                ["name", "kind", "value"],
                ["Alpha", "url", "https://alpha.example.test/api"],
                ["Beta", "secret", "(hidden)"],
                ["Gamma", "secret", "(hidden)"],
            ],
            list.Output.Select(line => line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

        await using (var context = NewContext(scenario))
        {
            var rows = await context.TenantBindings.AsNoTracking().Where(binding => binding.TenantId == preview).ToListAsync(CancellationToken);
            var beta = rows.Single(row => row.Name == "Beta");
            var gamma = rows.Single(row => row.Name == "Gamma");
            Assert.Equal(EnvironmentSecret, AdminScenario.Cipher.Decrypt(preview, TenantSecretFields.Binding("Beta"), beta.SecretValue!).Reveal());
            Assert.Equal(InputSecret, AdminScenario.Cipher.Decrypt(preview, TenantSecretFields.Binding("Gamma"), gamma.SecretValue!).Reveal());
            Assert.All([beta, gamma], row => Assert.Null(row.Url));
            Assert.Null(rows.Single(row => row.Name == "Alpha").SecretValue);
        }

        var records = await RecordsAsync(scenario);
        Assert.Equal(
            [("tenant binding set", preview, "binding:Alpha"), ("tenant binding set", preview, "binding:Beta"), ("tenant binding set", preview, "binding:Gamma")],
            records.Where(record => record.Command == "tenant binding set").Select(record => (record.Command, record.TenantId!, Assert.Single(record.Changed))));
        Assert.All(records, record =>
        {
            Assert.DoesNotContain(EnvironmentSecret, record.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(InputSecret, record.Text, StringComparison.Ordinal);
        });

        // The main tenant is untouched.
        Assert.Equal(mainBefore, (await scenario.RunAsync(["tenant", "binding", "list", main], extra: AdminScenario.Directory)).Output);
        Assert.Equal(0, await scenario.CountBindingsAsync(main));
        Assert.Contains(mainSnapshot.Split('\n').First(line => line.StartsWith(main + "|", StringComparison.Ordinal)), (await scenario.SnapshotAsync()).Split('\n'));

        await using var server = AdminTenantRestoreTests.CreateDirectoryServer(scenario.Platform);
        await AdminTenantRestoreTests.RefreshAsync(server);
        var entry = server.Services.GetRequiredService<ITenantCatalog>().Find(preview)!;
        Assert.Equal("https://alpha.example.test/api", entry.Bindings["Alpha"].Url);
        Assert.Equal(EnvironmentSecret, entry.Bindings["Beta"].Secret!.Reveal());
        Assert.Equal(InputSecret, entry.Bindings["Gamma"].Secret!.Reveal());
        Assert.Empty(server.Services.GetRequiredService<ITenantCatalog>().Find(main)!.Bindings);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_storage_binding_is_set_and_listed_with_its_value_and_any_other_value_is_refused_and_writes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = await scenario.SnapshotAsync();
        var records = (await RecordsAsync(scenario)).Count;

        foreach (var value in new[] { "s3", "Database", "" })
        {
            var refused = await scenario.RunAsync(["tenant", "binding", "set", id, "Files", "--storage", value], extra: AdminScenario.Directory);

            Assert.Equal(2, refused.Exit);
            Assert.Empty(refused.Output);
            Assert.Equal(["The binding 'Files' is a storage binding, and its only value is 'database'."], refused.Error);
        }

        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.Equal(records, (await RecordsAsync(scenario)).Count);
        Assert.Equal(0, await scenario.CountBindingsAsync(id));

        var set = await scenario.RunAsync(["tenant", "binding", "set", id, "Files", "--storage", "database"], extra: AdminScenario.Directory);

        Assert.Equal(0, set.Exit);
        Assert.Equal([$"{id} binding ok"], set.Output);
        var list = await scenario.RunAsync(["tenant", "binding", "list", id], extra: AdminScenario.Directory);
        Assert.Equal(["name   kind     value", "Files  storage  database"], list.Output);
        var record = (await RecordsAsync(scenario))[^1];
        Assert.Equal(records + 1, (await RecordsAsync(scenario)).Count);
        Assert.Equal(["binding:Files"], record.Changed);
        Assert.Contains(UsageLines(), line => line.Contains("tenant binding set <id> <name> --storage database", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_replaced_binding_keeps_its_name_and_a_removed_binding_is_gone()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", id, "Alpha", "--url", "https://one.example.test"], extra: AdminScenario.Directory)).Exit);

        var secret = await scenario.RunAsync(["tenant", "binding", "set", id, "ALPHA", "--secret"], extra: AdminScenario.Directory, input: InputSecret);
        Assert.Equal(0, secret.Exit);

        var list = await scenario.RunAsync(["tenant", "binding", "list", id], extra: AdminScenario.Directory);
        Assert.Equal(["name   kind    value", "Alpha  secret  (hidden)"], list.Output);
        Assert.Equal(1, await scenario.CountBindingsAsync(id));

        var url = await scenario.RunAsync(["tenant", "binding", "set", id, "alpha", "--url", "https://two.example.test"], extra: AdminScenario.Directory);
        Assert.Equal(0, url.Exit);
        Assert.Equal(["name   kind  value", "Alpha  url   https://two.example.test"], (await scenario.RunAsync(["tenant", "binding", "list", id], extra: AdminScenario.Directory)).Output);

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "remove", id, "ALPHA"], extra: AdminScenario.Directory)).Exit);
        Assert.Equal(0, await scenario.CountBindingsAsync(id));
        Assert.All((await RecordsAsync(scenario)).Where(record => record.Command.StartsWith("tenant binding", StringComparison.Ordinal)), record => Assert.Equal(["binding:Alpha"], record.Changed));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_host_moves_to_another_tenant_and_the_server_then_finds_the_new_tenant_for_it()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var from = scenario.NewTenantId();
        var to = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(from, "one.example.test", "two.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(to, "three.example.test")).Exit);
        await using var server = AdminTenantRestoreTests.CreateDirectoryServer(scenario.Platform);
        await AdminTenantRestoreTests.RefreshAsync(server);
        var catalog = server.Services.GetRequiredService<ITenantCatalog>();
        Assert.Equal(from, catalog.FindByHost("one.example.test")!.Id);

        var before = await scenario.SnapshotAsync();
        var records = (await RecordsAsync(scenario)).Count;
        var unknown = await scenario.RunAsync(["tenant", "host", "move", "one.example.test", "--to", "t-unknown"], extra: AdminScenario.Directory);
        var held = await scenario.RunAsync(["tenant", "host", "add", to, "TWO.example.test"], extra: AdminScenario.Directory);
        var last = await scenario.RunAsync(["tenant", "host", "remove", to, "three.example.test"], extra: AdminScenario.Directory);
        var nobody = await scenario.RunAsync(["tenant", "host", "move", "nobody.example.test", "--to", to], extra: AdminScenario.Directory);
        var same = await scenario.RunAsync(["tenant", "host", "move", "three.example.test", "--to", to], extra: AdminScenario.Directory);

        Assert.All([unknown, held, last, nobody, same], result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
            Assert.Single(result.Error);
        });
        Assert.Equal(["The tenant does not exist."], unknown.Error);
        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.Equal(records, (await RecordsAsync(scenario)).Count);

        var move = await scenario.RunAsync(["tenant", "host", "move", "ONE.example.test", "--to", to], extra: AdminScenario.Directory);
        await AdminTenantRestoreTests.RefreshAsync(server);

        Assert.Equal(0, move.Exit);
        Assert.Equal([$"{to} host ok"], move.Output);
        Assert.Equal(to, catalog.FindByHost("one.example.test")!.Id);
        Assert.Equal(from, catalog.FindByHost("two.example.test")!.Id);
        Assert.Equal(["two.example.test"], await scenario.HostsAsync(from));
        var record = Assert.Single(await RecordsAsync(scenario), row => row.Command == "tenant host move");
        Assert.Equal(to, record.TenantId);
        Assert.Equal(["host:one.example.test", $"from:{from}"], record.Changed);

        // A move may leave the source with no hosts.
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "host", "move", "two.example.test", "--to", to], extra: AdminScenario.Directory)).Exit);
        Assert.Empty(await scenario.HostsAsync(from));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Every_command_exits_2_under_the_configuration_source_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var other = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(other, $"{other}.example.test")).Exit);
        var before = await scenario.SnapshotAsync();
        var records = (await RecordsAsync(scenario)).Count;

        foreach (var source in new IReadOnlyDictionary<string, string?>?[] { null, Settings(("Tenancy:Source", "configuration")) })
        {
            foreach (var command in _everyCommand)
            {
                var result = await scenario.RunAsync(Arguments(command, id, other), extra: source, input: InputSecret);

                Assert.Equal(2, result.Exit);
                Assert.Empty(result.Output);
                Assert.Equal(["The tenant setting commands need Tenancy:Source=directory."], result.Error);
            }
        }

        var invalid = await scenario.RunAsync(Arguments(_everyCommand[1], id, other), extra: Settings(("Tenancy:Source", "nonsense")));
        Assert.Equal(2, invalid.Exit);
        Assert.DoesNotContain("nonsense", string.Join('\n', invalid.Error), StringComparison.Ordinal);
        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.Equal(records, (await RecordsAsync(scenario)).Count);
    }

    [Fact]
    public async Task Each_change_under_the_directory_source_writes_one_record_and_raises_the_version()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var other = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(other, $"{other}.example.test")).Exit);
        var records = (await RecordsAsync(scenario)).Count;

        foreach (var (command, changed, versions) in new (string, string[], (long Id, long Other))[]
        {
            ("tenant binding set {id} Alpha --url https://alpha.example.test", ["binding:Alpha"], (1, 0)),
            ("tenant binding set {id} Beta --secret", ["binding:Beta"], (1, 0)),
            ("tenant binding remove {id} Alpha", ["binding:Alpha"], (1, 0)),
            ("tenant host add {id} extra.example.test", ["host:extra.example.test"], (1, 0)),
            ("tenant host remove {id} extra.example.test", ["host:extra.example.test"], (1, 0)),
            ("tenant host move {id}.example.test --to {other}", [$"host:{id}.example.test", $"from:{id}"], (1, 1)),
        })
        {
            var idBefore = (await scenario.RowAsync(id)).Version;
            var otherBefore = (await scenario.RowAsync(other)).Version;
            var arguments = Arguments(command, id, other);

            var result = await scenario.RunAsync(arguments, extra: AdminScenario.Directory, input: InputSecret);

            Assert.Equal(0, result.Exit);
            Assert.Equal(idBefore + versions.Id, (await scenario.RowAsync(id)).Version);
            Assert.Equal(otherBefore + versions.Other, (await scenario.RowAsync(other)).Version);
            var all = await RecordsAsync(scenario);
            Assert.Equal(++records, all.Count);
            var record = all[^1];
            Assert.Equal(string.Join(' ', arguments.Take(3)), record.Command);
            Assert.Equal(arguments[1] == "host" && arguments[2] == "move" ? other : id, record.TenantId);
            Assert.Equal(changed, record.Changed);
            Assert.DoesNotContain(InputSecret, record.Text, StringComparison.Ordinal);
        }

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_refused_command_exits_2_and_changes_nothing_and_a_secret_is_never_taken_from_an_argument()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var provisioning = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(provisioning, $"{provisioning}.example.test")).Exit);
        await scenario.SetStatusAsync(provisioning, "provisioning");
        var before = await scenario.SnapshotAsync();
        var records = (await RecordsAsync(scenario)).Count;

        var refused = new List<AdminResult>
        {
            await scenario.RunAsync(["tenant", "binding", "set", id, "Beta", "--secret", "argument-secret"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "set", id, "Beta", "--url"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "list"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "host", "move", "x.example.test", id], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "list", "t-unknown"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "set", "t-unknown", "Alpha", "--url", "https://a.example.test"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "set", id, "Alpha", "--url", "ftp://a.example.test"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "set", id, "Alpha", "--url", "not a url"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "set", id, "Beta", "--secret"], extra: AdminScenario.Directory, input: "\n"),
            await scenario.RunAsync(["tenant", "binding", "remove", id, "Missing"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "binding", "set", provisioning, "Alpha", "--url", "https://a.example.test"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "host", "add", provisioning, "new.example.test"], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "host", "move", $"{id}.example.test", "--to", provisioning], extra: AdminScenario.Directory),
            await scenario.RunAsync(["tenant", "host", "remove", id, "other.example.test"], extra: AdminScenario.Directory),
        };

        Assert.All(refused, result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
            Assert.NotEmpty(result.Error);
        });
        Assert.StartsWith("Usage:", refused[0].Error[0], StringComparison.Ordinal);
        Assert.DoesNotContain("argument-secret", string.Join('\n', refused[0].Error), StringComparison.Ordinal);
        Assert.Contains(UsageLines(), line => line.Contains("tenant binding set <id> <name> --secret", StringComparison.Ordinal));
        Assert.Contains(refused[10].Error, line => line.Contains("provisioning", StringComparison.Ordinal));
        Assert.Contains(refused[8].Error, line => line.Contains("AXIS_BINDING_SECRET", StringComparison.Ordinal));
        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.Equal(records, (await RecordsAsync(scenario)).Count);

        // A provisioning tenant's bindings can still be listed.
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "list", provisioning], extra: AdminScenario.Directory)).Exit);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_record_that_cannot_be_written_leaves_the_binding_unchanged()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        await PostgreSqlServer.AdminScalarAsync(
            scenario.Platform,
            """
            CREATE FUNCTION platform.reject_audit_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'audit is down';
            END;
            $$;
            CREATE TRIGGER audit_records_no_insert BEFORE INSERT ON platform.audit_records
                FOR EACH ROW EXECUTE FUNCTION platform.reject_audit_insert();
            """,
            CancellationToken);
        var before = await scenario.SnapshotAsync();

        var result = await scenario.RunAsync(["tenant", "binding", "set", id, "Alpha", "--url", "https://a.example.test"], extra: AdminScenario.Directory);

        Assert.Equal(1, result.Exit);
        Assert.Equal([$"{id} audit failed: audit is down"], result.Output);
        Assert.Equal(before, await scenario.SnapshotAsync());
    }

    private static string[] Arguments(string command, string id, string other) =>
        command.Replace("{id}", id, StringComparison.Ordinal).Replace("{other}", other, StringComparison.Ordinal).Split(' ');

    private static Dictionary<string, string?> Settings(params (string Name, string Value)[] settings)
    {
        var all = new Dictionary<string, string?>(AdminScenario.Directory);
        foreach (var (name, value) in settings)
        {
            all[name] = value;
        }

        return all;
    }

    private static string[] UsageLines() => AdminCommands.Usage.Split('\n').Select(line => line.Trim()).ToArray();

    private static PlatformDbContext NewContext(AdminScenario scenario) => new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);

    private static async Task<List<AuditRow>> RecordsAsync(AdminScenario scenario)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, changed, t::text FROM platform.audit_records t ORDER BY occurred_at, id",
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<AuditRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new AuditRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetFieldValue<string[]>(2), reader.GetString(3)));
        }

        return rows;
    }

    private sealed record AuditRow(string Command, string? TenantId, string[] Changed, string Text);
}
