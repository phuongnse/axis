using Axis.Configuration.Tests;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Upgrading a tenant database that holds staged calls keeps every call and gives it the revision its
/// step ran at, because a step may now stage one call per pass of a loop.
/// </summary>
public sealed class CallStagedRevisionMigrationTests
{
    private const string BeforeStagedRevision = "20261023030525_AddWorkItemQueueOrder";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrating_a_database_with_staged_calls_keeps_them_with_the_revision_of_their_staging_history_row()
    {
        var connectionString = await PostgreSqlServer.CreateDatabaseAsync("call_staged_revision", CancellationToken);
        await using var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        await context.GetService<IMigrator>().MigrateAsync(BeforeStagedRevision, CancellationToken);
        var instance = Guid.NewGuid();
        var connectorCall = Guid.NewGuid();
        var packageCall = Guid.NewGuid();
        var unstagedCall = Guid.NewGuid();
        var release = Guid.NewGuid();

        // The instance is at revision 3. Its calls were staged by the run at revision 2, which is the one history row
        // that names each call as waiting. The call of the step 'other' has no such row.
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.process_instances (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
            VALUES ({instance}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {release}, 'waiting', 3, 'pay')
            """,
            CancellationToken);
        await InsertHistoryAsync(context, instance, "send", 1, """{"next":"pay"}""");
        await InsertHistoryAsync(context, instance, "send", 2, $$"""{"state":"waiting","operationId":"{{connectorCall}}"}""");
        await InsertHistoryAsync(context, instance, "pay", 2, $$"""{"state":"waiting","operationId":"{{packageCall}}"}""");
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.connector_calls (id, process_instance_id, step, release_id, connector, operation, state, attempts)
            VALUES ({connectorCall}, {instance}, 'send', {release}, 'Purchasing', 'createOrder', 'pending', 0),
                   ({unstagedCall}, {instance}, 'other', {release}, 'Purchasing', 'createOrder', 'pending', 0)
            """,
            CancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.package_calls (id, process_instance_id, step, release_id, package, version, operation, state, attempts)
            VALUES ({packageCall}, {instance}, 'pay', {release}, 'ledger', '1.0.0', 'post', 'pending', 0)
            """,
            CancellationToken);

        await context.Database.MigrateAsync(CancellationToken);

        Assert.Equal(
            [(connectorCall, 2L), (unstagedCall, 3L)],
            await StagedRevisionsAsync(connectionString, "connector_calls"));
        Assert.Equal([(packageCall, 2L)], await StagedRevisionsAsync(connectionString, "package_calls"));
    }

    private static async Task InsertHistoryAsync(ProcessesDbContext context, Guid instance, string step, long revision, string output) =>
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.process_step_history (id, process_instance_id, step, revision, input, output, started_at, finished_at)
            VALUES ({Guid.NewGuid()}, {instance}, {step}, {revision}, jsonb_build_object(), {output}::jsonb, now(), now())
            """,
            CancellationToken);

    private static async Task<IReadOnlyList<(Guid Id, long StagedRevision)>> StagedRevisionsAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT id, staged_revision FROM axis.{table} ORDER BY staged_revision", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(Guid, long)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetGuid(0), reader.GetInt64(1)));
        }

        return rows;
    }
}
