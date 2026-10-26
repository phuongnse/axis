using System.Text.Json.Nodes;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

public sealed class ProcessNotifyStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Notify_step_stores_one_row_for_each_user_and_role_recipient_and_moves_on()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var instanceId = await fixture.StartAsync("Notify", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["tell", "done"], history.Select(row => row.Step));
        var notificationId = Guid.Parse(JsonNode.Parse(history[0].Output!)!["notificationId"]!.GetValue<string>());
        Assert.Equal(7, notificationId.Version);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["next"] = "done", ["notificationId"] = notificationId },
            JsonNode.Parse(history[0].Output!)));

        // The comment field is null, so its user recipient has no row. The two others share the id.
        var title = $"Request {subjectId:N}";
        var rows = await NotificationsAsync(notificationId);
        Assert.Equal([("role", "reviewer"), ("user", title)], rows.Select(row => (row.Kind, row.Recipient)));
        Assert.All(rows, row =>
        {
            Assert.Equal(
                (fixture.Model.Manifest.Id, fixture.FirstReleaseId, "request.notice.subject", "request.notice.body", "Tasks"),
                (row.ApplicationId, row.ReleaseId, row.SubjectKey, row.BodyKey, row.OpenPage));
            var values = Assert.IsType<JsonObject>(JsonNode.Parse(row.Values));
            Assert.Equal(["amount", "title"], values.Select(entry => entry.Key).Order(StringComparer.Ordinal));
            Assert.Equal(title, values["title"]!.GetValue<string>());
            Assert.Equal(500m, values["amount"]!.GetValue<decimal>());
            Assert.Equal("{}", row.OpenParameters);
        });
    }

    [Fact]
    public async Task Notify_step_whose_role_row_cannot_be_stored_fails_the_instance_and_keeps_no_row()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var title = $"Request {subjectId:N}";
        var instanceId = await fixture.StartAsync("Notify", subjectId, fixture.FirstReleaseId);

        // The two rows are one statement, so the user row goes too. The injected error fires only for this test's request.
        await fixture.ScalarAsync(
            $$"""
            CREATE FUNCTION axis.test_injected_notification_failure() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.recipient_kind = 'role' AND NEW."values"->>'title' = '{{title}}' THEN
                    RAISE EXCEPTION 'injected notification failure';
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER test_injected_notification_failure BEFORE INSERT ON axis.notifications
                FOR EACH ROW EXECUTE FUNCTION axis.test_injected_notification_failure();
            """);
        try
        {
            await fixture.RunUntilIdleAsync(CancellationToken);
        }
        finally
        {
            await fixture.ScalarAsync(
                """
                DROP TRIGGER test_injected_notification_failure ON axis.notifications;
                DROP FUNCTION axis.test_injected_notification_failure();
                """);
        }

        Assert.Equal(ProcessStarts.Failed, (await fixture.InstanceAsync(instanceId)).State);
        var failure = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal(("tell", "Npgsql.PostgresException"), (failure.Step, failure.Error));
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.notifications WHERE \"values\"->>'title' = @title", ("title", title)));
    }

    [Fact]
    public async Task Notify_step_with_only_the_email_channel_stores_no_row_and_moves_on()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var instanceId = await fixture.StartAsync("NotifyByEmail", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        var notificationId = (string?)await fixture.ScalarAsync(
            "SELECT output->>'notificationId' FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'tell'",
            ("id", instanceId));
        Assert.NotNull(notificationId);
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.notifications WHERE id = @id", ("id", Guid.Parse(notificationId))));
    }

    /// <summary>The rows of the notification, in recipient order, read from SQL.</summary>
    private async Task<IReadOnlyList<NotificationRow>> NotificationsAsync(Guid id)
    {
        await using var command = fixture.DataSource.CreateCommand(
            """
            SELECT application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values"::text, open_page, open_parameters::text
            FROM axis.notifications WHERE id = @id ORDER BY recipient_kind, recipient
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<NotificationRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new NotificationRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    private sealed record NotificationRow(
        Guid ApplicationId,
        Guid ReleaseId,
        string Kind,
        string Recipient,
        string SubjectKey,
        string BodyKey,
        string Values,
        string? OpenPage,
        string? OpenParameters);
}
