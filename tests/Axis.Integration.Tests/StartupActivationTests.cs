using System.Net;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
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
    private async Task<Guid?[]> StartAsync(string a, string b, TemporaryFolder folder)
    {
        await using (var factory = CreateFactory(a, b, folder.Path, new LogCollector()))
        {
            factory.CreateClient().Dispose();
        }

        Guid?[] active = [(await ActiveAsync(a))?.ReleaseId, (await ActiveAsync(b))?.ReleaseId];
        Assert.All(active, id => Assert.NotNull(id));
        return active;
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

    private string NoteFile(params string[] fields) =>
        $$"""{ "id": "{{_noteId}}", "kind": "entity", "name": "Note", "formatVersion": 1, "fields": [{{string.Join(", ", fields)}}] }""";
}
