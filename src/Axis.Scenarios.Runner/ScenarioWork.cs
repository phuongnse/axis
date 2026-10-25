using Axis.Configuration.Packages;
using Axis.Expressions.Sql;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Scenarios.Runner;

/// <summary>
/// Runs the due process step, connector call, package call and event delivery work items of the
/// scenario's tenant in-process, as the worker would, until none is due. The items run in the order
/// they were queued, so a clock that steps back during a run cannot reorder them. The handlers are the real
/// ones, with the scenario's placeholder bindings, so an <c>async</c> event trigger starts its process
/// here. The scenario's stubs take the place of the HTTP request of a connector call and of the
/// package operation. A call that no stub answers fails the scenario before it runs. After an action,
/// schedule triggers, fan-outs and task deadlines stay queued, so none of them fires on the real clock.
/// <see cref="AdvanceAsync"/> runs them too, and the <c>task.deadline</c> items, after the scenario's <c>now</c> moved,
/// so a task expires only when the scenario's time reaches its deadline. The items run over
/// the scenario's connection string, so they read the scenario's <c>now</c> too, and a due time is
/// compared with it. Leases keep the database clock.
/// </summary>
internal sealed class ScenarioWork : IDisposable
{
    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(5);

    private string _connectionString;
    private readonly int _limit;
    private readonly ScenarioStubs _stubs;
    private readonly WorkItemRunner _runner;
    private readonly WorkItemRunner _timeRunner;
    private readonly ConnectorClient _client;
    private readonly PackageHttpTransport _transport;

    public ScenarioWork(string connectionString, ILoggerFactory loggerFactory, int limit, PackageCatalog packages, ScenarioStubs stubs)
    {
        _connectionString = connectionString;
        _limit = limit;
        _stubs = stubs;
        var models = new ReleaseModelCache(packages);
        var bindings = new ScenarioBindings();
        var settings = new ConnectorCallSettings(false);
        _client = new ConnectorClient(stubs.HttpHandler);
        _transport = new PackageHttpTransport();
        var handler = new ProcessStepHandler(models, packages, loggerFactory.CreateLogger<ProcessStepHandler>());
        IExternalWorkItemHandler[] externalHandlers =
        [
            new ConnectorCallHandler(models, bindings, _client, settings, loggerFactory.CreateLogger<ConnectorCallHandler>()),
            new PackageCallHandler(models, packages, bindings, settings, _transport, loggerFactory.CreateLogger<PackageCallHandler>(), stubs.CreateOperation),
            new EventDeliveryHandler(models, new DeliverySettings(5), loggerFactory.CreateLogger<EventDeliveryHandler>()),
        ];
        _runner = CreateRunner([handler], externalHandlers, loggerFactory);
        _timeRunner = CreateRunner(
            [handler, new TaskDeadlineHandler(models, loggerFactory.CreateLogger<TaskDeadlineHandler>())],
            [
                .. externalHandlers,
                new TriggerScheduleHandler(models, loggerFactory.CreateLogger<TriggerScheduleHandler>()),
                new TriggerFanOutHandler(models, loggerFactory.CreateLogger<TriggerFanOutHandler>()),
            ],
            loggerFactory);
    }

    private WorkItemRunner CreateRunner(IWorkItemHandler[] handlers, IExternalWorkItemHandler[] externalHandlers, ILoggerFactory loggerFactory) =>
        new(
            new ScenarioConnections(() => _connectionString),
            new TenantContextAccessor(),
            handlers,
            loggerFactory.CreateLogger<WorkItemRunner>(),
            externalHandlers)
        {
            InQueueOrder = true,
        };

    public void Dispose()
    {
        _client.Dispose();
        _transport.Dispose();
    }

    /// <summary>
    /// Runs the due items until none is due. A step queues its next item as due at once, so the
    /// loop ends when the process waits or ends.
    /// </summary>
    /// <exception cref="StepFailedException">More items than the limit ran, or a due call has no stub.</exception>
    public Task RunDueAsync(CancellationToken cancellationToken) => RunAsync(_runner, cancellationToken);

