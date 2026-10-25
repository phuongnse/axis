using System.Formats.Tar;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminTenantRestoreTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private const string SecretBinding = "PurchasingSystemToken";

    private static readonly string[] _steps =
    [
        "check", "entry", "roles", "database", "restore", "grants", "grants", "work", "role-check", "version",
    ];

    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_backup_restored_under_a_new_id_serves_the_same_records_on_its_new_host_and_leaves_the_original_tenant_as_it_was()
    {
        using var client = fixture.CreateClient();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await AdminBackupFixture.CreateRequestAsync(client));
        }

        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        var scenario = fixture.Scenario;
        await scenario.SetEmailAndLocaleClaimsAsync(fixture.TenantId, "mail", "lang");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var original = await scenario.RowAsync(fixture.TenantId);
        var originalHosts = await scenario.HostsAsync(fixture.TenantId);
        var newId = scenario.NewTenantId();
        const string newHost = "restored.example.test";

        var result = await RestoreAsync(file, newId, newHost);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_steps.Select(step => $"{newId} {step} ok"), result.Output);
        Assert.Empty(result.Error);
        var row = await scenario.RowAsync(newId);
        Assert.Equal("suspended", row.Status);
        Assert.Equal(TenantSchemaVersion.Current, row.SchemaVersion);
        Assert.Equal([newHost], await scenario.HostsAsync(newId));
        Assert.Equal(original.OidcAuthority, row.OidcAuthority);
        Assert.Equal(original.OidcClientId, row.OidcClientId);
        Assert.Equal("mail", row.ClaimEmail);
        Assert.Equal("lang", row.ClaimLocale);
        Assert.NotEqual(original.DatabaseName, row.DatabaseName);
        Assert.Equal(0, await CountSecretBindingValuesAsync(newId));
        Assert.Equal(
            ["PurchasingSystemToken", "PurchasingSystemUrl"],
            await BindingNamesAsync(newId));

        await using var server = CreateDirectoryServer(scenario.Platform);
        using var restored = server.CreateClient().SignedInAs("anna");
        await RefreshAsync(server);
        using (var held = await restored.SendAsync(Request("/api/me", newHost), CancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, held.StatusCode);
        }

        // The operator sets the secret binding, which the archive never held, and resumes the tenant.
        await scenario.SetBindingAsync(newId, SecretBinding, binding => binding.SecretValue = AdminScenario.Cipher.Encrypt(newId, TenantSecretFields.Binding(SecretBinding), "restored-token"));
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "resume", newId])).Exit);
        await RefreshAsync(server);

        using (var served = await restored.SendAsync(Request("/api/me", newHost), CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        }

        foreach (var id in ids)
        {
            var path = $"/api/apps/PurchaseRequests/entities/PurchaseRequest/records/{id:D}";
            using var restoredRead = await restored.SendAsync(Request(path, newHost), CancellationToken);
            using var originalRead = await client.SendAsync(Request(path, AdminBackupFixture.Host), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, restoredRead.StatusCode);
            Assert.Equal(HttpStatusCode.OK, originalRead.StatusCode);
            using var restoredBody = JsonDocument.Parse(await restoredRead.Content.ReadAsStringAsync(CancellationToken));
            using var originalBody = JsonDocument.Parse(await originalRead.Content.ReadAsStringAsync(CancellationToken));
            var title = originalBody.RootElement.GetProperty("values").GetProperty("title").GetString();
            Assert.False(string.IsNullOrEmpty(title));
            Assert.Equal(title, restoredBody.RootElement.GetProperty("values").GetProperty("title").GetString());
        }

        var after = await scenario.RowAsync(fixture.TenantId);
        Assert.Equal(
            (original.Status, original.Version, original.SchemaVersion, original.DatabaseName),
            (after.Status, after.Version, after.SchemaVersion, after.DatabaseName));
        Assert.Equal(originalHosts, await scenario.HostsAsync(fixture.TenantId));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task An_archive_of_an_older_schema_is_restored_migrated_and_served_after_resume()
    {
        var (oldVersion, database) = await NewOldDatabaseAsync();
        Assert.True(string.CompareOrdinal(oldVersion, TenantSchemaVersion.Current) < 0);
        using var folder = new TemporaryFolder();
        var dump = Path.Combine(folder.Path, "old.dump");
        await fixture.Tools.DumpAsync(new NpgsqlConnectionStringBuilder(database).Database!, dump, CancellationToken);
        var file = Path.Combine(folder.Path, "old.tar");
        WriteArchive(
            file,
            JsonSerializer.Serialize(
                new
                {
                    format = 1,
                    tenantId = "old",
                    axisVersion = "test",
                    schemaVersion = oldVersion,
                    createdAt = "2026-01-01T00:00:00Z",
                    directory = new
                    {
                        status = "active",
                        hosts = Array.Empty<string>(),
                        oidcAuthority = AdminScenario.Authority,
                        oidcClientId = "axis",
                        bindings = Array.Empty<object>(),
                    },
                    packages = Array.Empty<object>(),
                    modules = Array.Empty<object>(),
                },
                _web),
            dump);
        var scenario = fixture.Scenario;
        var id = scenario.NewTenantId();
        var host = $"{id}.example.test";

        var result = await RestoreAsync(file, id, host);

        Assert.Equal(0, result.Exit);
        string[] steps = ["check", "entry", "roles", "database", "restore", "grants", "migrations", "grants", "work", "role-check", "version"];
        Assert.Equal(steps.Select(step => $"{id} {step} ok"), result.Output);
        var row = await scenario.RowAsync(id);
        Assert.Equal(TenantSchemaVersion.Current, row.SchemaVersion);
        Assert.Equal("suspended", row.Status);

        await using var server = CreateDirectoryServer(scenario.Platform);
        using var client = server.CreateClient().SignedInAs("anna");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "resume", id])).Exit);
        await RefreshAsync(server);
        using var served = await client.SendAsync(Request("/api/me", host), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Each_refusal_exits_2_and_leaves_no_entry_role_or_database()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var scenario = fixture.Scenario;

        var unreadable = Path.Combine(folder.Path, "unreadable.tar");
        await File.WriteAllBytesAsync(unreadable, System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096), CancellationToken);
        var unknownFormat = Rewrite(file, folder.Path, "format", manifest => manifest["format"] = 2);
        var newer = Rewrite(file, folder.Path, "newer", manifest => manifest["schemaVersion"] = "99991231235959_Future");
        var unknownVersion = Rewrite(file, folder.Path, "unknown", manifest => manifest["schemaVersion"] = "20000101000000_Unknown");
        var missingPackage = Rewrite(
            file,
            folder.Path,
            "package",
            manifest => ((JsonArray)manifest["packages"]!).Add(new JsonObject { ["id"] = "missing-package", ["version"] = "9.9.9", ["required"] = true }));

        // The host is null where the case uses a new host.
        var cases = new (string Name, string Source, string? Host, string Message)[]
        {
            ("unreadable", unreadable, null, "the archive cannot be read"),
            ("format", unknownFormat, null, "format 2"),
            ("newer", newer, null, "99991231235959_Future"),
            ("unknown version", unknownVersion, null, "20000101000000_Unknown"),
            ("taken host", file, AdminBackupFixture.Host.ToUpperInvariant(), $"a host belongs to another tenant: {AdminBackupFixture.Host.ToUpperInvariant()}"),
            ("package", missingPackage, null, "missing-package 9.9.9"),
        };

        foreach (var (name, source, host, message) in cases)
        {
            var id = scenario.NewTenantId();
            var entries = await scenario.CountEntriesAsync();

            var result = await scenario.RunAsync(
                RestoreArgs(source, id, host ?? $"{id}.example.test"),
                path: fixture.Tools.PathSetting);

            Assert.True(result.Exit == 2, $"{name} exited with {result.Exit}");
            Assert.Single(result.Output);
            Assert.StartsWith($"{id} check failed: ", result.Output[0], StringComparison.Ordinal);
            Assert.Contains(message, result.Output[0], StringComparison.Ordinal);
            Assert.Empty(result.Error);
            await AssertNothingCreatedAsync(id, entries);
        }

        // An existing id is refused whether the tenant is active or suspended, and the tenant stays as it was.
        var before = await scenario.SnapshotAsync();
        var existing = await RestoreAsync(file, fixture.TenantId, "other.example.test");
        Assert.Equal(2, existing.Exit);
        Assert.Contains(fixture.TenantId, existing.Output.Single(), StringComparison.Ordinal);
        Assert.Contains("already exists", existing.Output.Single(), StringComparison.Ordinal);
        Assert.Equal(before, await scenario.SnapshotAsync());

        var noSecretId = scenario.NewTenantId();
        var entriesBefore = await scenario.CountEntriesAsync();
        var noSecret = await RestoreAsync(file, noSecretId, $"{noSecretId}.example.test", withClientSecret: false);
        Assert.Equal(2, noSecret.Exit);
        Assert.Empty(noSecret.Output);
        Assert.Contains("AXIS_OIDC_CLIENT_SECRET", noSecret.Error[0], StringComparison.Ordinal);
        await AssertNothingCreatedAsync(noSecretId, entriesBefore);

        var noTool = await scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", noSecretId, "--host", $"{noSecretId}.example.test"],
            path: folder.Path);
        Assert.Equal(2, noTool.Exit);
        Assert.Empty(noTool.Output);
        Assert.Contains("pg_restore", noTool.Error[0], StringComparison.Ordinal);
        await AssertNothingCreatedAsync(noSecretId, entriesBefore);

        var noPackages = await scenario.RunAsync(
            ["tenant", "restore", file, "--tenant", noSecretId, "--host", $"{noSecretId}.example.test"],
            path: fixture.Tools.PathSetting,
            extra: new Dictionary<string, string?> { ["Extensions:Path"] = folder.Path });
        Assert.Equal(2, noPackages.Exit);
        Assert.Contains("payment-references", noPackages.Output.Single(), StringComparison.Ordinal);
        await AssertNothingCreatedAsync(noSecretId, entriesBefore);

        string[][] usages =
        [
            ["tenant", "restore", file, "--host", "x.example.test"],
            ["tenant", "restore", file, "--tenant", noSecretId],
            ["tenant", "restore", "--tenant", noSecretId, "--host", "x.example.test"],
            ["tenant", "restore", file, "--tenant", noSecretId, "--tenant", noSecretId, "--host", "x.example.test"],
        ];
        foreach (var usage in usages)
        {
            var result = await scenario.RunAsync(usage, path: fixture.Tools.PathSetting);
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
            Assert.StartsWith("Usage:", result.Error[0], StringComparison.Ordinal);
        }

        await AssertNothingCreatedAsync(noSecretId, entriesBefore);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_preview_tenants_archive_is_refused_with_the_preview_message_and_creates_nothing()
    {
        var scenario = fixture.Scenario;
        var mainId = scenario.NewTenantId();
        var previewId = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(mainId, $"{mainId}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(previewId, mainId, $"{previewId}.example.test"), withClientSecret: false)).Exit);
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", previewId, "--out", file], path: fixture.Tools.PathSetting)).Exit);

        // An archive from before previewOf existed has the empty sign-in settings of a preview entry only.
        var old = Rewrite(file, folder.Path, "old-preview", manifest => ((JsonObject)manifest["directory"]!).Remove("previewOf"));

        foreach (var source in new[] { file, old })
        {
            var id = scenario.NewTenantId();
            var entries = await scenario.CountEntriesAsync();

            var result = await RestoreAsync(source, id, $"{id}.example.test");

            Assert.Equal(2, result.Exit);
            var line = Assert.Single(result.Output);
            Assert.StartsWith($"{id} check failed: ", line, StringComparison.Ordinal);
            Assert.Contains("preview tenant", line, StringComparison.Ordinal);
            Assert.Contains("tenant create --preview-of", line, StringComparison.Ordinal);
            Assert.DoesNotContain("OIDC", line, StringComparison.Ordinal);
            Assert.Empty(result.Error);
            await AssertNothingCreatedAsync(id, entries);
        }

        // The other checks are still reported next to the preview refusal.
        var other = scenario.NewTenantId();
        var taken = await RestoreAsync(file, other, AdminBackupFixture.Host);
        Assert.Equal(2, taken.Exit);
        Assert.Equal(2, taken.Output.Length);
        Assert.Single(taken.Output, text => text.Contains("preview tenant", StringComparison.Ordinal));
        Assert.Single(taken.Output, text => text.Contains(AdminBackupFixture.Host, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_archive_with_several_problems_reports_every_one_and_creates_nothing()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var scenario = fixture.Scenario;
        var several = Rewrite(
            file,
            folder.Path,
            "several",
            manifest =>
            {
                manifest["schemaVersion"] = "99991231235959_Future";
                ((JsonArray)manifest["packages"]!).Add(new JsonObject { ["id"] = "missing-package", ["version"] = "9.9.9", ["required"] = true });

                // A module that is not installed never refuses a restore.
                ((JsonArray)manifest["modules"]!).Add(new JsonObject { ["id"] = "missing-module", ["version"] = "9.9.9" });
            });
        var id = scenario.NewTenantId();
        var entries = await scenario.CountEntriesAsync();

        var result = await RestoreAsync(several, id, AdminBackupFixture.Host);

        Assert.Equal(2, result.Exit);
        Assert.Equal(3, result.Output.Length);
        Assert.All(result.Output, line => Assert.StartsWith($"{id} check failed: ", line, StringComparison.Ordinal));
        var text = string.Join('\n', result.Output);
        Assert.Contains("99991231235959_Future", text, StringComparison.Ordinal);
        Assert.Contains("missing-package 9.9.9", text, StringComparison.Ordinal);
        Assert.Contains(AdminBackupFixture.Host, text, StringComparison.Ordinal);
        Assert.DoesNotContain("missing-module", text, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        await AssertNothingCreatedAsync(id, entries);

        // The id and host checks need no manifest, so they run for an archive that cannot be read.
        var unreadable = Path.Combine(folder.Path, "unreadable.tar");
        await File.WriteAllBytesAsync(unreadable, System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096), CancellationToken);
        var before = await scenario.SnapshotAsync();
        var both = await RestoreAsync(unreadable, fixture.TenantId, $"{id}.example.test");
        Assert.Equal(2, both.Exit);
        Assert.Equal(2, both.Output.Length);
        Assert.Single(both.Output, line => line.Contains("already exists", StringComparison.Ordinal) && line.Contains(fixture.TenantId, StringComparison.Ordinal));
        Assert.Single(both.Output, line => line.Contains("the archive cannot be read", StringComparison.Ordinal));
        Assert.Equal(before, await scenario.SnapshotAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task No_output_of_a_restore_holds_a_password_the_client_secret_or_a_binding_secret()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var id = fixture.Scenario.NewTenantId();

        var result = await RestoreAsync(file, id, $"{id}.example.test");
        var refused = await RestoreAsync(file, id, $"{id}-again.example.test");

        Assert.Equal(0, result.Exit);
        Assert.Equal(2, refused.Exit);
        foreach (var line in result.Output.Concat(result.Error).Concat(refused.Output).Concat(refused.Error))
        {
            Assert.DoesNotContain(fixture.BindingSecret, line, StringComparison.Ordinal);
        }

        await fixture.Scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_restore_that_stopped_part_way_is_finished_by_running_the_same_command_again()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        var scenario = fixture.Scenario;
        var id = scenario.NewTenantId();
        var host = $"{id}.example.test";
        using var stop = new CancellationTokenSource();

        var stopped = await scenario.RunAsync(
            RestoreArgs(file, id, host),
            new CancelOnLineWriter(stop),
            stop,
            path: fixture.Tools.PathSetting);

        Assert.Equal(1, stopped.Exit);
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);

        var finished = await RestoreAsync(file, id, host);

        Assert.Equal(0, finished.Exit);
        Assert.Equal("suspended", (await scenario.RowAsync(id)).Status);
        Assert.Equal(TenantSchemaVersion.Current, (await scenario.RowAsync(id)).SchemaVersion);

        // A database that holds tables counts as restored, so a rerun of a finished restore's steps does not run pg_restore again.
        await scenario.SetStatusAsync(id, "provisioning");
        using var tools = new TemporaryFolder();
        PostgresClientTools.WriteScript(Path.Combine(tools.Path, "pg_restore"), "#!/bin/sh\nexit 3");
        var again = await scenario.RunAsync(RestoreArgs(file, id, host), path: tools.Path);
        Assert.Equal(0, again.Exit);
        Assert.Equal("suspended", (await scenario.RowAsync(id)).Status);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_failed_pg_restore_exits_1_keeps_the_entry_provisioning_and_never_prints_what_pg_restore_wrote()
    {
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");
        Assert.Equal(0, (await fixture.BackupAsync(file)).Exit);
        using var tools = new TemporaryFolder();
        PostgresClientTools.WriteScript(
            Path.Combine(tools.Path, "pg_restore"),
            "#!/bin/sh\necho 'connection to db.internal.example failed' >&2\nexit 3");
        var scenario = fixture.Scenario;
        var id = scenario.NewTenantId();

        var result = await scenario.RunAsync(
            RestoreArgs(file, id, $"{id}.example.test"),
            path: tools.Path);

        Assert.Equal(1, result.Exit);
        Assert.Equal($"{id} restore failed: pg_restore exited with code 3", result.Output[^1]);
        Assert.Empty(result.Error);
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);

        await scenario.AssertNoSecretsAsync();
    }

    internal static string[] RestoreArgs(string file, string id, params string[] hosts) =>
        ["tenant", "restore", file, "--tenant", id, .. hosts.SelectMany(host => new[] { "--host", host })];

    private Task<AdminResult> RestoreAsync(string file, string id, string host, bool withClientSecret = true) =>
        fixture.Scenario.RunAsync(
            RestoreArgs(file, id, host),
            withClientSecret: withClientSecret,
            path: fixture.Tools.PathSetting);

    private async Task AssertNothingCreatedAsync(string id, long entries)
    {
        Assert.Equal(entries, await fixture.Scenario.CountEntriesAsync());
        Assert.False(await fixture.Scenario.DatabaseExistsAsync(id));
        Assert.Equal(0, await fixture.Scenario.CountRolesAsync(id));
    }

    // A server on the directory of the scenario. Its refresh timer is an hour, so a test refreshes the directory itself.
    internal static WebApplicationFactory<Program> CreateDirectoryServer(string platform) =>
        AdminScenario.CreateServer(platform).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
        });

    internal static async Task RefreshAsync(WebApplicationFactory<Program> server) =>
        await server.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);

    private async Task<long> CountSecretBindingValuesAsync(string id) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(
            fixture.Scenario.Platform,
            $"SELECT count(*) FROM platform.tenant_bindings WHERE tenant_id = '{id}' AND secret_value IS NOT NULL",
            CancellationToken))!;

    private async Task<string[]> BindingNamesAsync(string id)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(fixture.Scenario.Platform).Options);
        return await context.TenantBindings.AsNoTracking().Where(binding => binding.TenantId == id).OrderBy(binding => binding.Name).Select(binding => binding.Name).ToArrayAsync(CancellationToken);
    }

    // Makes a database whose newest migration is older than this build's: the context that holds the current version stops one migration short.
    private async Task<(string Version, string ConnectionString)> NewOldDatabaseAsync()
    {
        var connectionString = await fixture.NewDatabaseAsync("old");
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connectionString).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        foreach (var context in new DbContext[] { configuration, data, processes })
        {
            var migrations = context.Database.GetMigrations().Order(StringComparer.Ordinal).ToArray();
            if (migrations[^1] == TenantSchemaVersion.Current)
            {
                Assert.True(migrations.Length > 1, "The context of the current version needs an older migration.");
                await context.GetService<IMigrator>().MigrateAsync(migrations[^2], CancellationToken);
            }
            else
            {
                await context.Database.MigrateAsync(CancellationToken);
            }
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        return (await TenantSchemaVersion.ReadAsync(connection, CancellationToken), connectionString);
    }

    // Copies the archive and edits its manifest. The dump stays as it was.
    private static string Rewrite(string source, string folder, string name, Action<JsonObject> edit)
    {
        string? manifest = null;
        var dump = Path.Combine(folder, $"{name}.dump");
        using (var stream = File.OpenRead(source))
        using (var reader = new TarReader(stream))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.Name == "manifest.json")
                {
                    using var text = new StreamReader(entry.DataStream!, Encoding.UTF8);
                    manifest = text.ReadToEnd();
                }
                else
                {
                    using var output = File.Create(dump);
                    entry.DataStream!.CopyTo(output);
                }
            }
        }

        var node = JsonNode.Parse(manifest!)!.AsObject();
        edit(node);
        var destination = Path.Combine(folder, $"{name}.tar");
        WriteArchive(destination, node.ToJsonString(), dump);
        return destination;
    }

    private static void WriteArchive(string destination, string manifest, string dumpFile)
    {
        using var archive = File.Create(destination);
        using var writer = new TarWriter(archive, TarEntryFormat.Pax);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(manifest)) });
        using var dump = File.OpenRead(dumpFile);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "tenant.dump") { DataStream = dump });
    }
}
