using Axis.Configuration.Packages;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Scenarios;

/// <summary>
/// Runs the due process step work items of the scenario's tenant in-process, as the worker would,
/// until none is due. The scenario has no deployment bindings and no packages, so the process step
/// is the only kind it runs. Event deliveries, schedule triggers and calls to connectors and
/// packages stay queued. The items run over the scenario's connection string, so they read the
/// scenario's <c>now</c> too.
/// </summary>
internal sealed class ScenarioWork
{
    private static readonly TimeSpan _lease = TimeSpan.FromMinutes(5);

    private readonly string _connectionString;
    private readonly int _limit;
    private readonly WorkItemRunner _runner;

    public ScenarioWork(string connectionString, ILoggerFactory loggerFactory, int limit)
    {
        _connectionString = connectionString;
        _limit = limit;
        var handler = new ProcessStepHandler(
            new ReleaseModelCache(PackageCatalog.Empty), PackageCatalog.Empty, loggerFactory.CreateLogger<ProcessStepHandler>());
        _runner = new WorkItemRunner(
            new ScenarioConnections(connectionString), new TenantContextAccessor(), [handler], loggerFactory.CreateLogger<WorkItemRunner>());
    }

    /// <summary>
    /// Runs the due items until none is due. A step queues its next item as due at once, so the
    /// loop ends when the process waits or ends.
    /// </summary>
    /// <exception cref="StepFailedException">More items than the limit ran.</exception>
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var ran = 0;
        while (true)
        {
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

    // The application clock queued an item, and the database clock decides when it is due. When the
    // two differ slightly, a waiting item is made due at once, so a step is never skipped.
    private async Task<bool> MakeDueAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE axis.process_work_items SET due_at = now() WHERE tenant_id = @tenant AND kind = @kind AND due_at > now()",
            connection);
        command.Parameters.AddWithValue("tenant", ScenarioRunner.TenantId);
        command.Parameters.AddWithValue("kind", ProcessStarts.StepWorkItemKind);
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
