using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Audit;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Record = Axis.Data.Records.Record;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c> unless another tenant is given, with the configuration, data and
/// processes migrations applied and the StepApp fixture activated. It holds the worker's services,
/// built from the worker's own registration with no deployment bindings, with the host never
/// started, so a test runs the work items itself.
/// </summary>
public sealed class ProcessStepFixture : IAsyncLifetime
{
    public const string TenantId = "a";

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan _lockWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlFixture _database = new();
    private readonly string _tenantId;
    private readonly string _appFolder;
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private ServiceProvider? _services;
    private ApplicationModel? _model;

    public ProcessStepFixture()
        : this(TenantId)
    {
    }

    internal ProcessStepFixture(string tenantId)
        : this(tenantId, StepAppFolder)
    {
    }

    /// <summary>A fixture that activates the application in <paramref name="appFolder"/> first, in place of StepApp.</summary>
    internal ProcessStepFixture(string tenantId, string appFolder)
    {
        _tenantId = tenantId;
        _appFolder = appFolder;
    }

    /// <summary>The folder of the StepApp fixture application.</summary>
    internal static string StepAppFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "StepApp");

    /// <summary>The tenant whose database the fixture holds.</summary>
    public string Tenant => _tenantId;

    /// <summary>The compiled model of the StepApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The release of StepApp that the fixture activated first.</summary>
    public Guid FirstReleaseId { get; private set; }

    /// <summary>Every log entry the worker's services wrote.</summary>
    internal LogCollector Logs { get; } = new();

    /// <summary>The worker's services.</summary>
    internal IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The worker's work item runner, with the process step handler registered.</summary>
    public WorkItemRunner Runner =>
        (_services ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();

    /// <summary>The connection string of the tenant database, for a test that builds its own worker hosts.</summary>
    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("The fixture is not initialized.");

    public NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _connectionString = await _database.CreateDatabaseAsync($"steps_{_tenantId}");
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

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{_tenantId}:Hosts:0"] = $"{_tenantId}.example.test",
                [$"Tenants:{_tenantId}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(_connectionString),
            })
            .Build();
        _services = new ServiceCollection().AddLogging(logging => logging.AddProvider(Logs)).AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing" }).BuildServiceProvider();
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

    /// <summary>
    /// The deployment bindings StepApp needs on <paramref name="tenantId"/>: the base URL of its
    /// Purchasing connector and the secret that holds its bearer token.
    /// </summary>
    public static ActivationBindings StepAppBindings(string tenantId) =>
        new(
            tenantId,
            new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
            {
                ["PurchasingUrl"] = new(ActivationBinding.UrlKind, new Uri("https://purchasing.example.test")),
                ["PurchasingToken"] = new(ActivationBinding.SecretKind, null),
            },
            AllowHttp: false);

    /// <summary>
    /// <see cref="StepAppBindings"/> and the bindings the <c>describe</c> and <c>probe</c> operations of
    /// <c>test-package</c> declare: <c>TestUrl</c>, a url binding holding <paramref name="testUrl"/>, and
    /// the secret <c>TestToken</c>.
    /// </summary>
    public static ActivationBindings PackageBindings(string tenantId, Uri testUrl, bool allowHttp)
    {
        var bindings = new Dictionary<string, ActivationBinding>(StepAppBindings(tenantId).Bindings, StringComparer.OrdinalIgnoreCase)
        {
            ["TestUrl"] = new(ActivationBinding.UrlKind, testUrl),
            ["TestToken"] = new(ActivationBinding.SecretKind, null),
        };
        return new ActivationBindings(tenantId, bindings, allowHttp);
    }

    /// <summary>
    /// Inserts a request with a new title, <paramref name="amount"/>, <paramref name="divisor"/> and
    /// an optional <paramref name="department"/>, and returns its id.
    /// </summary>
    public async Task<Guid> InsertRequestAsync(decimal? amount, long divisor, Guid? department = null)
    {
        var id = Guid.NewGuid();
        await using var command = DataSource.CreateCommand(
            $"""
            INSERT INTO {RequestTable} ({Column("id")}, {Column("title")}, {Column("amount")}, {Column("divisor")}, {Column("department")})
            VALUES (@id, @title, @amount, @divisor, @department)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Request {id:N}");
        command.Parameters.Add(new NpgsqlParameter("amount", NpgsqlDbType.Numeric) { Value = (object?)amount ?? DBNull.Value });
        command.Parameters.AddWithValue("divisor", divisor);
        command.Parameters.Add(new NpgsqlParameter("department", NpgsqlDbType.Uuid) { Value = (object?)department ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Inserts <paramref name="count"/> requests with new titles, <paramref name="amount"/> and
    /// <paramref name="divisor"/> and no department, in one statement.
    /// </summary>
    public async Task InsertRequestsAsync(int count, decimal amount, long divisor)
    {
        await using var command = DataSource.CreateCommand(
            $"""
            INSERT INTO {RequestTable} ({Column("id")}, {Column("title")}, {Column("amount")}, {Column("divisor")}, {Column("department")})
            SELECT gen_random_uuid(), 'Request ' || n, @amount, @divisor, NULL FROM generate_series(1, @count) AS n
            """);
        command.Parameters.AddWithValue("count", count);
        command.Parameters.Add(new NpgsqlParameter("amount", NpgsqlDbType.Numeric) { Value = amount });
        command.Parameters.AddWithValue("divisor", divisor);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Inserts a department with a new name and <paramref name="manager"/>, and returns its id.</summary>
    public async Task<Guid> InsertDepartmentAsync(string? manager)
    {
        Assert.True(Model.TryGetEntity("Department", out var department));
        var id = Guid.NewGuid();
        await using var command = DataSource.CreateCommand(
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

    /// <summary>Reads the request <paramref name="id"/> as the record API does.</summary>
    public async Task<Record> RequestAsync(Guid id)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        return await RecordQueries.GetAsync(connection, Model, RequestEntity, id)
            ?? throw new InvalidOperationException($"No request has the id '{id}'.");
    }

    /// <summary>
    /// Sets the title of the request <paramref name="id"/> at <paramref name="version"/> in
    /// <paramref name="transaction"/>, as a user's record update does, with its <c>record.updated</c>
    /// audit record by the user <c>anna</c>. Returns the new version.
    /// </summary>
    public async Task<long> UpdateTitleAsync(NpgsqlTransaction transaction, Guid id, long version, string title)
    {
        Assert.True(RequestEntity.TryGetField("title", out var field));
        var result = await RecordCommands.UpdateAsync(transaction.Connection!, Model, RequestEntity, FirstReleaseId, id, version, [new RecordValue(field, title)], []);
        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        var written = result.Record!.Version;
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                "anna",
                AuditActions.RecordUpdated,
                Model.Manifest.Id,
                RequestEntity.Id,
                id,
                ProcessInstanceId: null,
                new JsonObject { ["version"] = written, ["fields"] = new JsonArray("title") }));
        return written;
    }

    /// <summary>
    /// Starts <paramref name="process"/> for the request <paramref name="subjectId"/>, pinned to
    /// <paramref name="releaseId"/>, as the start endpoint does: the instance is <c>running</c> at
    /// revision 1 on the process's start step, with its first work item due a minute ago. Returns
    /// the instance id. The process comes from <paramref name="applicationModel"/>, or from StepApp
    /// when none is given.
    /// </summary>
    public async Task<Guid> StartAsync(string process, Guid subjectId, Guid releaseId, ApplicationModel? applicationModel = null)
    {
        applicationModel ??= Model;
        Assert.True(applicationModel.TryGetProcess(process, out var model));
        Assert.True(applicationModel.TryGetEntity("Request", out var request));
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
            _tenantId,
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
        while (await Runner.RunNextAsync(_tenantId, _lease, cancellationToken))
        {
        }
    }

    /// <summary>Claims and runs the tenant's next due work item. Returns <see langword="false"/> when there is none.</summary>
    public Task<bool> RunNextAsync(CancellationToken cancellationToken) =>
        Runner.RunNextAsync(_tenantId, _lease, cancellationToken);

    /// <summary>
    /// Claims and runs the tenant's next due <c>process.step</c> item, so no staged connector call
    /// is sent. Returns <see langword="false"/> when there is none.
    /// </summary>
    public async Task<bool> RunNextStepAsync(CancellationToken cancellationToken)
    {
        var item = await ClaimAsync(ProcessStarts.StepWorkItemKind, _lease, cancellationToken);
        if (item is null)
        {
            return false;
        }

        await Runner.ExecuteAsync(item, cancellationToken);
        return true;
    }

    /// <summary>Claims the tenant's next due item of <paramref name="kind"/> for <paramref name="lease"/>, or returns <see langword="null"/>.</summary>
    public async Task<ClaimedWorkItem?> ClaimAsync(string kind, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var connection = await DataSource.OpenConnectionAsync(cancellationToken);
        return await WorkItemQueue.ClaimAsync(connection, _tenantId, [kind], lease, cancellationToken);
    }

    /// <summary>
    /// Compiles and activates a copy of StepApp whose Review process names its end steps
    /// <c>reviewedV2</c> and <c>skippedV2</c>. Returns the new release's id.
    /// </summary>
    public async Task<Guid> ActivateRenamedReleaseAsync()
    {
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(StepAppFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(StepAppFolder, file);
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

    /// <summary>
    /// Compiles and activates a copy of StepApp with one more file, <paramref name="relativePath"/>
    /// holding <paramref name="content"/>. Returns the new release's id.
    /// </summary>
    public async Task<Guid> ActivateCopyWithAsync(string relativePath, string content)
    {
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(StepAppFolder, "*", SearchOption.AllDirectories))
        {
            folder.With(Path.GetRelativePath(StepAppFolder, file), await File.ReadAllTextAsync(file));
        }

        folder.With(relativePath, content);
        var (releaseId, _) = await ActivateAsync(folder.Path);
        Assert.NotEqual(FirstReleaseId, releaseId);
        return releaseId;
    }

    /// <summary>Waits until one session of the tenant database waits on a lock, and fails when <paramref name="run"/> ends first.</summary>
    public async Task WaitUntilBlockedOnLockAsync(Task run, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _lockWaitTimeout;
        while (await AdminCountAsync(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") != 1)
        {
            Assert.False(run.IsCompleted, "The step ended before it waited on a row lock.");
            Assert.True(DateTime.UtcNow < deadline, "The step never waited on a row lock.");
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    /// <summary>
    /// Runs <paramref name="sql"/>, a <c>SELECT count(*)</c>, on the tenant database as the PostgreSQL superuser,
    /// which is the only role that sees or ends the sessions of other roles.
    /// </summary>
    public async Task<long> AdminCountAsync(string sql) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(ConnectionString, sql))!;

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

    /// <summary>The instance's tasks, in the order they were created.</summary>
    public async Task<IReadOnlyList<TaskRow>> TasksAsync(Guid instanceId)
    {
        await using var command = DataSource.CreateCommand(
            """
            SELECT id, state, assignee_kind, assignee, form_id, due_at, created_at, due_at - created_at
            FROM axis.process_tasks WHERE process_instance_id = @id ORDER BY created_at, id
            """);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<TaskRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new TaskRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetTimeSpan(7)));
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

    /// <summary>The state rows of the schedule triggers, by trigger id.</summary>
    internal async Task<IReadOnlyList<TriggerScheduleState>> TriggerSchedulesAsync()
    {
        await using var command = DataSource.CreateCommand(
            "SELECT trigger_id, application_id, release_id, next_occurrence_at, last_occurrence_at FROM axis.trigger_schedules ORDER BY trigger_id");
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<TriggerScheduleState>();
        while (await reader.ReadAsync())
        {
            rows.Add(new TriggerScheduleState(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return rows;
    }

    /// <summary>The <c>trigger.schedule</c> work items: the trigger each waits for and when it is due.</summary>
    internal async Task<IReadOnlyList<(Guid TriggerId, DateTimeOffset DueAt)>> ScheduleItemsAsync()
    {
        await using var command = DataSource.CreateCommand(
            "SELECT trigger_id, due_at FROM axis.process_work_items WHERE kind = @kind ORDER BY trigger_id");
        command.Parameters.AddWithValue("kind", ProcessStarts.TriggerScheduleWorkItemKind);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(Guid, DateTimeOffset)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return rows;
    }

    /// <summary>Queues a <c>trigger.fanOut</c> item for the trigger's occurrence, due a minute ago.</summary>
    internal async Task EnqueueFanOutAsync(Guid triggerId, Guid releaseId, DateTimeOffset occurrenceAt)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction: null,
            _tenantId,
            ProcessStarts.TriggerFanOutWorkItemKind,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            CancellationToken.None,
            triggerId: triggerId,
            releaseId: releaseId,
            occurrenceAt: occurrenceAt);
    }

    private Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync(string folder) => ActivateAsync(folder, []);

    /// <summary>
    /// Compiles the application in <paramref name="folder"/> against <paramref name="packages"/> and activates it.
    /// Activation recomputes at most <paramref name="inlineRecomputeLimit"/> rows of an entity inline and leaves
    /// more to the backfill. The schedules are written as of <paramref name="activatedAt"/> when it is given.
    /// Returns the release's id and model.
    /// </summary>
    internal async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync(
        string folder,
        IReadOnlyCollection<PackageVersion> packages,
        ActivationBindings? bindings = null,
        int inlineRecomputeLimit = ReleaseActivator.DefaultInlineRecomputeLimit,
        DateTimeOffset? activatedAt = null)
    {
        var (compiled, activated) = await TryActivateAsync(folder, packages, bindings ?? StepAppBindings(_tenantId), inlineRecomputeLimit);
        Assert.Empty(activated.Diagnostics);
        if (activatedAt is null)
        {
            await using var connection = await DataSource.OpenConnectionAsync();
            await TriggerSchedules.ActivateAsync(connection, _tenantId, compiled.Model!, compiled.Release!.Id, CancellationToken.None);
        }
        else
        {
            // A session setting on a pooled connection could leak into later statements, so the fixed time gets its own connection.
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
            {
                Pooling = false,
                Options = "-c axis.now=" + activatedAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
            };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await TriggerSchedules.ActivateAsync(connection, _tenantId, compiled.Model!, compiled.Release!.Id, CancellationToken.None);
        }

        return (compiled.Release!.Id, compiled.Model!);
    }

    /// <summary>
    /// Compiles the application in <paramref name="folder"/> against <paramref name="packages"/>, which must
    /// give no diagnostic, and tries to activate it with <paramref name="bindings"/>. Returns the compilation
    /// and the activation result, which may hold diagnostics. It writes no schedules.
    /// </summary>
    internal async Task<(ReleaseCompilationResult Compiled, ActivationResult Activated)> TryActivateAsync(
        string folder,
        IReadOnlyCollection<PackageVersion> packages,
        ActivationBindings bindings,
        int inlineRecomputeLimit = ReleaseActivator.DefaultInlineRecomputeLimit)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, packages);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance, inlineRecomputeLimit);
        return (compiled, activated);
    }

    private EntityModel RequestEntity
    {
        get
        {
            Assert.True(Model.TryGetEntity("Request", out var request));
            return request;
        }
    }

    private string RequestTable => EntityNaming.QualifiedTable(EntityNaming.Table(RequestEntity.Id));

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);

    private DataDbContext CreateDataContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(_connectionString).Options);
}

/// <summary>A row of <c>axis.trigger_schedules</c>.</summary>
internal sealed record TriggerScheduleState(
    Guid TriggerId,
    Guid ApplicationId,
    Guid ReleaseId,
    DateTimeOffset NextOccurrenceAt,
    DateTimeOffset? LastOccurrenceAt);

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

/// <summary>A row of a process instance's tasks. <see cref="DueIn"/> is its <c>due_at</c> minus its <c>created_at</c>.</summary>
public sealed record TaskRow(
    Guid Id,
    string State,
    string AssigneeKind,
    string Assignee,
    Guid FormId,
    DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt,
    TimeSpan? DueIn);
