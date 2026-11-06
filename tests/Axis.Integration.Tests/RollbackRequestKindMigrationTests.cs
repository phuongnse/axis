using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Upgrading a tenant database renames the publish request kind <c>rollback</c> to <c>activate</c>, as D130 agrees, so a
/// queued request still runs and the console lists every row under the new name.
/// </summary>
public sealed class RollbackRequestKindMigrationTests
{
    private const string BeforeRename = "20261020085233_AddPublishRequestScenarios";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrating_a_database_with_a_rollback_request_renames_its_kind_to_activate_and_leaves_the_others()
    {
        var connectionString = await PostgreSqlServer.CreateDatabaseAsync("rollback_request_kind", CancellationToken);
        await using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        await context.GetService<IMigrator>().MigrateAsync(BeforeRename, CancellationToken);
        var rollback = Guid.CreateVersion7();
        var publish = Guid.CreateVersion7();
        var release = Guid.NewGuid();
        var application = Guid.NewGuid();

        // The rollback request is queued, so the publisher still has to run it after the upgrade.
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.publish_requests (id, application_id, kind, status, release_id, requested_by, requested_by_name, requested_at)
            VALUES ({rollback}, {application}, 'rollback', 'queued', {release}, 'author', 'Author User', clock_timestamp()),
                   ({publish}, {application}, 'publish', 'succeeded', {release}, 'author', 'Author User', clock_timestamp())
            """,
            CancellationToken);

        await context.Database.MigrateAsync(CancellationToken);

        // The rows come back ordered by kind, because two version 7 ids made in one millisecond have no fixed order.
        Assert.Equal(
            [(rollback, "activate", "queued"), (publish, "publish", "succeeded")],
            await RequestsAsync(connectionString));
    }

    private static async Task<IReadOnlyList<(Guid Id, string Kind, string Status)>> RequestsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, kind, status FROM axis.publish_requests ORDER BY kind", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(Guid, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }
}
