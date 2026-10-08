using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class StartupActivationTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
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
            using var client = factory.CreateClient();
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
    public async Task Seed_records_are_inserted_once_and_a_record_edited_between_starts_keeps_its_edit()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        using var folder = ApplicationFolder(TitleField, DoneField)
            .With("seeds/notes.json", SeedFile("""{ "title": "First", "done": false }""", """{ "title": "Second", "done": false }"""));

        var logs = new LogCollector();
        await using (var factory = CreateFactory(a, b, folder.Path, logs))
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/apps/{_name}/entities/Note/records/{_firstSeedId}")
            {
                Content = JsonContent.Create(new { version = 1, values = new { title = "Edited" } }),
            };
            request.Headers.Host = HostA;
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
            using var client = factory.CreateClient();
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
            using var client = factory.CreateClient();
            Assert.Equal(("First", false, 1L), await GetRecordAsync(client, HostA, _firstSeedId));
        }

        Assert.Contains(logs.Entries, entry => entry.Contains("Inserted 0 seed record(s) and updated 0 ", StringComparison.Ordinal));

        folder.With("seeds/notes.json", SeedFile("""{ "title": "Renamed", "done": false }""", """{ "title": "Second", "done": false }""", sync: true));
        logs = new LogCollector();
        await using (var factory = CreateFactory(a, b, folder.Path, logs))
        {
            using var client = factory.CreateClient();
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
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/apps/{_name}/entities/Note/records/{_firstSeedId}")
            {
                Content = JsonContent.Create(new { version = 1, values = new { done = true } }),
            };
            request.Headers.Host = HostA;
            using var response = await client.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        folder.With("seeds/notes.json", SeedFile("""{ "title": "Renamed" }""", secondValues: null, sync: true));
        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            using var client = factory.CreateClient();
            Assert.Equal(("Renamed", true, 3L), await GetRecordAsync(client, HostA, _firstSeedId));
            Assert.Equal(("Second", null, 1L), await GetRecordAsync(client, HostA, _secondSeedId));
        }

        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_noteId));
        Assert.Equal(2L, await ScalarAsync(a, $"SELECT count(*) FROM {table}"));
        Assert.Equal(2L, await ScalarAsync(b, $"SELECT count(*) FROM {table}"));
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
            using var client = factory.CreateClient();
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
    public async Task Without_the_setting_the_server_starts_and_leaves_the_tenant_database_unmigrated()
    {
        var (a, b) = (await CreateDatabaseAsync(), await CreateDatabaseAsync());
        await using var factory = CreateFactory(a, b, folder: null, new LogCollector());

        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DBNull.Value, await ScalarAsync(a, "SELECT to_regclass('axis.releases')::text"));
        Assert.Equal(DBNull.Value, await ScalarAsync(b, "SELECT to_regclass('axis.releases')::text"));
    }

    private static WebApplicationFactory<Program> CreateFactory(string a, string b, string? folder, LogCollector logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", a);
            builder.UseSetting("Tenants:a:Hosts:0", HostA);
            builder.UseSetting("Tenants:a:ConnectionString", a);
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", b);
            if (folder is not null)
            {
                builder.UseSetting("ActivateOnStartup:0", folder);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

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

    private async Task<string> CreateDatabaseAsync()
    {
        var name = $"startup_{Guid.NewGuid():N}";
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""CREATE DATABASE "{name}" """);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = name }.ConnectionString;
    }

    private TemporaryFolder ApplicationFolder(params string[] fields) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/note.json", NoteFile(fields));

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
