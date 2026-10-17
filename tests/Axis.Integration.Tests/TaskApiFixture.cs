using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Axis.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Integration.Tests;

/// <summary>
/// Two tenant databases, <c>a</c> and <c>b</c>, each with the configuration, data and processes
/// migrations applied and the StepApp fixture activated, served by the real server on hosts
/// <c>a.example.test</c> and <c>b.example.test</c>. The test authentication scheme signs in
/// <c>maria</c> and <c>fiona</c>, who hold the role <c>finance</c>, <c>rita</c>, who holds
/// <c>reviewer</c>, and <c>otto</c>, who holds <c>employee</c>, which StepApp does not declare.
/// The fixture also holds the worker's services for both tenants, with the host never started, so
/// a test runs the work items that create tasks itself.
/// </summary>
public sealed class TaskApiFixture : IAsyncLifetime
{
    public const string TenantA = "a";

    public const string TenantB = "b";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "StepApp");

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlFixture _database = new();
    private readonly Dictionary<string, Tenant> _tenants = new(StringComparer.Ordinal);
    private WebApplicationFactory<Program>? _factory;
    private ServiceProvider? _services;
    private ApplicationModel? _model;

    /// <summary>The compiled model of the StepApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>
    /// The release the StepApp fixture was first activated as on <paramref name="tenant"/>, which
    /// <see cref="StartAsync"/> pins every instance to.
    /// </summary>
    public Guid ReleaseId(string tenant) => _tenants[tenant].ReleaseId;

    /// <summary>The host of <paramref name="tenant"/>.</summary>
    public static string Host(string tenant) => $"{tenant}.example.test";

    /// <summary>The title <see cref="InsertRequestAsync"/> gives the request <paramref name="id"/>.</summary>
    public static string RequestTitle(Guid id) => $"Request {id:N}";

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            _tenants[tenant] = await CreateTenantAsync($"tasks_{tenant}");
        }

        var a = _tenants[TenantA];
        var b = _tenants[TenantB];
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(a.ConnectionString));
            builder.UseSetting("Tenants:a:Hosts:0", Host(TenantA));
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(a.ConnectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", a.ConnectionString);
            builder.UseSetting("Tenants:b:Hosts:0", Host(TenantB));
            builder.UseSetting("Tenants:b:ConnectionString", PostgreSqlServer.RuntimeConnectionString(b.ConnectionString));
            builder.UseSetting("Tenants:b:SchemaConnectionString", b.ConnectionString);
            builder.UseTestOidc(TenantA, TenantB);
            builder.UseTestAuthentication(
                new TestIdentity("maria", "Maria Manager", "finance"),
                new TestIdentity("fiona", "Fiona Finance", "finance"),
                new TestIdentity("rita", "Rita Reviewer", "reviewer"),
                new TestIdentity("otto", "Otto Other", "employee"));
        });

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{TenantA}:Hosts:0"] = Host(TenantA),
                [$"Tenants:{TenantA}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(a.ConnectionString),
                [$"Tenants:{TenantB}:Hosts:0"] = Host(TenantB),
                [$"Tenants:{TenantB}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(b.ConnectionString),
            })
            .Build();
        _services = new ServiceCollection().AddLogging().AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing" }).BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        foreach (var tenant in _tenants.Values)
        {
            await tenant.DataSource.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>Inserts a department of <paramref name="tenant"/> with a new name and <paramref name="manager"/>, and returns its id.</summary>
    public async Task<Guid> InsertDepartmentAsync(string tenant, string? manager)
    {
        Assert.True(Model.TryGetEntity("Department", out var department));
        var id = Guid.NewGuid();
        await using var command = DataSource(tenant).CreateCommand(
            $"""
            INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(department.Id))} ({Column("id")}, {Column("name")}, {Column("manager")})
            VALUES (@id, @name, @manager)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", $"Department {id:N}");
        command.Parameters.Add(new NpgsqlParameter("manager", NpgsqlDbType.Text) { Value = (object?)manager ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Inserts a request of <paramref name="tenant"/> titled <see cref="RequestTitle"/> with an
    /// optional <paramref name="department"/>, and returns its id. The id is <paramref name="id"/>,
    /// or a new one.
    /// </summary>
    public async Task<Guid> InsertRequestAsync(string tenant, Guid? department, Guid? id = null)
    {
        Assert.True(Model.TryGetEntity("Request", out var request));
        var requestId = id ?? Guid.NewGuid();
        await using var command = DataSource(tenant).CreateCommand(
            $"""
            INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} ({Column("id")}, {Column("title")}, {Column("department")})
            VALUES (@id, @title, @department)
            """);
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("title", RequestTitle(requestId));
        command.Parameters.Add(new NpgsqlParameter("department", NpgsqlDbType.Uuid) { Value = (object?)department ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
        return requestId;
    }

    /// <summary>Deletes the request <paramref name="id"/> of <paramref name="tenant"/>.</summary>
    public async Task DeleteRequestAsync(string tenant, Guid id)
    {
        Assert.True(Model.TryGetEntity("Request", out var request));
        await using var command = DataSource(tenant).CreateCommand(
            $"DELETE FROM {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// Inserts the request <paramref name="id"/> of <paramref name="tenant"/> titled
    /// <see cref="RequestTitle"/>, with <paramref name="code"/> in a <c>code</c> field that only a
    /// later release declares.
    /// </summary>
    public async Task InsertRequestWithCodeAsync(string tenant, Guid id, string code)
    {
        Assert.True(Model.TryGetEntity("Request", out var request));
        await using var command = DataSource(tenant).CreateCommand(
            $"""
            INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} ({Column("id")}, {Column("title")}, {Column("code")})
            VALUES (@id, @title, @code)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", RequestTitle(id));
        command.Parameters.AddWithValue("code", code);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Starts <paramref name="process"/> in <paramref name="tenant"/> for the request
    /// <paramref name="subjectId"/>, pinned to <paramref name="releaseId"/> or else the tenant's first StepApp release, as the start endpoint
    /// does: the instance is <c>running</c> at revision 1 on the process's start step, with its
    /// first work item due a minute ago. Returns the instance id.
    /// </summary>
    public async Task<Guid> StartAsync(string tenant, string process, Guid subjectId, Guid? releaseId = null)
    {
        Assert.True(Model.TryGetProcess(process, out var model));
        Assert.True(Model.TryGetEntity("Request", out var request));
        var instanceId = Guid.CreateVersion7();
        await using var connection = await DataSource(tenant).OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Assert.True(await ProcessStarts.TryInsertInstanceAsync(
            transaction,
            new ProcessInstanceRow
            {
                Id = instanceId,
                ApplicationId = Model.Manifest.Id,
                ProcessId = model.Id,
                SubjectEntityId = request.Id,
                SubjectId = subjectId,
                ReleaseId = releaseId ?? _tenants[tenant].ReleaseId,
                State = ProcessStarts.Running,
                Revision = 1,
                Step = model.Start,
            }));
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenant,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            CancellationToken.None,
            processInstanceId: instanceId);
        await transaction.CommitAsync();
        return instanceId;
    }

    /// <summary>A service of the real server, such as a model cache.</summary>
    public T ServerService<T>() where T : notnull =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).Services.GetRequiredService<T>();

    /// <summary>A service of the worker's container, such as a model cache.</summary>
    public T WorkerService<T>() where T : notnull =>
        (_services ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<T>();

    /// <summary>Claims and runs the due work items of <paramref name="tenant"/> until none is left.</summary>
    public async Task RunUntilIdleAsync(string tenant, CancellationToken cancellationToken)
    {
        var runner = (_services ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        while (await runner.RunNextAsync(tenant, _lease, cancellationToken))
        {
        }
    }

    /// <summary>The id of the one task of the instance <paramref name="instanceId"/> in <paramref name="tenant"/>.</summary>
    public async Task<Guid> TaskIdAsync(string tenant, Guid instanceId)
    {
        await using var command = DataSource(tenant).CreateCommand("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The instance has no task.");
        var id = reader.GetGuid(0);
        Assert.False(await reader.ReadAsync(), "The instance has more than one task.");
        return id;
    }

    /// <summary>The <c>computed_release_id</c> of the Request <paramref name="requestId"/> in <paramref name="tenant"/>, or null when it has no stamp.</summary>
    public async Task<Guid?> ComputedReleaseAsync(string tenant, Guid requestId)
    {
        Assert.True(Model.TryGetEntity("Request", out var request));
        await using var command = DataSource(tenant).CreateCommand(
            $"SELECT {EntityNaming.Quote(EntityNaming.ComputedReleaseColumn)} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", requestId);
        return await command.ExecuteScalarAsync() is Guid stamp ? stamp : null;
    }

    /// <summary>
    /// The state of the instance <paramref name="instanceId"/> in <paramref name="tenant"/>, its
    /// task and its subject record, and the number of its audit records, history rows and work items.
    /// The audit records count those of the instance and of its subject record.
    /// </summary>
    public async Task<ProcessSnapshot> SnapshotAsync(string tenant, Guid instanceId)
    {
        Assert.True(Model.TryGetEntity("Request", out var request));
        await using var command = DataSource(tenant).CreateCommand(
            $"""
            SELECT i.state, i.step, i.revision,
                   (SELECT t.state FROM axis.process_tasks t WHERE t.process_instance_id = i.id),
                   (SELECT count(*) FROM axis.audit_records a WHERE a.process_instance_id = i.id OR a.record_id = i.subject_id),
                   (SELECT count(*) FROM axis.process_step_history h WHERE h.process_instance_id = i.id),
                   (SELECT count(*) FROM axis.process_work_items w WHERE w.process_instance_id = i.id),
                   (SELECT r.{Column("version")} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} r WHERE r.{Column("id")} = i.subject_id)
            FROM axis.process_instances i WHERE i.id = @id
            """);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The instance does not exist.");
        return new ProcessSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7));
    }

    /// <summary>
    /// The actor, the stored actor name and the details of each audit record of
    /// <paramref name="action"/> of the instance <paramref name="instanceId"/>, oldest first.
    /// </summary>
    public async Task<IReadOnlyList<(string Actor, string? ActorName, string Details)>> AuditRecordsAsync(string tenant, Guid instanceId, string action)
    {
        await using var command = DataSource(tenant).CreateCommand(
            "SELECT actor, actor_name, details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action ORDER BY id");
        command.Parameters.AddWithValue("id", instanceId);
        command.Parameters.AddWithValue("action", action);
        var rows = new List<(string, string?, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    /// <summary>The actor and the stored actor name of each audit record of the record <paramref name="recordId"/>, oldest first.</summary>
    public async Task<IReadOnlyList<(string Actor, string? ActorName)>> ActorsAsync(string tenant, Guid recordId)
    {
        await using var command = DataSource(tenant).CreateCommand(
            "SELECT actor, actor_name FROM axis.audit_records WHERE record_id = @record ORDER BY occurred_at, id");
        command.Parameters.AddWithValue("record", recordId);
        var rows = new List<(string, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>The step history rows of the instance <paramref name="instanceId"/> in <paramref name="tenant"/>, oldest first.</summary>
    public async Task<IReadOnlyList<HistoryRow>> HistoryAsync(string tenant, Guid instanceId)
    {
        await using var command = DataSource(tenant).CreateCommand(
            """
            SELECT step, revision, input::text, output::text, output->>'next', decision, error, started_at <= finished_at
            FROM axis.process_step_history WHERE process_instance_id = @id ORDER BY started_at, finished_at
            """);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<HistoryRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new HistoryRow(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetBoolean(7)));
        }

        return rows;
    }

    /// <summary>
    /// Compiles and activates a copy of the StepApp fixture on <paramref name="tenant"/> as a new
    /// release, with <paramref name="edit"/> applied to each file. It gets the file's path relative
    /// to the folder and its content, and returns the new content, or null to leave the file out.
    /// Returns the new release's id.
    /// </summary>
    public async Task<Guid> ActivateEditedAsync(string tenant, Func<string, string, string?> edit)
    {
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(_appFolder, file);
            if (edit(path, await File.ReadAllTextAsync(file)) is { } content)
            {
                folder.With(path, content);
            }
        }

        await using var connection = new NpgsqlConnection(_tenants[tenant].ConnectionString);
        await connection.OpenAsync();
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, configuration);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ProcessStepFixture.StepAppBindings("default"), OpenInstanceCounter.Instance);
        Assert.Empty(activated.Diagnostics);
        return compiled.Release!.Id;
    }

    /// <summary>
    /// Compiles the fixture folder <paramref name="fixture"/>, such as <c>SecondApp</c>, on
    /// <paramref name="tenant"/> without activating it. Returns the id of the stored release.
    /// </summary>
    public async Task<Guid> CompileFixtureAsync(string tenant, string fixture)
    {
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_tenants[tenant].ConnectionString).Options);
        var compiled = await ReleaseCompiler.CompileAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), configuration);
        Assert.Empty(compiled.Diagnostics);
        return compiled.Release!.Id;
    }

    private NpgsqlDataSource DataSource(string tenant) => _tenants[tenant].DataSource;

    private async Task<Tenant> CreateTenantAsync(string databasePrefix)
    {
        var connectionString = await _database.CreateDatabaseAsync(databasePrefix);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options))
        {
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();
            await processes.Database.MigrateAsync();
            await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString);
        }

        var compiled = await ReleaseCompiler.CompileAsync(_appFolder, configuration);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ProcessStepFixture.StepAppBindings("default"), OpenInstanceCounter.Instance);
        Assert.Empty(activated.Diagnostics);
        _model ??= compiled.Model;
        return new Tenant(connectionString, NpgsqlDataSource.Create(connectionString), compiled.Release!.Id);
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field switch
        {
            "id" => EntityNaming.IdColumn,
            "version" => EntityNaming.VersionColumn,
            _ => EntityNaming.Column(field),
        });

    private sealed record Tenant(string ConnectionString, NpgsqlDataSource DataSource, Guid ReleaseId);
}

/// <summary>
/// An instance's state, step and revision, its task's state, its subject record's version, and the
/// number of its audit records, history rows and work items.
/// </summary>
public sealed record ProcessSnapshot(
    string State,
    string Step,
    long Revision,
    string? TaskState,
    long AuditRecords,
    long HistoryRows,
    long WorkItems,
    long SubjectVersion);
