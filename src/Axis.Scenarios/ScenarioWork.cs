using Axis.Configuration.Packages;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Scenarios;

/// <summary>
/// Runs the due process step, connector call and package call work items of the scenario's tenant
/// in-process, as the worker would, until none is due. The call handlers are the real ones, with the
/// scenario's placeholder bindings. The scenario's stubs take the place of the HTTP request of a
/// connector call and of the package operation. A call that no stub answers fails the scenario
/// before it runs. Event deliveries and schedule triggers stay queued. The items run over the
/// scenario's connection string, so they read the scenario's <c>now</c> too.
/// </summary>
internal sealed class ScenarioWork : IDisposable
{
    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(5);

    private readonly string _connectionString;
    private readonly int _limit;
    private readonly ScenarioStubs _stubs;
    private readonly WorkItemRunner _runner;
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
        ];
        _runner = new WorkItemRunner(
            new ScenarioConnections(connectionString),
            new TenantContextAccessor(),
            [handler],
            loggerFactory.CreateLogger<WorkItemRunner>(),
            externalHandlers);
    }

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
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var ran = 0;
        while (true)
        {
            await CheckStubsAsync(cancellationToken);
            if (await _runner.RunNextAsync(ScenarioRunner.TenantId, _lease, cancellationToken))
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
            """
            SELECT c.step, c.connector, c.operation
            FROM axis.process_work_items w
            JOIN axis.connector_calls c ON c.id = w.connector_call_id
            WHERE w.tenant_id = @tenant AND w.due_at <= now() AND c.state = 'pending'
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
            """
            SELECT p.step, p.package, p.operation
            FROM axis.process_work_items w
            JOIN axis.package_calls p ON p.id = w.package_call_id
            WHERE w.tenant_id = @tenant AND w.due_at <= now() AND p.state = 'pending'
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

    // The application clock queued an item, and the database clock decides when it is due. When the
    // two differ slightly, a waiting item is made due at once, so a step is never skipped.
    private async Task<bool> MakeDueAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE axis.process_work_items SET due_at = now() WHERE tenant_id = @tenant AND kind = ANY(@kinds) AND due_at > now()",
            connection);
        command.Parameters.AddWithValue("tenant", ScenarioRunner.TenantId);
        command.Parameters.AddWithValue(
            "kinds",
            new[] { ProcessStarts.StepWorkItemKind, ProcessStarts.ConnectorCallWorkItemKind, ProcessStarts.PackageCallWorkItemKind });
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private sealed class ScenarioConnections(string connectionString) : ITenantConnectionFactory
    {
        public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