    /// <summary>
    /// Moves the work to <paramref name="connectionString"/>, whose <c>axis.now</c> is the new time of
    /// the scenario. Then runs the due items until none is due, <c>trigger.schedule</c> and
    /// <c>trigger.fanOut</c> included. The limit counts the whole step.
    /// </summary>
    /// <exception cref="StepFailedException">More items than the limit ran, or a due call has no stub.</exception>
    public Task AdvanceAsync(string connectionString, CancellationToken cancellationToken)
    {
        _connectionString = connectionString;
        return RunAsync(_timeRunner, cancellationToken);
    }

    private async Task RunAsync(WorkItemRunner runner, CancellationToken cancellationToken)
    {
        var ran = 0;
        while (true)
        {
            await CheckStubsAsync(cancellationToken);
            if (await runner.RunNextAsync(ScenarioRunner.TenantId, _lease, cancellationToken))
            {
                if (++ran > _limit)
                {
                    throw new StepFailedException($"more than {_limit} work items ran after the step");
                }

                continue;
            }

            if (!await MakeDueAsync(cancellationToken))
            {
                return;
            }
        }
    }

    // A call with no stub is found before it runs, which is the one point where the process step and
    // the operation are both known. Running it would change the instance for a call nobody planned.
    private async Task CheckStubsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
            $"""
            SELECT c.step, c.connector, c.operation
            FROM axis.process_work_items w
            JOIN axis.connector_calls c ON c.id = w.connector_call_id
            WHERE w.tenant_id = @tenant AND w.due_at <= {SqlTranslator.NowSql} AND c.state = 'pending'
            """,
            connection))
        {
            command.Parameters.AddWithValue("tenant", ScenarioRunner.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!_stubs.HasConnector(reader.GetString(1), reader.GetString(2)))
                {
                    throw new StepFailedException(
                        $"the process step '{reader.GetString(0)}' calls the connector operation '{reader.GetString(1)}/{reader.GetString(2)}', but no stub answers it");
                }
            }
        }

        await using (var command = new NpgsqlCommand(
            $"""
            SELECT p.step, p.package, p.operation
            FROM axis.process_work_items w
            JOIN axis.package_calls p ON p.id = w.package_call_id
            WHERE w.tenant_id = @tenant AND w.due_at <= {SqlTranslator.NowSql} AND p.state = 'pending'
            """,
            connection))
        {
            command.Parameters.AddWithValue("tenant", ScenarioRunner.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!_stubs.HasPackage(reader.GetString(1), reader.GetString(2)))
                {
                    throw new StepFailedException(
                        $"the process step '{reader.GetString(0)}' calls the package operation '{reader.GetString(1)}/{reader.GetString(2)}', but no stub answers it");
                }
            }
        }
    }

    // The application clock queued an item, and the scenario's time decides when it is due. When the
    // two differ slightly, a waiting item is made due at once, so a step is never skipped. A schedule
    // item is not among the kinds, so it waits for its occurrence.
    private async Task<bool> MakeDueAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"UPDATE axis.process_work_items SET due_at = {SqlTranslator.NowSql} WHERE tenant_id = @tenant AND kind = ANY(@kinds) AND due_at > {SqlTranslator.NowSql}",
            connection);
        command.Parameters.AddWithValue("tenant", ScenarioRunner.TenantId);
        command.Parameters.AddWithValue(
            "kinds",
            new[]
            {
                ProcessStarts.StepWorkItemKind,
                ProcessStarts.ConnectorCallWorkItemKind,
                ProcessStarts.PackageCallWorkItemKind,
                ProcessStarts.EventDeliverWorkItemKind,
            });
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private sealed class ScenarioConnections(Func<string> connectionString) : ITenantConnectionFactory
    {
        public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new NpgsqlConnection(connectionString());
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
