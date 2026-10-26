using System.Diagnostics.Metrics;
using Axis.Core.Telemetry;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The limit, backoff and dead letters of event deliveries. StepApp's <c>Announce</c> raises
/// <c>RequestNoted</c>, and both <c>ApproveNotedRequests</c> and <c>DecideHugeNotedRequests</c> pass
/// their <c>when</c> for a ratio of a million or more. A PostgreSQL trigger makes the start of
/// <c>Decide</c> fail for one record, with an error message that quotes the event's ratio. The class
/// has its own fixture, so that trigger cannot affect <see cref="EventDeliveryTests"/>.
/// </summary>
[Collection(CollectionName)]
public sealed class DeadLetterTests(EventDeliveryFixture fixture) : IClassFixture<EventDeliveryFixture>
{
    /// <summary>The collection of the classes whose dead letters one metric listener must not see.</summary>
    public const string CollectionName = "Dead letters";

    private const int Limit = 5;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failing_start_of_one_trigger_backs_off_then_becomes_an_open_dead_letter_while_the_other_trigger_starts_once()
    {
        var tenant = fixture.Tenant("a");
        var delivery = await FailingDeliveryAsync(tenant);
        try
        {
            TimeSpan[] delays = [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(240)];
            for (var failures = 1; failures <= delays.Length; failures++)
            {
                var delay = delays[failures - 1].TotalSeconds;
                Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id AND claim_token IS NULL", ("id", delivery.EventId)));
                var seconds = (decimal)(await tenant.ScalarAsync(
                    "SELECT extract(epoch FROM due_at - now()) FROM axis.process_work_items WHERE event_id = @id",
                    ("id", delivery.EventId)))!;
                Assert.InRange(seconds, (decimal)delay - 5, (decimal)delay);
                Assert.Equal(failures, await AttemptsAsync(tenant, delivery));
                Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.dead_letters WHERE event_id = @id", ("id", delivery.EventId)));

                await MakeDueAndRunAsync(tenant, delivery);
            }
        }
        finally
        {
            await DropFailureAsync(tenant);
        }

        // The 5th failure is the limit, so the item is deleted.
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", delivery.EventId)));
        Assert.Equal(Limit, await AttemptsAsync(tenant, delivery));

        await using var command = tenant.DataSource.CreateCommand(
            """
            SELECT kind, application_id, process_id, trigger_id, process_instance_id, email_id, occurrence_at, attempts, last_error, state, reason, settled_by, settled_at
            FROM axis.dead_letters WHERE event_id = @id
            """);
        command.Parameters.AddWithValue("id", delivery.EventId);
        await using (var reader = await command.ExecuteReaderAsync(CancellationToken))
        {
            Assert.True(await reader.ReadAsync(CancellationToken));
            Assert.Equal("event.deliver", reader.GetString(0));
            Assert.Equal(delivery.ApplicationId, reader.GetGuid(1));
            Assert.Equal(delivery.DecideId, reader.GetGuid(2));
            Assert.Equal(delivery.TriggerId, reader.GetGuid(3));
            Assert.All([4, 5, 6, 10, 11, 12], column => Assert.True(reader.IsDBNull(column)));
            Assert.Equal(Limit, reader.GetInt32(7));
            Assert.Equal("Npgsql.PostgresException with SQLSTATE P0001", reader.GetString(8));
            Assert.Equal("open", reader.GetString(9));
            Assert.False(await reader.ReadAsync(CancellationToken));
        }

        Assert.Equal(("deadLettered", (Guid?)null), await ReceiptAsync(tenant, delivery.TriggerId, delivery));

        // The other trigger started its process once, and its receipt stopped later passes from starting it again.
        var started = Assert.Single(await InstancesAsync(tenant, "RoleApprove", delivery.SubjectId));
        Assert.Equal(("started", (Guid?)started), await ReceiptAsync(tenant, delivery.ApproveTriggerId, delivery));
        Assert.Empty(await InstancesAsync(tenant, "Decide", delivery.SubjectId));
    }

    [Fact]
    public async Task Dead_letter_is_logged_and_counted_without_the_payload_value_the_exception_message_quotes()
    {
        var tenant = fixture.Tenant("a");
        var delivery = await FailingDeliveryAsync(tenant);
        var measurements = new List<(long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == AxisMetrics.MeterName && instrument.Name == AxisMetrics.DeadLettersCreated)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var pairs = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                pairs[tag.Key] = tag.Value;
            }

