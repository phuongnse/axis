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
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration, data and processes migrations applied
/// and the StepApp fixture activated. It holds the worker's services, built from the worker's own
/// registration, with the host never started, so a test runs the work items itself.
/// </summary>
public sealed class ProcessStepFixture : IAsyncLifetime
{
    public const string TenantId = "a";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "StepApp");

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private ServiceProvider? _services;
    private ApplicationModel? _model;

    /// <summary>The compiled model of the StepApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The release of StepApp that the fixture activated first.</summary>
    public Guid FirstReleaseId { get; private set; }

    /// <summary>The worker's work item runner, with the process step handler registered.</summary>
    public WorkItemRunner Runner =>
        (_services ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();

    public NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await using (var admin = NpgsqlDataSource.Create(_database.ConnectionString))
        await using (var create = admin.CreateCommand("""CREATE DATABASE "steps_a" """))
        {
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = "steps_a" }.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        await using (var configuration = CreateConfigurationContext())
        await using (var data = CreateDataContext())
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_connectionString).Options))
        {
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();
            await processes.Database.MigrateAsync();
        }

        var (releaseId, model) = await ActivateAsync(_appFolder);
        FirstReleaseId = releaseId;
        _model = model;

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{TenantId}:Hosts:0"] = $"{TenantId}.example.test",
                [$"Tenants:{TenantId}:ConnectionString"] = _connectionString,
            })
            .Build();
        _services = new ServiceCollection().AddLogging().AddAxisWorker(settings).BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>Inserts a request with a new title, <paramref name="amount"/> and <paramref name="divisor"/>, and returns its id.</summary>
    public async Task<Guid> InsertRequestAsync(decimal amount, long divisor)
    {
        var id = Guid.NewGuid();
        await using var command = DataSource.CreateCommand(
            $"INSERT INTO {RequestTable} ({Column("id")}, {Column("title")}, {Column("amount")}, {Column("divisor")}) VALUES (@id, @title, @amount, @divisor)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Request {id:N}");
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("divisor", divisor);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Starts <paramref name="process"/> for the request <paramref name="subjectId"/>, pinned to
    /// <paramref name="releaseId"/>, as the start endpoint does: the instance is <c>running</c> at
    /// revision 1 on the process's start step, with its first work item due a minute ago. Returns
    /// the instance id.
    /// </summary>
    public async Task<Guid> StartAsync(string process, Guid subjectId, Guid releaseId)
    {
        Assert.True(Model.TryGetProcess(process, out var model));
        Assert.True(Model.TryGetEntity("Request", out var request));
        var instanceId = Guid.CreateVersion7();
        await using var connection = await DataSource.OpenConnectionAsync();
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
                ReleaseId = releaseId,
                State = ProcessStarts.Running,
                Revision = 1,
                Step = model.Start,
            }));
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            TenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            CancellationToken.None,
            processInstanceId: instanceId);
        await transaction.CommitAsync();
        return instanceId;
    }

    /// <summary>Claims and runs the tenant's due work items until none is left.</summary>
    public async Task RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        while (await Runner.RunNextAsync(TenantId, _lease, cancellationToken))
        {
        }
    }

    /// <summary>Claims and runs the tenant's next due work item. Returns <see langword="false"/> when there is none.</summary>
    public Task<bool> RunNextAsync(CancellationToken cancellationToken) =>
        Runner.RunNextAsync(TenantId, _lease, cancellationToken);

    /// <summary>
    /// Compiles and activates a copy of StepApp whose Review process names its end steps
    /// <c>reviewedV2</c> and <c>skippedV2</c>. Returns the new release's id.
    /// </summary>
    public async Task<Guid> ActivateRenamedReleaseAsync()
    {
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_appFolder, file);
            var content = await File.ReadAllTextAsync(file);
            if (string.Equals(Path.GetFileName(file), "review.json", StringComparison.Ordinal))
            {
                content = content
                    .Replace("\"reviewed\"", "\"reviewedV2\"", StringComparison.Ordinal)
                    .Replace("\"skipped\"", "\"skippedV2\"", StringComparison.Ordinal);
            }

            folder.With(relative, content);
        }

        var (releaseId, _) = await ActivateAsync(folder.Path);
        Assert.NotEqual(FirstReleaseId, releaseId);
        return releaseId;
    }

    /// <summary>Runs <paramref name="sql"/>, a <c>SELECT count(*)</c>, with the named parameters.</summary>
    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters) =>
        (long)(await ScalarAsync(sql, parameters))!;

    /// <summary>Runs <paramref name="sql"/> with the named parameters and returns the first column of the first row.</summary>
    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    /// <summary>The instance's state, revision, step and whether its end time is set.</summary>
    public async Task<(string State, long Revision, string Step, bool Ended)> InstanceAsync(Guid instanceId)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT state, revision, step, ended_at IS NOT NULL FROM axis.process_instances WHERE id = @id");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetBoolean(3));
    }

    /// <summary>The instance's history rows, in the order the steps started.</summary>
    public async Task<IReadOnlyList<HistoryRow>> HistoryAsync(Guid instanceId)
    {
        await using var command = DataSource.CreateCommand(
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

    /// <summary>The actions of the instance's audit records with the actor <c>system</c>, in the order they were written.</summary>
    public async Task<IReadOnlyList<string>> SystemAuditActionsAsync(Guid instanceId)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT action FROM axis.audit_records WHERE process_instance_id = @id AND actor = 'system' ORDER BY occurred_at, id");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        var actions = new List<string>();
        while (await reader.ReadAsync())
        {
            actions.Add(reader.GetString(0));
        }

        return actions;
    }

    private async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync(string folder)
    {
        await using var configuration = CreateConfigurationContext();
        await using var data = CreateDataContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data);
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

/// <summary>A row of a process instance's step history. <see cref="Next"/> is the output's <c>next</c>.</summary>
public sealed record HistoryRow(
    string Step,
    long Revision,
    string Input,
    string? Output,
    string? Next,
    string? Decision,
    string? Error,
    bool FinishedAfterStart);
