using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A work item past <c>Worker:StatementTimeout</c> fails that attempt and is retried like any other failure of its kind.
/// StepApp's <c>Announce</c> raises <c>RequestNoted</c>, and two triggers start a process for it. A lock on
/// <c>axis.trigger_starts</c>, which only event deliveries and schedule fan-outs touch, holds each of their
/// attempts until the limit. The class has its own fixture, so the lock cannot affect other delivery tests.
/// </summary>
public sealed class WorkItemStatementTimeoutTests(EventDeliveryFixture fixture) : IClassFixture<EventDeliveryFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delivery_past_the_statement_limit_counts_a_failure_per_trigger_and_is_queued_again_after_its_backoff()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertRequestAsync(4_123_456.75m, 4);
        var announceId = await tenant.StartAsync("Announce", subjectId, tenant.FirstReleaseId);

        Guid eventId;
        await using (var blocker = await tenant.DataSource.OpenConnectionAsync(CancellationToken))
        await using (var transaction = await blocker.BeginTransactionAsync(CancellationToken))
        {
            await using (var command = new NpgsqlCommand("LOCK TABLE axis.trigger_starts IN ACCESS EXCLUSIVE MODE", blocker, transaction))
            {
                await command.ExecuteNonQueryAsync(CancellationToken);
            }

            await fixture.RunUntilIdleAsync("a", TimeSpan.FromMilliseconds(500), CancellationToken);
            await transaction.RollbackAsync(CancellationToken);
        }

        eventId = (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.events WHERE process_instance_id = @id", ("id", announceId)))!;

        // One failure for each of the two triggers, each from the timeout of the statement that waited for the lock.
        Assert.Equal(
            2L,
            await tenant.CountAsync(
                "SELECT count(*) FROM axis.delivery_failures WHERE kind = 'event.deliver' AND target LIKE '%/' || @event AND attempts = 1 AND last_error = 'Npgsql.PostgresException with SQLSTATE 57014'",
                ("event", eventId.ToString("D"))));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE source = @source", ("source", eventId.ToString("D"))));

        // The item is queued again after the first backoff, 30 seconds.
        Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id AND claim_token IS NULL", ("id", eventId)));
        var seconds = (decimal)(await tenant.ScalarAsync(
            "SELECT extract(epoch FROM due_at - now()) FROM axis.process_work_items WHERE event_id = @id",
            ("id", eventId)))!;
        Assert.InRange(seconds, 25m, 30m);

        await tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE event_id = @id", ("id", eventId));
        await fixture.RunUntilIdleAsync("a", CancellationToken);

        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", eventId)));
        Assert.Equal(1, await StartedAsync(tenant, "RoleApprove", subjectId));
        Assert.Equal(1, await StartedAsync(tenant, "Decide", subjectId));
    }

    private static async Task<long> StartedAsync(ProcessStepFixture tenant, string process, Guid subjectId)
    {
        Assert.True(tenant.Model.TryGetProcess(process, out var model));
        return await tenant.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND subject_id = @subject",
            ("process", model.Id),
            ("subject", subjectId));
    }
}
