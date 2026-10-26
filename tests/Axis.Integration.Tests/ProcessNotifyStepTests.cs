using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Profiles;
using Axis.Processes.Instances;
using Microsoft.Extensions.Logging;

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
            var types = Assert.IsType<JsonObject>(JsonNode.Parse(row.ValueTypes));
            Assert.Equal("text", types["title"]!.GetValue<string>());
            Assert.Equal("decimal", types["amount"]!.GetValue<string>());
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

    [Fact]
    public async Task Notify_step_with_the_email_channel_stages_one_pending_email_and_queued_item_for_each_address()
    {
        var (releaseId, model) = await ActivateMailReleaseAsync();
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var title = $"Request {subjectId:N}";
        await UpsertProfileAsync(title, "vi", $"vi.{subjectId:N}@example.test");
        var instanceId = await fixture.StartAsync("NotifyMail", subjectId, releaseId, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        var notificationId = Guid.Parse((string)(await fixture.ScalarAsync(
            "SELECT output->>'notificationId' FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'tell'",
            ("id", instanceId)))!);
        var emails = await EmailsAsync($"%{title}%");

        // The user's profile gives its address and locale. The email recipient gets the release's first locale.
        Assert.Equal(
            [("ops@example.test", "en"), ($"vi.{subjectId:N}@example.test", "vi")],
            emails.Select(email => (email.Address, email.Locale)));
        Assert.All(emails, email =>
        {
            Assert.Equal((fixture.Model.Manifest.Id, releaseId, notificationId, "tell", "pending"), (email.ApplicationId, email.ReleaseId, email.NotificationId, email.Step, email.State));
            Assert.Equal(7, email.Id.Version);
            Assert.Equal($"<{email.Id:D}@{fixture.Tenant}.example.test>", email.MessageId);
            Assert.EndsWith($"\n\nhttps://{fixture.Tenant}.example.test/steps/tasks", email.Body, StringComparison.Ordinal);
        });
        Assert.Equal(2, emails.Select(email => email.MessageId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(($"Request {title}", "Amount 500.00."), (emails[0].Subject, emails[0].Body.Split("\n\n")[0]));
        Assert.Equal(($"Yêu cầu {title}", "Số tiền 500.00."), (emails[1].Subject, emails[1].Body.Split("\n\n")[0]));

        var items = await EmailItemsAsync(instanceId);
        Assert.Equal(emails.Select(email => email.Id).Order(), items.Order());
    }

    [Fact]
    public async Task Notify_step_stages_no_email_for_a_user_without_a_profile_email_and_logs_the_user()
    {
        var (releaseId, model) = await ActivateMailReleaseAsync();
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var title = $"Request {subjectId:N}";
        var instanceId = await fixture.StartAsync("NotifyMail", subjectId, releaseId, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(ProcessStarts.Completed, (await fixture.InstanceAsync(instanceId)).State);
        var emails = await EmailsAsync($"%{title}%");
        Assert.Equal(["ops@example.test"], emails.Select(email => email.Address));
        Assert.Equal(emails.Select(email => email.Id), await EmailItemsAsync(instanceId));

        // The comment field is null, so its user recipient has no row and no log. The title user has no profile.
        var entry = Assert.Single(
            fixture.Logs.Entries,
            entry => entry.StartsWith($"{LogLevel.Information}: Step tell of process instance {instanceId} sends no email", StringComparison.Ordinal));
        Assert.Contains($"user {title}", entry, StringComparison.Ordinal);
        Assert.DoesNotContain('@', entry);
    }

    [Fact]
    public async Task Notify_step_that_fails_after_staging_its_emails_leaves_no_email_and_no_item()
    {
        var (releaseId, model) = await ActivateMailReleaseAsync();
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var title = $"Request {subjectId:N}";
        await UpsertProfileAsync(title, "vi", $"vi.{subjectId:N}@example.test");
        var instanceId = await fixture.StartAsync("NotifyMail", subjectId, releaseId, model);

        // The history row is written after the emails are staged. The failure's own row has an error, so it still goes in.
        await fixture.ScalarAsync(
            $$"""
            CREATE FUNCTION axis.test_injected_history_failure() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.process_instance_id = '{{instanceId}}' AND NEW.error IS NULL THEN
                    RAISE EXCEPTION 'injected history failure';
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER test_injected_history_failure BEFORE INSERT ON axis.process_step_history
                FOR EACH ROW EXECUTE FUNCTION axis.test_injected_history_failure();
            """);
        try
        {
            await fixture.RunUntilIdleAsync(CancellationToken);
        }
        finally
        {
            await fixture.ScalarAsync(
                """
                DROP TRIGGER test_injected_history_failure ON axis.process_step_history;
                DROP FUNCTION axis.test_injected_history_failure();
                """);
        }

        Assert.Equal(ProcessStarts.Failed, (await fixture.InstanceAsync(instanceId)).State);
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.email_outbox WHERE subject LIKE '%' || @title || '%'", ("title", title)));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_work_items WHERE kind = 'notification.email' AND process_instance_id = @id",
                ("id", instanceId)));
    }

    /// <summary>
    /// Activates a copy of StepApp with the process NotifyMail and Vietnamese texts. The process notifies the title user and
    /// <c>ops@example.test</c> by both channels, and the comment user, who is null.
    /// </summary>
    private async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateMailReleaseAsync()
    {
        var process = await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "processes", "notify.json"), CancellationToken);
        process = process
            .Replace("d74f217a-8513-4825-9467-c3aa720d70a5", "d74f217a-8513-4825-9467-c3aa720d70b6", StringComparison.Ordinal)
            .Replace("\"name\": \"Notify\"", "\"name\": \"NotifyMail\"", StringComparison.Ordinal)
            .Replace("""{ "role": "reviewer" }""", """{ "email": "'ops@example.test'" }""", StringComparison.Ordinal)
            .Replace("""["inApp"]""", """["inApp", "email"]""", StringComparison.Ordinal);
        var texts = await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "texts", "en.json"), CancellationToken);
        texts = texts
            .Replace("0b6e1f4c-5d2a-4e8b-9c3f-7a1d2e4b6c81", "0b6e1f4c-5d2a-4e8b-9c3f-7a1d2e4b6c82", StringComparison.Ordinal)
            .Replace("\"TextsEn\"", "\"TextsVi\"", StringComparison.Ordinal)
            .Replace("\"locale\": \"en\"", "\"locale\": \"vi\"", StringComparison.Ordinal)
            .Replace("\"Amount {amount}.\"", "\"Số tiền {amount}.\"", StringComparison.Ordinal)
            .Replace("\"Request {title}\"", "\"Yêu cầu {title}\"", StringComparison.Ordinal);
        var site = await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "sites", "steps.json"), CancellationToken);
        site = site.Replace("\"available\": [\"en\"]", "\"available\": [\"en\", \"vi\"]", StringComparison.Ordinal);
        return await fixture.ActivateCopyWithFilesAsync(
            ("processes/notify-mail.json", process),
            ("texts/vi.json", texts),
            ("sites/steps.json", site));
    }

    private async Task UpsertProfileAsync(string userId, string locale, string email)
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        await UserProfiles.UpsertAsync(connection, userId, "Requester", email, locale, CancellationToken);
    }

    /// <summary>The outbox rows whose subject matches <paramref name="subjectPattern"/>, ordered by address.</summary>
    private async Task<IReadOnlyList<EmailRow>> EmailsAsync(string subjectPattern)
    {
        await using var command = fixture.DataSource.CreateCommand(
            """
            SELECT id, application_id, release_id, notification_id, step, address, locale, subject, body, message_id, state
            FROM axis.email_outbox WHERE subject LIKE @pattern ORDER BY address
            """);
        command.Parameters.AddWithValue("pattern", subjectPattern);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<EmailRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new EmailRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return rows;
    }

    /// <summary>The <c>email_id</c> of each <c>notification.email</c> item of the instance, in queue order.</summary>
    private async Task<IReadOnlyList<Guid>> EmailItemsAsync(Guid instanceId)
    {
        await using var command = fixture.DataSource.CreateCommand(
            "SELECT email_id FROM axis.process_work_items WHERE kind = 'notification.email' AND process_instance_id = @id ORDER BY seq");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private sealed record EmailRow(
        Guid Id,
        Guid ApplicationId,
        Guid ReleaseId,
        Guid NotificationId,
        string Step,
        string Address,
        string? Locale,
        string Subject,
        string Body,
        string MessageId,
        string State);

    /// <summary>The rows of the notification, in recipient order, read from SQL.</summary>
    private async Task<IReadOnlyList<NotificationRow>> NotificationsAsync(Guid id)
    {
        await using var command = fixture.DataSource.CreateCommand(
            """
            SELECT application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values"::text, value_types::text, open_page, open_parameters::text
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
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
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
        string ValueTypes,
        string? OpenPage,
        string? OpenParameters);
}
