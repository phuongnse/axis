using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class StartupActivationTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string HostA = "a.example.test";
    private const string HostB = "b.example.test";

    private const string TitleField = """{ "name": "title", "type": "text", "required": true, "maxLength": 200 }""";
    private const string DoneField = """{ "name": "done", "type": "boolean" }""";

    // Each test uses its own application, so active releases and tables never meet across tests.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _noteId = Guid.NewGuid();
    private readonly string _name = $"Startup{Guid.NewGuid():N}";
    private readonly Guid _firstSeedId = Guid.NewGuid();
    private readonly Guid _secondSeedId = Guid.NewGuid();
    private readonly Guid _seedResourceId = Guid.NewGuid();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_migrates_every_tenant_database_and_activates_the_folder_and_a_restart_stores_no_new_release()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField);

        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            foreach (var host in new[] { HostA, HostB })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/apps/{_name}/entities/Note/records");
                request.Headers.Host = host;
                using var response = await client.SendAsync(request, CancellationToken);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        var first = new[] { await ActiveAsync(a), await ActiveAsync(b) };
        Assert.All(first, Assert.NotNull);

        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            factory.CreateClient().Dispose();
        }

        var second = new[] { await ActiveAsync(a), await ActiveAsync(b) };
        foreach (var (before, after) in first.Zip(second))
        {
            Assert.NotNull(after);
            Assert.Equal(before!.ReleaseId, after.ReleaseId);
            Assert.True(after.ActivatedAt > before.ActivatedAt, "Activating again refreshes the activation time.");
        }

        Assert.Equal(1L, await ScalarAsync(a, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(1L, await ScalarAsync(b, "SELECT count(*) FROM axis.releases"));
        Assert.Equal(0L, await ScalarAsync(a, "SELECT count(*) FROM axis.process_work_items"));
        Assert.Equal(0L, await ScalarAsync(b, "SELECT count(*) FROM axis.process_work_items"));
    }

    [Fact]
    public async Task Compile_diagnostic_stops_the_start_is_logged_with_its_code_and_file_and_keeps_the_active_release()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField);
        var active = await StartAsync(a, b, folder);

        folder.With("entities/broken.json", "{");
        var logs = new LogCollector();
        await using var factory = CreateFactory(a, b, folder.Path, logs);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(folder.Path, exception.Message, StringComparison.Ordinal);
        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.InvalidJson, StringComparison.Ordinal)
            && entry.Contains("entities/broken.json", StringComparison.Ordinal));
        Assert.Equal(active, [(await ActiveAsync(a))?.ReleaseId, (await ActiveAsync(b))?.ReleaseId]);
    }

    [Fact]
    public async Task Activation_diagnostic_stops_the_start_is_logged_with_its_code_and_file_and_keeps_the_active_release()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField);
        var active = await StartAsync(a, b, folder);

        // Removing a field compiles, but provisioning refuses it.
        folder.With("entities/note.json", NoteFile(TitleField));
        var logs = new LogCollector();
        await using var factory = CreateFactory(a, b, folder.Path, logs);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(folder.Path, exception.Message, StringComparison.Ordinal);
        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.RemovedField, StringComparison.Ordinal)
            && entry.Contains("entities/note.json", StringComparison.Ordinal));
        Assert.Equal(active, [(await ActiveAsync(a))?.ReleaseId, (await ActiveAsync(b))?.ReleaseId]);
    }

    [Fact]
    public async Task Connector_binding_check_stops_the_start_outside_development_and_keeps_the_active_release()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField);
        var active = await StartAsync(a, b, folder);

        folder.With("connectors/purchasing-system.json", $$"""
            { "id": "{{Guid.NewGuid()}}", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
              "baseUrl": { "binding": "PurchasingSystemUrl" },
              "auth": { "type": "bearer", "token": { "binding": "PurchasingSystemBearerToken" } },
              "operations": [ { "name": "createPurchaseOrder", "method": "POST", "path": "/purchase-orders" } ] }
            """);
        var logs = new LogCollector();
        await using var factory = CreateFactory(a, b, folder.Path, logs, new Dictionary<string, string>
        {
            ["Tenants:a:Bindings:PurchasingSystemUrl:Kind"] = "url",
            ["Tenants:a:Bindings:PurchasingSystemUrl:Value"] = "http://purchasing.example.test/api",
            ["Tenants:a:Bindings:PurchasingSystemBearerToken:Kind"] = "secret",
            ["Tenants:a:Bindings:PurchasingSystemBearerToken:Value"] = "token-value-never-logged",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(folder.Path, exception.Message, StringComparison.Ordinal);
        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.InsecureBaseUrl, StringComparison.Ordinal)
            && entry.Contains("connectors/purchasing-system.json", StringComparison.Ordinal)
            && entry.Contains("PurchasingSystemUrl", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("token-value-never-logged", StringComparison.Ordinal));
        Assert.Equal(active, [(await ActiveAsync(a))?.ReleaseId, (await ActiveAsync(b))?.ReleaseId]);
    }

    [Fact]
    public async Task Seed_records_are_inserted_once_and_a_record_edited_between_starts_keeps_its_edit()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField)
            .With("seeds/notes.json", SeedFile("""{ "title": "First", "done": false }""", """{ "title": "Second", "done": false }"""));

        var logs = new LogCollector();
        await using (var factory = CreateFactory(a, b, folder.Path, logs))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/apps/{_name}/entities/Note/records/{_firstSeedId}")
            {
                Content = JsonContent.Create(new { version = 1, values = new { title = "Edited" } }),
            };
            request.Headers.Host = HostA;
            request.Headers.Add("X-Axis-Csrf", "1");
            using var response = await client.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Contains(logs.Entries, entry => entry.Contains("Inserted 2 seed record(s)", StringComparison.Ordinal));
        await StartAsync(a, b, folder);

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_noteId));
        Assert.Equal(2L, await ScalarAsync(a, $"SELECT count(*) FROM {table}"));
        Assert.Equal(2L, await ScalarAsync(b, $"SELECT count(*) FROM {table}"));

        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            foreach (var (host, title, version) in new[] { (HostA, "Edited", 2L), (HostB, "First", 1L) })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/apps/{_name}/entities/Note/records/{_firstSeedId}");
                request.Headers.Host = host;
                using var response = await client.SendAsync(request, CancellationToken);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var record = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
                Assert.Equal(
                    (title, version),
                    (record.GetProperty("values").GetProperty("title").GetString(), record.GetProperty("version").GetInt64()));
            }
        }
    }

    [Fact]
    public async Task Synced_seed_updates_a_changed_record_and_an_unchanged_start_writes_nothing()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField)
            .With("seeds/notes.json", SeedFile("""{ "title": "First", "done": false }""", """{ "title": "Second", "done": false }""", sync: true));

        var logs = new LogCollector();
        await StartAsync(a, b, folder, logs);
        Assert.Contains(logs.Entries, entry => entry.Contains("Inserted 2 seed record(s) and updated 0 ", StringComparison.Ordinal));

        logs = new LogCollector();
        await using (var factory = CreateFactory(a, b, folder.Path, logs))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            Assert.Equal(("First", false, 1L), await GetRecordAsync(client, HostA, _firstSeedId));
        }

        Assert.Contains(logs.Entries, entry => entry.Contains("Inserted 0 seed record(s) and updated 0 ", StringComparison.Ordinal));

        folder.With("seeds/notes.json", SeedFile("""{ "title": "Renamed", "done": false }""", """{ "title": "Second", "done": false }""", sync: true));
        logs = new LogCollector();
        await using (var factory = CreateFactory(a, b, folder.Path, logs))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            foreach (var host in new[] { HostA, HostB })
            {
                Assert.Equal(("Renamed", false, 2L), await GetRecordAsync(client, host, _firstSeedId));
                Assert.Equal(("Second", false, 1L), await GetRecordAsync(client, host, _secondSeedId));
            }
        }

        Assert.Contains(logs.Entries, entry => entry.Contains("Inserted 0 seed record(s) and updated 1 ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Synced_seed_keeps_edits_to_undeclared_fields_and_records_removed_from_the_file()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField)
            .With("seeds/notes.json", SeedFile("""{ "title": "First" }""", """{ "title": "Second" }""", sync: true));

        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/apps/{_name}/entities/Note/records/{_firstSeedId}")
            {
                Content = JsonContent.Create(new { version = 1, values = new { done = true } }),
            };
            request.Headers.Host = HostA;
            request.Headers.Add("X-Axis-Csrf", "1");
            using var response = await client.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        folder.With("seeds/notes.json", SeedFile("""{ "title": "Renamed" }""", secondValues: null, sync: true));
        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            Assert.Equal(("Renamed", true, 3L), await GetRecordAsync(client, HostA, _firstSeedId));
            Assert.Equal(("Second", null, 1L), await GetRecordAsync(client, HostA, _secondSeedId));
        }

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_noteId));
        Assert.Equal(2L, await ScalarAsync(a, $"SELECT count(*) FROM {table}"));
        Assert.Equal(2L, await ScalarAsync(b, $"SELECT count(*) FROM {table}"));
    }

    [Fact]
    public async Task Synced_seed_computes_its_computed_field_from_the_declared_and_the_stored_values()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        const string SummaryField = """{ "name": "summary", "type": "text", "expression": "if(done == true, concat(title, ' (done)'), title)" }""";
        using var folder = ApplicationFolder(TitleField, DoneField, SummaryField)
            .With("seeds/notes.json", SeedFile("""{ "title": "First" }""", """{ "title": "Second" }""", sync: true));

        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            Assert.Equal("First", await GetSummaryAsync(client, _firstSeedId));

            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/apps/{_name}/entities/Note/records/{_firstSeedId}")
            {
                Content = JsonContent.Create(new { version = 1, values = new { done = true } }),
            };
            request.Headers.Host = HostA;
            request.Headers.Add("X-Axis-Csrf", "1");
            using var response = await client.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("First (done)", await GetSummaryAsync(client, _firstSeedId));
        }

        // The seed does not declare done, so the update computes the summary from the stored value.
        folder.With("seeds/notes.json", SeedFile("""{ "title": "Renamed" }""", """{ "title": "Second" }""", sync: true));
        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            Assert.Equal("Renamed (done)", await GetSummaryAsync(client, _firstSeedId));
            Assert.Equal("Second", await GetSummaryAsync(client, _secondSeedId));
        }
    }

    [Fact]
    public async Task Seed_value_that_cannot_be_computed_stops_the_start_at_the_computed_field_and_inserts_no_seed_record()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        const string SizeField = """{ "name": "size", "type": "integer", "expression": "length(title) * 1000000000000000000" }""";
        using var folder = ApplicationFolder(TitleField, DoneField, SizeField)
            .With("seeds/notes.json", SeedFile("""{ "title": "A" }""", """{ "title": "Much too long" }"""));
        var logs = new LogCollector();
        await using var factory = CreateFactory(a, b, folder.Path, logs);

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.InvalidSeedValue, StringComparison.Ordinal)
            && entry.Contains("seeds/notes.json", StringComparison.Ordinal)
            && entry.Contains("/records/1/values/size", StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarAsync(a, $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_noteId))}"));
    }

    [Fact]
    public async Task Seed_record_that_leaves_out_a_required_field_with_a_default_is_inserted_with_the_default()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        const string KindField = """{ "name": "kind", "type": "text", "required": true, "maxLength": 20, "default": "'seeded'" }""";
        using var folder = ApplicationFolder(TitleField, KindField)
            .With("seeds/notes.json", SeedFile("""{ "title": "First" }""", secondValues: null));

        await using var factory = CreateFactory(a, b, folder.Path, new LogCollector());
        using var client = factory.CreateClient();

        Assert.Equal(
            "seeded",
            await ScalarAsync(a, $"SELECT {EntityNaming.Quote(EntityNaming.Column("kind"))} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_noteId))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = '{_firstSeedId}'"));
    }

    [Fact]
    public async Task Seed_without_sync_ignores_a_changed_file_value()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField)
            .With("seeds/notes.json", SeedFile("""{ "title": "First", "done": false }""", """{ "title": "Second", "done": false }"""));
        await StartAsync(a, b, folder);

        folder.With("seeds/notes.json", SeedFile("""{ "title": "Renamed", "done": false }""", """{ "title": "Second", "done": false }"""));
        var logs = new LogCollector();
        await using (var factory = CreateFactory(a, b, folder.Path, logs))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            Assert.Equal(("First", false, 1L), await GetRecordAsync(client, HostA, _firstSeedId));
        }

        Assert.Contains(logs.Entries, entry => entry.Contains("Inserted 0 seed record(s) and updated 0 ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_seed_value_stops_the_start_is_logged_with_its_file_and_pointer_and_inserts_no_seed_record()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField)
            .With("seeds/notes.json", SeedFile("""{ "title": 5, "done": false }""", """{ "title": "Second", "done": false }"""));
        var logs = new LogCollector();
        await using var factory = CreateFactory(a, b, folder.Path, logs);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(folder.Path, exception.Message, StringComparison.Ordinal);
        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.InvalidSeedValue, StringComparison.Ordinal)
            && entry.Contains("seeds/notes.json", StringComparison.Ordinal)
            && entry.Contains("/records/0/values/title", StringComparison.Ordinal));
        Assert.NotNull(await ActiveAsync(a));
        Assert.Equal(0L, await ScalarAsync(a, $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_noteId))}"));
    }

    [Fact]
    public async Task Seed_reference_to_a_missing_record_stops_the_start_and_rolls_back_the_earlier_seed_records()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField)
            .With("entities/note.json", $$"""
                { "id": "{{_noteId}}", "kind": "entity", "name": "Note", "formatVersion": 1, "displayField": "title",
                  "fields": [{{TitleField}}, { "name": "parent", "type": "reference", "target": "Note" }] }
                """)
            .With("seeds/notes.json", SeedFile("""{ "title": "First" }""", $$"""{ "title": "Second", "parent": "{{Guid.NewGuid()}}" }"""));
        var logs = new LogCollector();
        await using var factory = CreateFactory(a, b, folder.Path, logs);

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.InvalidSeedValue, StringComparison.Ordinal)
            && entry.Contains("seeds/notes.json", StringComparison.Ordinal)
            && entry.Contains("/records/1/values/parent", StringComparison.Ordinal));
        Assert.NotNull(await ActiveAsync(a));
        Assert.Equal(0L, await ScalarAsync(a, $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_noteId))}"));
    }

    [Fact]
    public async Task Start_builds_a_new_index_that_activation_deferred_on_a_table_above_the_limit_before_the_server_listens()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField);
        await StartAsync(a, b, folder);
        var table = EntityNaming.Table(_noteId);
        await ScalarAsync(a, $"INSERT INTO {EntityNaming.QualifiedTable(table)} (id, f_title) SELECT gen_random_uuid(), 'n' || n FROM generate_series(1, 25) n");
        folder.With("entities/note.json", $$"""
            { "id": "{{_noteId}}", "kind": "entity", "name": "Note", "formatVersion": 1,
              "indexes": [{ "fields": ["title"] }], "fields": [{{TitleField}}] }
            """);
        var logs = new LogCollector();

        await using (var factory = CreateFactory(a, b, folder.Path, logs, new Dictionary<string, string> { ["Activation:InlineRecomputeLimit"] = "10" }))
        {
            factory.CreateClient().Dispose();
        }

        var index = EntityNaming.Index(table, ["f_title"]);
        Assert.Equal(
            true,
            await ScalarAsync(a, $"SELECT x.indisvalid FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid WHERE i.relname = '{index}'"));
        Assert.Contains(logs.Entries, entry => entry.Contains("Built 1 deferred index(es)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_compiles_against_the_installed_packages_and_pins_the_highest_version_in_range()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var packages = new TemporaryFolder().Install("1.0.0", "2.0.0");
        using var folder = ApplicationFolder(TitleField)
            .With("application.json", $$"""
                { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
                  "packages": [{ "id": "{{TestPackages.Id}}", "version": "^1.0.0" }] }
                """);

        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector(), new Dictionary<string, string>
        {
            ["Extensions:Path"] = packages.Path,
        }))
        {
            factory.CreateClient().Dispose();
        }

        var active = await ActiveAsync(a);
        Assert.NotNull(active);
        Assert.Equal(
            $"{TestPackages.Id} 1.0.0",
            await ScalarAsync(a, $"SELECT package_id || ' ' || version FROM axis.release_packages WHERE release_id = '{active.ReleaseId}'"));
        Assert.Equal(1L, await ScalarAsync(a, "SELECT count(*) FROM axis.release_packages"));
    }

    [Fact]
    public async Task Start_activates_an_application_that_declares_the_sample_departments_module()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField)
            .With("application.json", $$"""
                { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
                  "modules": [{ "id": "departments", "version": "^1.0.0" }] }
                """)
            .With("texts/vi.json", """{ "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a16", "kind": "text", "name": "TextsVi", "formatVersion": 1, "locale": "vi", "texts": { "role.employee": "Nhân viên" } }""")
            .With(
                "policies/manager-access.json",
                """
                { "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a17", "kind": "policy", "name": "ManagerAccess", "formatVersion": 1, "roles": ["employee"],
                  "grants": [
                    { "entity": "departments/Department", "actions": ["read"], "filter": "departments:IsManager(manager, currentUser())" },
                    { "dataSource": "departments/Departments", "actions": ["read"] }
                  ] }
                """);
        var logs = new LogCollector();

        await using (var factory = CreateFactory(a, b, folder.Path, logs, new Dictionary<string, string>
        {
            ["Modules:Path"] = SampleModules.Path,
        }))
        {
            factory.CreateClient().Dispose();
        }

        Assert.NotNull(await ActiveAsync(a));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("AXC", StringComparison.Ordinal));
        var department = EntityNaming.QualifiedTable(EntityNaming.Table(ModuleResourceIds.Derive(_applicationId, SampleModules.DepartmentEntityId)));
        Assert.Equal(3L, await ScalarAsync(a, $"SELECT count(*) FROM {department}"));
    }

    [Fact]
    public async Task Package_that_is_not_installed_stops_the_start_with_its_diagnostic()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField)
            .With("application.json", $$"""
                { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
                  "packages": [{ "id": "{{TestPackages.Id}}", "version": "^1.0.0" }] }
                """);
        var logs = new LogCollector();

        await using var factory = CreateFactory(a, b, folder.Path, logs);

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.UnknownPackage, StringComparison.Ordinal)
            && entry.Contains("application.json /packages/0/id", StringComparison.Ordinal));
        Assert.Null(await ActiveAsync(a));
    }

    [Fact]
    public async Task Start_brings_the_realm_of_a_tenant_with_realm_details_in_step_with_the_sites_of_the_folder()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        var secrets = new TenantRealmSecrets("platform-1", "activation-1", "users-1");
        var baseAddress = await keycloak.BaseAddressAsync();
        var realm = $"t{Guid.NewGuid():N}"[..16];
        using var http = new HttpClient();
        using var folder = WithSites(ApplicationFolder(TitleField), "one", "two");
        try
        {
            await new TenantRealm(http, new IdentityProviderAdmin(baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
                .EnsureAsync(realm, "Acme", secrets, new Dictionary<string, string>(), CancellationToken);

            await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector(), RealmSettings(baseAddress, realm, secrets.Activation)))
            {
                factory.CreateClient().Dispose();
            }

            var token = await MasterTokenAsync(http, baseAddress);
            foreach (var path in new[] { "one", "two" })
            {
                var clients = await GetJsonAsync(http, baseAddress, token, $"admin/realms/{realm}/clients?clientId=site-{path}");
                Assert.Equal(1, clients.GetArrayLength());
            }
        }
        finally
        {
            await DeleteRealmAsync(http, baseAddress, realm);
        }
    }

    [Fact]
    public async Task A_wrong_activation_secret_stops_the_start_and_neither_the_error_nor_the_log_holds_it()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        const string WrongSecret = "wrong-activation-secret-never-logged";
        var baseAddress = await keycloak.BaseAddressAsync();
        var realm = $"t{Guid.NewGuid():N}"[..16];
        using var http = new HttpClient();
        using var folder = WithSites(ApplicationFolder(TitleField), "one", "two");
        var logs = new LogCollector();
        try
        {
            await new TenantRealm(http, new IdentityProviderAdmin(baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
                .EnsureAsync(realm, "Acme", new TenantRealmSecrets("platform-1", "activation-1", "users-1"), new Dictionary<string, string>(), CancellationToken);

            await using var factory = CreateFactory(a, b, folder.Path, logs, RealmSettings(baseAddress, realm, WrongSecret));

            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            Assert.Contains("get the activation token", exception.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(WrongSecret, exception.ToString(), StringComparison.Ordinal);
            Assert.All(logs.Entries, entry => Assert.DoesNotContain(WrongSecret, entry, StringComparison.Ordinal));
        }
        finally
        {
            await DeleteRealmAsync(http, baseAddress, realm);
        }
    }

    [Fact]
    public async Task Without_the_setting_the_server_starts_and_leaves_the_tenant_database_unmigrated()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        await using var factory = CreateFactory(a, b, folder: null, new LogCollector());

        using var client = factory.CreateClient().SignedInAs("requester");
        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DBNull.Value, await ScalarAsync(a, "SELECT to_regclass('axis.releases')::text"));
        Assert.Equal(DBNull.Value, await ScalarAsync(b, "SELECT to_regclass('axis.releases')::text"));
    }

    [Fact]
    public async Task Startup_activation_leaves_the_schema_role_owning_every_axis_and_entities_table()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);

        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            factory.CreateClient().Dispose();
        }

        Assert.Equal(0L, await ScalarAsync(a, $"SELECT count(*) FROM pg_tables WHERE schemaname IN ('axis', 'entities') AND tableowner <> '{PostgreSqlServer.SchemaRole}'"));
        Assert.True((long)(await ScalarAsync(a, "SELECT count(*) FROM pg_tables WHERE schemaname = 'entities'"))! > 0);
    }

    [Fact]
    public async Task Runtime_role_cannot_create_alter_or_drop_in_the_axis_or_entities_schema()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);
        await StartAsync(a, b, folder);
        var note = EntityNaming.QualifiedTable(EntityNaming.Table(_noteId));

        foreach (var sql in new[]
        {
            "CREATE TABLE axis.denied (id uuid)",
            "CREATE TABLE entities.denied (id uuid)",
            $"ALTER TABLE {note} ADD COLUMN denied text",
            "ALTER TABLE axis.audit_records ADD COLUMN denied text",
            $"DROP TABLE {note}",
            "DROP TABLE axis.audit_records",
        })
        {
            await AssertSqlStateAsync(PostgreSqlServer.RuntimeConnectionString(a), sql, PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Fact]
    public async Task Runtime_role_reads_and_writes_the_table_of_an_entity_that_a_later_start_created()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);
        await StartAsync(a, b, folder);

        var ticketId = Guid.NewGuid();
        folder.With("entities/ticket.json", $$"""{ "id": "{{ticketId}}", "kind": "entity", "name": "Ticket", "formatVersion": 1, "fields": [{{TitleField}}] }""");
        await StartAsync(a, b, folder);

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(ticketId));
        var runtime = PostgreSqlServer.RuntimeConnectionString(a);
        var id = Guid.NewGuid();
        await ScalarAsync(runtime, $"INSERT INTO {table} (id, f_title) VALUES ('{id}', 'first')");
        Assert.Equal(1L, await ScalarAsync(runtime, $"SELECT count(*) FROM {table}"));
        await ScalarAsync(runtime, $"UPDATE {table} SET f_title = 'second' WHERE id = '{id}'");
        Assert.Equal("second", await ScalarAsync(runtime, $"SELECT f_title FROM {table} WHERE id = '{id}'"));
        await ScalarAsync(runtime, $"DELETE FROM {table} WHERE id = '{id}'");
        Assert.Equal(0L, await ScalarAsync(runtime, $"SELECT count(*) FROM {table}"));
    }

    [Fact]
    public async Task Runtime_role_reads_and_writes_the_rows_of_the_sessions_table()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);
        await StartAsync(a, b, folder);

        var runtime = PostgreSqlServer.RuntimeConnectionString(a);
        await ScalarAsync(
            runtime,
            """
            INSERT INTO axis.sessions (id_hash, user_id, display_name, roles, signed_in_at, last_request_at)
            VALUES ('\x01'::bytea, 'requester', 'Requester User', ARRAY['employee'], now(), now())
            """);
        Assert.Equal("requester", await ScalarAsync(runtime, "SELECT user_id FROM axis.sessions WHERE id_hash = '\\x01'::bytea"));
        await ScalarAsync(runtime, "UPDATE axis.sessions SET last_request_at = now() WHERE id_hash = '\\x01'::bytea");
        await ScalarAsync(runtime, "DELETE FROM axis.sessions WHERE id_hash = '\\x01'::bytea");
        Assert.Equal(0L, await ScalarAsync(runtime, "SELECT count(*) FROM axis.sessions"));
    }

    [Fact]
    public async Task Nobody_can_update_delete_or_truncate_a_stored_release_and_the_runtime_role_cannot_insert_one()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);
        var active = await StartAsync(a, b, folder);

        // A release without packages leaves release_packages empty, and a row trigger needs a row to fire on.
        await ScalarAsync(a, $"INSERT INTO axis.release_packages (release_id, package_id, version) VALUES ('{active[0]}', 'Example', '1.0.0')");
        var runtime = PostgreSqlServer.RuntimeConnectionString(a);

        foreach (var table in new[] { "releases", "release_resources", "release_packages" })
        {
            var column = table == "releases" ? "id" : "release_id";
            foreach (var sql in new[] { $"UPDATE axis.{table} SET {column} = {column}", $"DELETE FROM axis.{table}" })
            {
                await AssertSqlStateAsync(runtime, sql, PostgresErrorCodes.InsufficientPrivilege);
                await AssertSqlStateAsync(a, sql, PostgresErrorCodes.RestrictViolation);
            }

            await AssertSqlStateAsync(a, $"TRUNCATE axis.{table} CASCADE", PostgresErrorCodes.RestrictViolation);
            await AssertSqlStateAsync(runtime, $"INSERT INTO axis.{table} DEFAULT VALUES", PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Fact]
    public async Task Runtime_role_cannot_write_the_tables_that_activation_writes()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);
        await StartAsync(a, b, folder);
        var runtime = PostgreSqlServer.RuntimeConnectionString(a);

        foreach (var (table, column) in new[] { ("active_releases", "application_id"), ("provisioned_entities", "entity_id"), ("release_computed_fingerprints", "entity_id") })
        {
            Assert.True((long)(await ScalarAsync(runtime, $"SELECT count(*) FROM axis.{table}"))! > 0);
            foreach (var sql in new[] { $"INSERT INTO axis.{table} DEFAULT VALUES", $"UPDATE axis.{table} SET {column} = {column}", $"DELETE FROM axis.{table}" })
            {
                await AssertSqlStateAsync(runtime, sql, PostgresErrorCodes.InsufficientPrivilege);
            }
        }

        // The application has one entity and it is not a child entity, so its release has one fingerprint.
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM axis.release_computed_fingerprints"));
    }

    [Fact]
    public async Task Start_repairs_a_database_with_wider_grants_than_the_runtime_role_may_have()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);
        await StartAsync(a, b, folder);
        await ScalarAsync(a, $"GRANT ALL ON axis.active_releases TO {PostgreSqlServer.RuntimeRole}");
        await ScalarAsync(a, $"ALTER DEFAULT PRIVILEGES GRANT ALL ON TABLES TO {PostgreSqlServer.RuntimeRole}");

        await StartAsync(a, b, folder);

        await AssertSqlStateAsync(PostgreSqlServer.RuntimeConnectionString(a), "DELETE FROM axis.active_releases", PostgresErrorCodes.InsufficientPrivilege);
        Assert.Equal(0L, await ScalarAsync(a, "SELECT count(*) FROM pg_default_acl WHERE defaclnamespace = 0"));
    }

    [Fact]
    public async Task Start_refuses_a_runtime_role_that_is_the_schema_role_and_names_the_tenant()
    {
        var a = await CreateDatabaseAsync();
        var b = await CreateDatabaseAsync();
        using var folder = ApplicationFolder(TitleField);
        var logs = new LogCollector();
        await using var factory = CreateFactory(
            a, b, folder.Path, logs, new Dictionary<string, string> { ["Tenants:a:ConnectionString"] = a });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("tenant 'a'", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, await ScalarAsync(a, "SELECT count(*) FROM pg_tables WHERE schemaname = 'axis'"));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string a,
        string b,
        string? folder,
        LogCollector logs,
        IReadOnlyDictionary<string, string>? settings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(a);
            builder.UseSetting("Tenants:a:Hosts:0", HostA);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(a));
            builder.UseSetting("Tenants:a:SchemaConnectionString", a);
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", PostgreSqlServer.RuntimeConnectionString(b));
            builder.UseSetting("Tenants:b:SchemaConnectionString", b);
            builder.UseTestOidc("a", "b");
            builder.UseTestAuthentication(new TestIdentity("requester", "Requester User", "employee"));
            if (folder is not null)
            {
                builder.UseSetting("ActivateOnStartup:0", folder);
            }

            foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    /// <summary>The settings that give tenant <c>a</c> the realm details of <paramref name="realm"/> in the shared Keycloak.</summary>
    private static Dictionary<string, string> RealmSettings(Uri baseAddress, string realm, string activationSecret) => new()
    {
        ["Tenants:a:Oidc:Authority"] = new Uri(baseAddress, $"realms/{realm}").ToString(),
        ["Tenants:a:Realm:Name"] = realm,
        ["Tenants:a:Realm:ActivationClientSecret"] = activationSecret,
    };

    /// <summary>Gives the folder one site for each path, each opening a table page of notes.</summary>
    private static TemporaryFolder WithSites(TemporaryFolder folder, params string[] paths)
    {
        folder
            .With("texts/en.json", $$"""
                { "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a14", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "role.employee": "Employee", "site.title": "Notes", "nav.notes": "Notes", "notes.title": "Notes" } }
                """)
            .With("pages/notes.json", """
                { "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a21", "kind": "page", "name": "Notes", "formatVersion": 1,
                  "title": { "textKey": "notes.title" }, "widgets": [ { "type": "table", "entity": "Note" } ] }
                """)
            .With("identity-providers/accounts.json", """{ "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a22", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }""");
        foreach (var path in paths)
        {
            folder.With($"sites/{path}.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "site", "name": "Site{{path}}", "formatVersion": 1, "path": "{{path}}",
                  "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
                  "navigation": [ { "page": "Notes", "label": { "textKey": "nav.notes" } } ] }
                """);
        }

        return folder;
    }

    private static async Task<string> MasterTokenAsync(HttpClient http, Uri baseAddress)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(baseAddress, "realms/master/protocol/openid-connect/token"), form, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, Uri baseAddress, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private static async Task DeleteRealmAsync(HttpClient http, Uri baseAddress, string realm)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(baseAddress, $"admin/realms/{realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync(http, baseAddress));
        using var response = await http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    /// <summary>Starts the server once with <paramref name="folder"/> and returns the active release ids in both tenants.</summary>
    private async Task<Guid?[]> StartAsync(string a, string b, TemporaryFolder folder, LogCollector? logs = null)
    {
        await using (var factory = CreateFactory(a, b, folder.Path, logs ?? new LogCollector()))
        {
            factory.CreateClient().Dispose();
        }

        Guid?[] active = [(await ActiveAsync(a))?.ReleaseId, (await ActiveAsync(b))?.ReleaseId];
        Assert.All(active, id => Assert.NotNull(id));
        return active;
    }

    /// <summary>Reads a <c>Note</c> record through the record API; <c>Done</c> is null when the field has no value.</summary>
    private async Task<(string? Title, bool? Done, long Version)> GetRecordAsync(HttpClient client, string host, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/apps/{_name}/entities/Note/records/{id}");
        request.Headers.Host = host;
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var values = record.GetProperty("values");
        bool? done = values.TryGetProperty("done", out var value) && value.ValueKind != JsonValueKind.Null ? value.GetBoolean() : null;
        return (values.GetProperty("title").GetString(), done, record.GetProperty("version").GetInt64());
    }

    /// <summary>Reads the computed <c>summary</c> of a <c>Note</c> record in tenant A.</summary>
    private async Task<string?> GetSummaryAsync(HttpClient client, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/apps/{_name}/entities/Note/records/{id}");
        request.Headers.Host = HostA;
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return record.GetProperty("values").GetProperty("summary").GetString();
    }

    private async Task<ActiveRelease?> ActiveAsync(string connectionString)
    {
        await using var context = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        return await new ActiveReleaseStore(context).FindByApplicationIdAsync(_applicationId, CancellationToken);
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(CancellationToken);
    }

    /// <summary>Runs <paramref name="sql"/> and checks that PostgreSQL refuses it with <paramref name="sqlState"/>.</summary>
    private static async Task AssertSqlStateAsync(string connectionString, string sql, string sqlState)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(connectionString, sql));
        Assert.True(exception.SqlState == sqlState, $"{sql} gave {exception.SqlState}, expected {sqlState}: {exception.Message}");
    }

    private Task<string> CreateDatabaseAsync() => database.CreateDatabaseAsync("startup", CancellationToken);

    private TemporaryFolder ApplicationFolder(params string[] fields) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/note.json", NoteFile(fields))
            .With("roles/employee.json", """{ "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a13", "kind": "role", "name": "employee", "formatVersion": 1, "label": { "textKey": "role.employee" } }""")
            .With("texts/en.json", """{ "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a14", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": { "role.employee": "Employee" } }""")
            .With(
                "policies/employee-access.json",
                """{ "id": "4b0f6a1e-2c7d-4e3a-9f51-8d2c6b7e0a15", "kind": "policy", "name": "EmployeeAccess", "formatVersion": 1, "roles": ["employee"], "grants": [{ "entity": "Note", "actions": ["read", "create", "update", "delete"] }] }""");

    /// <summary>
    /// A seed over <c>Note</c> with the fixed seed ids and the given values, synced when
    /// <paramref name="sync"/> is set. The second record is left out when its values are null.
    /// </summary>
    private string SeedFile(string firstValues, string? secondValues, bool sync = false)
    {
        var records = secondValues is null
            ? $$"""{ "id": "{{_firstSeedId}}", "values": {{firstValues}} }"""
            : $$"""{ "id": "{{_firstSeedId}}", "values": {{firstValues}} }, { "id": "{{_secondSeedId}}", "values": {{secondValues}} }""";
        return $$"""
            { "id": "{{_seedResourceId}}", "kind": "seed", "name": "Notes", "formatVersion": 1, "entity": "Note",{{(sync ? " \"sync\": true," : "")}}
              "records": [ {{records}} ] }
            """;
    }

    private string NoteFile(params string[] fields) =>
        $$"""{ "id": "{{_noteId}}", "kind": "entity", "name": "Note", "formatVersion": 1, "fields": [{{string.Join(", ", fields)}}] }""";
}
