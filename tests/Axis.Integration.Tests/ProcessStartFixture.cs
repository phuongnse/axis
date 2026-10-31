using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration, data and processes migrations applied
/// and the ProcessApp fixture activated, served by the real server on host <c>a.example.test</c>.
/// The test authentication scheme signs in <c>requester</c>, who holds <c>requester</c>, and
/// <c>olga</c>, who holds <c>reader</c> and may also delete requests, and <c>sam</c>, who holds <c>status-reader</c>, may
/// inspect <c>Submit</c> and reads only the <c>status</c> field of a request, not its title. <see cref="Client"/> is signed in as <c>requester</c>.
/// </summary>
public sealed class ProcessStartFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ProcessApp");

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private ApplicationModel? _model;

    public HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    /// <summary>The compiled model of the ProcessApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The factory of the test host, for a test that needs a host with other settings.</summary>
    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    /// <summary>The release of ProcessApp that the fixture activated first.</summary>
    public Guid FirstReleaseId { get; private set; }

    private NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    /// <summary>A new connection to the tenant database. The caller disposes it.</summary>
    public ValueTask<NpgsqlConnection> OpenConnectionAsync() => DataSource.OpenConnectionAsync();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _connectionString = await _database.CreateDatabaseAsync("processes_a");
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        await using (var configuration = CreateConfigurationContext())
        await using (var data = CreateDataContext())
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_connectionString).Options))
        {
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();
            await processes.Database.MigrateAsync();
            await PostgreSqlServer.GrantRuntimeRoleAsync(_connectionString);
        }

        var (releaseId, model) = await ActivateAsync(_appFolder);
        FirstReleaseId = releaseId;
        _model = model;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseTestOidc("a");
            builder.UseTestAuthentication(
                new TestIdentity("requester", "Requester User", "requester"),
                new TestIdentity("olga", "Olga Reader", "reader"),
                new TestIdentity("sam", "Sam Status", "status-reader"));
        });
        _client = _factory.CreateClient().SignedInAs("requester");
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>
    /// Inserts a request with <paramref name="status"/> and a new title that starts with
    /// <paramref name="title"/>, and returns its id. The <c>requester</c> role reads only the
    /// requests whose title starts with <c>Request</c>.
    /// </summary>
    public async Task<Guid> InsertRequestAsync(string? status, string title = "Request")
    {
        var id = Guid.NewGuid();
        await using var command = DataSource.CreateCommand(
            $"INSERT INTO {RequestTable} ({Column("id")}, {Column("title")}, {Column("status")}) VALUES (@id, @title, @status)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"{title} {id:N}");
        command.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Sets the status of the request <paramref name="id"/>.</summary>
    public async Task SetStatusAsync(Guid id, string? status)
    {
        await using var command = DataSource.CreateCommand($"UPDATE {RequestTable} SET {Column("status")} = @status WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// Inserts a running instance of the <c>Submit</c> process for <paramref name="subject"/>, with
    /// no start through the API, and returns its id. The subject need not be a record the user may
    /// read, or a record at all.
    /// </summary>
    public async Task<Guid> InsertInstanceAsync(Guid subject)
    {
        Assert.True(Model.TryGetProcess("Submit", out var process));
        Assert.True(Model.TryGetEntity(process.Entity!.Name, out var entity));
        var id = Guid.CreateVersion7();
        await using var command = DataSource.CreateCommand(
            """
            INSERT INTO axis.process_instances (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
            VALUES (@id, @application, @process, @entity, @subject, @release, 'running', 1, @step)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", Model.Manifest.Id);
        command.Parameters.AddWithValue("process", process.Id);
        command.Parameters.AddWithValue("entity", entity.Id);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("release", FirstReleaseId);
        command.Parameters.AddWithValue("step", process.Start);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Moves the instance <paramref name="id"/> to <paramref name="state"/>, which ends it unless it is <c>running</c> or <c>waiting</c>.</summary>
    public async Task SetInstanceStateAsync(Guid id, string state)
    {
        await using var command = DataSource.CreateCommand(
            "UPDATE axis.process_instances SET state = @state, ended_at = CASE WHEN @state IN ('running', 'waiting') THEN NULL ELSE now() END WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", state);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    /// <summary>Completes every running or waiting instance of the process <paramref name="processId"/>, so that it can be started again.</summary>
    public async Task EndInstancesAsync(Guid processId)
    {
        await using var command = DataSource.CreateCommand(
            "UPDATE axis.process_instances SET state = 'completed', ended_at = now() WHERE process_id = @process AND state IN ('running', 'waiting')");
        command.Parameters.AddWithValue("process", processId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs <paramref name="sql"/>, a <c>SELECT count(*)</c>, with the named parameters.</summary>
    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Compiles and activates a copy of ProcessApp with one text changed, so its content hash and
    /// its release are new. Returns the new release's id.
    /// </summary>
    public async Task<Guid> ActivateChangedReleaseAsync()
    {
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(file);
            folder.With(Path.GetRelativePath(_appFolder, file), content.Replace(
                "This request cannot be submitted.",
                $"This request cannot be submitted ({Guid.NewGuid():N}).",
                StringComparison.Ordinal));
        }

        var (releaseId, _) = await ActivateAsync(folder.Path);
        Assert.NotEqual(FirstReleaseId, releaseId);
        return releaseId;
    }

    /// <summary>
    /// Compiles a copy of ProcessApp with a new <c>note</c> field and a migration that fills it, so
    /// the release has a pending migration, and activates it through the store that
    /// <paramref name="wrap"/> returns. Returns the new release's id once the activation commits.
    /// </summary>
    public async Task<Guid> ActivateMigratingReleaseAsync(Func<IActiveReleaseStore, IActiveReleaseStore> wrap)
    {
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_appFolder, file);
            var content = await File.ReadAllTextAsync(file);
            folder.With(
                relative,
                relative == Path.Combine("entities", "request.json")
                    ? content.Replace(
                        """{ "name": "status", "type": "text" }""",
                        """{ "name": "status", "type": "text" }, { "name": "note", "type": "text" }""",
                        StringComparison.Ordinal)
                    : content);
        }

        folder.With(
            "migrations/0001-fill-note.json",
            $$"""
            { "id": "{{Guid.NewGuid()}}", "kind": "migration", "name": "FillNote", "formatVersion": 1, "entity": "Request", "changes": [{ "type": "fill", "field": "note", "value": "'n/a'" }] }
            """);
        var (releaseId, _) = await ActivateAsync(folder.Path, wrap);
        return releaseId;
    }

    /// <summary>
    /// Activates a second application called <paramref name="name"/>, with a new id and one process without a subject,
    /// <c>NightlyJob</c>, with a new id. Returns its release and model.
    /// </summary>
    public async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateApplicationAsync(string name)
    {
        using var folder = new TemporaryFolder();
        folder.With(
            "application.json",
            $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "application", "name": "{{name}}", "formatVersion": 1 }""");
        var job = await File.ReadAllTextAsync(Path.Combine(_appFolder, "processes", "nightly-job.json"));
        folder.With(
            Path.Combine("processes", "nightly-job.json"),
            job.Replace("2f6b9c0e-4d1a-4e57-8b3c-5a7d9e1f3b24", Guid.NewGuid().ToString(), StringComparison.Ordinal));
        return await ActivateAsync(folder.Path);
    }

    /// <summary>The number of lock requests that wait on an advisory lock.</summary>
    public Task<long> CountWaitingAdvisoryLocksAsync() =>
        CountAsync("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted");

    /// <summary>The id of the release active for ProcessApp now.</summary>
    public async Task<Guid> ActiveReleaseIdAsync()
    {
        await using var configuration = CreateConfigurationContext();
        var active = await new ActiveReleaseStore(configuration).FindByNameAsync(Model.Manifest.Name);
        return Assert.IsType<ActiveRelease>(active).ReleaseId;
    }

    private async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync(string folder, Func<IActiveReleaseStore, IActiveReleaseStore>? wrap = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration);
        Assert.Empty(compiled.Diagnostics);
        IActiveReleaseStore store = new ActiveReleaseStore(configuration);
        var activated = await ReleaseActivator.ActivateAsync(compiled, wrap?.Invoke(store) ?? store, data, ActivationBindings.None("a"), OpenInstanceCounter.Instance);
        Assert.Empty(activated.Diagnostics);
        return (compiled.Release!.Id, compiled.Model!);
    }

    private string RequestTable
    {
        get
        {
            Assert.True(Model.TryGetEntity("Request", out var request));
            return EntityNaming.QualifiedTable(EntityNaming.Table(request.Id));
        }
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);

    private DataDbContext CreateDataContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(_connectionString).Options);
}