            lock (measurements)
            {
                measurements.Add((value, pairs));
            }
        });
        listener.Start();
        try
        {
            for (var failures = 1; failures < Limit; failures++)
            {
                await MakeDueAndRunAsync(tenant, delivery);
            }
        }
        finally
        {
            await DropFailureAsync(tenant);
        }

        var deadLetterId = (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.dead_letters WHERE event_id = @id", ("id", delivery.EventId)))!;
        var lastError = (string)(await tenant.ScalarAsync("SELECT last_error FROM axis.dead_letters WHERE event_id = @id", ("id", delivery.EventId)))!;
        var counted = (string)(await tenant.ScalarAsync("SELECT last_error FROM axis.delivery_failures WHERE target = @target", ("target", Target(delivery))))!;
        var ratio = (string)(await tenant.ScalarAsync("SELECT payload->>'ratio' FROM axis.events WHERE id = @id", ("id", delivery.EventId)))!;
        Assert.True(decimal.Parse(ratio, System.Globalization.CultureInfo.InvariantCulture) >= 1_000_000m);

        // The ratio has a decimal point, so no id or timestamp can contain it by chance.
        Assert.Contains('.', ratio);

        var warning = Assert.Single(
            fixture.Logs.Entries,
            entry => entry.StartsWith("Warning: ", StringComparison.Ordinal) && entry.Contains(deadLetterId.ToString(), StringComparison.Ordinal));
        Assert.Contains(delivery.TriggerId.ToString(), warning, StringComparison.Ordinal);
        Assert.Contains(delivery.EventId.ToString(), warning, StringComparison.Ordinal);
        Assert.Contains("event.deliver", warning, StringComparison.Ordinal);

        var measurement = Assert.Single(measurements);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal("a", measurement.Tags[AxisTelemetry.TenantTag]);
        Assert.Equal("event.deliver", measurement.Tags[AxisTelemetry.WorkItemKindTag]);

        Assert.DoesNotContain(ratio, lastError, StringComparison.Ordinal);
        Assert.DoesNotContain(ratio, counted, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(ratio, StringComparison.Ordinal));
        Assert.DoesNotContain(
            measurements.SelectMany(point => point.Tags.Values),
            value => value is string text && text.Contains(ratio, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 480)]
    [InlineData(7, 1920)]
    [InlineData(8, 3600)]
    [InlineData(100, 3600)]
    [InlineData(int.MaxValue, 3600)]
    public void Backoff_doubles_from_thirty_seconds_and_stops_at_one_hour(int attempts, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), DeliveryFailures.Backoff(attempts));

    /// <summary>
    /// Raises <c>RequestNoted</c> for a new request with a ratio over a million and a decimal fraction,
    /// after a PostgreSQL trigger has made the start of <c>Decide</c> for that record fail with a message
    /// that quotes the ratio. Runs the raising process and the first delivery, which fails once.
    /// </summary>
    private async Task<Delivery> FailingDeliveryAsync(ProcessStepFixture tenant)
    {
        Assert.True(tenant.Model.TryGetProcess("Decide", out var decide));
        var decideTrigger = Assert.Single(tenant.Model.Triggers, candidate => candidate.Name == "DecideHugeNotedRequests");
        var approveTrigger = Assert.Single(tenant.Model.Triggers, candidate => candidate.Name == "ApproveNotedRequests");
        var subjectId = await tenant.InsertRequestAsync(4_123_456.75m, 4);
        await tenant.ScalarAsync(
            $$"""
            CREATE FUNCTION axis.test_injected_delivery_failure() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.process_id = '{{decide.Id:D}}'::uuid AND NEW.subject_id = '{{subjectId:D}}'::uuid THEN
                    RAISE EXCEPTION 'ratio % refused', (SELECT payload->>'ratio' FROM axis.events WHERE subject_id = NEW.subject_id LIMIT 1);
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER test_injected_delivery_failure BEFORE INSERT ON axis.process_instances
                FOR EACH ROW EXECUTE FUNCTION axis.test_injected_delivery_failure();
            """);
        try
        {
            var announceId = await tenant.StartAsync("Announce", subjectId, tenant.FirstReleaseId);
            await fixture.RunUntilIdleAsync("a", CancellationToken);
            Assert.Equal(ProcessStarts.Completed, (await tenant.InstanceAsync(announceId)).State);
            var eventId = (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.events WHERE process_instance_id = @id", ("id", announceId)))!;
            var applicationId = (Guid)(await tenant.ScalarAsync("SELECT application_id FROM axis.events WHERE id = @id", ("id", eventId)))!;
            return new Delivery(subjectId, eventId, applicationId, decide.Id, decideTrigger.Id, approveTrigger.Id);
        }
        catch
        {
            await DropFailureAsync(tenant);
            throw;
        }
    }

    private async Task MakeDueAndRunAsync(ProcessStepFixture tenant, Delivery delivery)
    {
        await tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE event_id = @id", ("id", delivery.EventId));
        await fixture.RunUntilIdleAsync("a", CancellationToken);
    }

    private static async Task DropFailureAsync(ProcessStepFixture tenant) =>
        await tenant.ScalarAsync(
            """
            DROP TRIGGER test_injected_delivery_failure ON axis.process_instances;
            DROP FUNCTION axis.test_injected_delivery_failure();
            """);

    private static string Target(Delivery delivery) => $"{delivery.TriggerId:D}/{delivery.EventId:D}";

    private static async Task<int> AttemptsAsync(ProcessStepFixture tenant, Delivery delivery) =>
        (int)(await tenant.ScalarAsync(
            "SELECT attempts FROM axis.delivery_failures WHERE kind = 'event.deliver' AND target = @target",
            ("target", Target(delivery))))!;

    private static async Task<IReadOnlyList<Guid>> InstancesAsync(ProcessStepFixture tenant, string process, Guid subjectId)
    {
        Assert.True(tenant.Model.TryGetProcess(process, out var model));
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT id FROM axis.process_instances WHERE process_id = @process AND subject_id = @subject ORDER BY id");
        command.Parameters.AddWithValue("process", model.Id);
        command.Parameters.AddWithValue("subject", subjectId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(reader.GetGuid(0));
        }

        return rows;
    }

    private static async Task<(string Outcome, Guid? InstanceId)> ReceiptAsync(ProcessStepFixture tenant, Guid triggerId, Delivery delivery)
    {
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT outcome, process_instance_id FROM axis.trigger_starts WHERE trigger_id = @trigger AND source = @source AND subject_id = @subject");
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("source", delivery.EventId.ToString("D"));
        command.Parameters.AddWithValue("subject", delivery.SubjectId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private sealed record Delivery(Guid SubjectId, Guid EventId, Guid ApplicationId, Guid DecideId, Guid TriggerId, Guid ApproveTriggerId);
}
