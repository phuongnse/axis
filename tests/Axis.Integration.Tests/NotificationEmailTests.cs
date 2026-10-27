using System.Net;
using System.Net.Sockets;
using Axis.Configuration.Model;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The worker sends the emails that <c>notify</c> steps staged, over SMTP to a real Mailpit. Each test stages its emails
/// with the fixture's runner, which runs only steps, and sends them with a worker of its own, built with the mail
/// settings under test. Every test uses addresses of its own, since Mailpit keeps the emails of the whole run.
/// </summary>
public sealed class NotificationEmailTests(ProcessStepFixture fixture, MailpitFixture mailpit) : IClassFixture<ProcessStepFixture>
{
    private const string Sender = "axis@example.test";

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Notify_step_to_a_vi_user_and_an_email_address_sends_two_plain_text_emails_in_their_locales_and_marks_both_sent()
    {
        var address = UniqueAddress();
        var (releaseId, model) = await NotifyMailApp.ActivateAsync(fixture, CancellationToken, address);
        var (title, subjectId, _) = await StageAsync(releaseId, model, viProfile: true);
        var emails = await EmailsAsync(title);
        await using var worker = Worker(new LogCollector(), await MailpitSettingsAsync("Email"));

        await RunUntilIdleAsync(worker);

        var vi = Assert.Single(await mailpit.MessagesToAsync($"vi.{subjectId:N}", CancellationToken));
        var en = Assert.Single(await mailpit.MessagesToAsync(address, CancellationToken));
        Assert.Equal([$"Yêu cầu {title}", $"Request {title}"], [vi.Subject, en.Subject]);
        var link = $"https://{fixture.Tenant}.example.test/steps/tasks";
        Assert.All([vi, en], message =>
        {
            Assert.StartsWith("text/plain", message.ContentType, StringComparison.Ordinal);
            Assert.EndsWith(link, message.Text.TrimEnd(), StringComparison.Ordinal);
        });
        Assert.StartsWith("Số tiền 500.00.", vi.Text, StringComparison.Ordinal);
        Assert.StartsWith("Amount 500.00.", en.Text, StringComparison.Ordinal);

        // The delivered Message-IDs are the stored ones, one for each row.
        Assert.Equal(2, emails.Count);
        Assert.Equal(
            emails.Select(email => email.MessageId.Trim('<', '>')).Order(StringComparer.Ordinal),
            new[] { vi.MessageId, en.MessageId }.Order(StringComparer.Ordinal));
        Assert.NotEqual(vi.MessageId, en.MessageId);
        Assert.Equal(
            [("sent", true), ("sent", true)],
            (await EmailsAsync(title)).Select(email => (email.State, email.SentAt is not null)));
        Assert.Equal(0L, await ItemCountAsync(emails.Select(email => email.Id)));
    }

    [Fact]
    public async Task Tenant_Email_block_replaces_the_host_settings_so_the_email_goes_through_the_tenant_server()
    {
        var address = UniqueAddress();
        var (releaseId, model) = await NotifyMailApp.ActivateAsync(fixture, CancellationToken, address);
        var (title, _, _) = await StageAsync(releaseId, model, viProfile: false);
        var settings = new List<(string, string)>
        {
            ("Email:Smtp:Host", "smtp.invalid"),
            ("Email:Smtp:Port", "25"),
            ("Email:Smtp:UseTls", "false"),
            ("Email:From", "host@example.test"),
        };
        settings.AddRange(await MailpitSettingsAsync($"Tenants:{fixture.Tenant}:Email"));
        await using var worker = Worker(new LogCollector(), settings);

        await RunUntilIdleAsync(worker);

        var message = Assert.Single(await mailpit.MessagesToAsync(address, CancellationToken));
        Assert.Equal($"Request {title}", message.Subject);
        Assert.Equal("sent", Assert.Single(await EmailsAsync(title)).State);
    }

    [Fact]
    public async Task Worker_with_no_mail_host_sends_nothing_keeps_the_items_queued_counts_no_failure_and_warns_once_for_the_tenant()
    {
        var address = UniqueAddress();
        var (releaseId, model) = await NotifyMailApp.ActivateAsync(fixture, CancellationToken, address);
        var first = await StageAsync(releaseId, model, viProfile: false);
        var second = await StageAsync(releaseId, model, viProfile: false);
        var emails = (await EmailsAsync(first.Title)).Concat(await EmailsAsync(second.Title)).ToList();
        var ids = emails.Select(email => email.Id).ToList();
        var logs = new LogCollector();
        await using var worker = Worker(logs, []);

        await RunUntilIdleAsync(worker);

        Assert.Equal(2, ids.Count);
        Assert.Empty(await mailpit.MessagesToAsync(address, CancellationToken));
        Assert.Equal(2L, await ItemCountAsync(ids));
        Assert.Equal(2L, await ItemCountAsync(ids, "AND due_at > now() AND claim_token IS NULL"));
        Assert.Equal(["pending", "pending"], (await EmailsAsync(first.Title)).Concat(await EmailsAsync(second.Title)).Select(email => email.State));
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.delivery_failures WHERE kind = 'notification.email' AND target = ANY(@ids)", ("ids", ids.Select(id => id.ToString("D")).ToArray())));
        var warning = Assert.Single(logs.Entries, entry => entry.Contains("no Email:Smtp:Host", StringComparison.Ordinal));
        Assert.Equal($"{LogLevel.Warning}: Tenant {fixture.Tenant} has no Email:Smtp:Host, so its emails are not sent and stay queued. This is logged once for the tenant.", warning);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains('@', StringComparison.Ordinal));
    }

    [Fact]
    public async Task Email_row_already_marked_sent_has_its_item_deleted_and_sends_no_email()
    {
        var address = UniqueAddress();
        var (releaseId, model) = await NotifyMailApp.ActivateAsync(fixture, CancellationToken, address);
        var (title, _, _) = await StageAsync(releaseId, model, viProfile: false);
        var email = Assert.Single(await EmailsAsync(title));
        await fixture.ScalarAsync("UPDATE axis.email_outbox SET state = 'sent', sent_at = now() WHERE id = @id", ("id", email.Id));
        await using var worker = Worker(new LogCollector(), await MailpitSettingsAsync("Email"));

        await RunUntilIdleAsync(worker);

        Assert.Equal(0L, await ItemCountAsync([email.Id]));
        Assert.Empty(await mailpit.MessagesToAsync(address, CancellationToken));
        Assert.Equal("sent", Assert.Single(await EmailsAsync(title)).State);
    }

    [Fact]
    public async Task Failed_send_is_counted_once_and_the_item_is_kept_for_a_later_try_with_the_row_still_pending()
    {
        var address = UniqueAddress();
        var (releaseId, model) = await NotifyMailApp.ActivateAsync(fixture, CancellationToken, address);
        var (title, _, _) = await StageAsync(releaseId, model, viProfile: false);
        var email = Assert.Single(await EmailsAsync(title));
        var logs = new LogCollector();
        await using var worker = Worker(
            logs,
            [("Email:Smtp:Host", "127.0.0.1"), ("Email:Smtp:Port", UnusedPort().ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Email:Smtp:UseTls", "false"), ("Email:From", Sender)]);

        await RunUntilIdleAsync(worker);

        Assert.Equal(
            1,
            await fixture.ScalarAsync("SELECT attempts FROM axis.delivery_failures WHERE kind = 'notification.email' AND target = @id", ("id", email.Id.ToString("D"))));
        var error = (string?)await fixture.ScalarAsync("SELECT last_error FROM axis.delivery_failures WHERE kind = 'notification.email' AND target = @id", ("id", email.Id.ToString("D")));
        Assert.NotNull(error);
        Assert.DoesNotContain('@', error);
        Assert.Equal(1L, await ItemCountAsync([email.Id], "AND due_at > now() AND claim_token IS NULL"));
        Assert.Equal("pending", Assert.Single(await EmailsAsync(title)).State);
        Assert.Equal(
            1,
            logs.Entries.Count(entry => entry.StartsWith($"{LogLevel.Warning}: Sending email {email.Id} failed with ", StringComparison.Ordinal)));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(address, StringComparison.Ordinal));
        Assert.Empty(await mailpit.MessagesToAsync(address, CancellationToken));
    }

    [Fact]
    public async Task Email_that_fails_the_limit_becomes_an_open_dead_letter_with_no_item_and_a_redeliver_queues_it_again_and_sends_it()
    {
        var address = UniqueAddress();
        var (releaseId, model) = await NotifyMailApp.ActivateAsync(fixture, CancellationToken, address);
        var (title, _, instanceId) = await StageAsync(releaseId, model, viProfile: false);
        var email = Assert.Single(await EmailsAsync(title));
        var id = ("id", (object)email.Id);
        var target = ("id", (object)email.Id.ToString("D"));
        var process = (Guid)(await fixture.ScalarAsync("SELECT process_id FROM axis.process_instances WHERE id = @id", ("id", instanceId)))!;
        await using (var failing = Worker(
            new LogCollector(),
            [
                ("Email:Smtp:Host", "127.0.0.1"),
                ("Email:Smtp:Port", UnusedPort().ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Email:Smtp:UseTls", "false"),
                ("Email:From", Sender),
                ("Worker:DeliveryAttempts", "3"),
            ]))
        {
            for (var run = 1; run <= 3; run++)
            {
                await fixture.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE kind = 'notification.email' AND email_id = @id", id);
                await RunUntilIdleAsync(failing);
                Assert.Equal(run, await fixture.ScalarAsync("SELECT attempts FROM axis.delivery_failures WHERE kind = 'notification.email' AND target = @id", target));
                Assert.Equal(
                    run == 3 ? 1L : 0L,
                    await fixture.CountAsync("SELECT count(*) FROM axis.dead_letters WHERE kind = 'notification.email' AND email_id = @id", id));
            }
        }

        var deadLetterId = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.dead_letters WHERE kind = 'notification.email' AND email_id = @id AND state = 'open'", id))!;
        Assert.Equal(
            (model.Manifest.Id, process, instanceId, 3),
            await DeadLetterAsync(deadLetterId));
        var error = (string?)await fixture.ScalarAsync("SELECT last_error FROM axis.dead_letters WHERE id = @id", ("id", deadLetterId));
        Assert.NotNull(error);
        Assert.DoesNotContain('@', error);
        Assert.Equal(0L, await ItemCountAsync([email.Id]));
        Assert.Equal("pending", Assert.Single(await EmailsAsync(title)).State);

        DeadLetterResult result;
        await using (var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(CancellationToken))
        {
            result = await DeadLetterCommand.RedeliverAsync(
                connection, transaction, fixture.Tenant, model, releaseId, process, deadLetterId, "maria", "Maria", "The mail server is back.", CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }

        Assert.Equal(DeadLetterOutcome.Settled, result.Outcome);
        Assert.Equal("redelivered", (string?)await fixture.ScalarAsync("SELECT state FROM axis.dead_letters WHERE id = @id", ("id", deadLetterId)));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.delivery_failures WHERE kind = 'notification.email' AND target = @id", target));
        Assert.Equal(1L, await ItemCountAsync([email.Id], "AND due_at <= now() AND claim_token IS NULL"));

        await using var worker = Worker(new LogCollector(), await MailpitSettingsAsync("Email"));
        await RunUntilIdleAsync(worker);

        Assert.Single(await mailpit.MessagesToAsync(address, CancellationToken));
        Assert.Equal("sent", Assert.Single(await EmailsAsync(title)).State);
        Assert.Equal(0L, await ItemCountAsync([email.Id]));
    }

    [Fact]
    public async Task Directory_tenant_SMTP_override_sends_through_the_tenant_server()
    {
        var address = UniqueAddress();
        var (releaseId, model) = await NotifyMailApp.ActivateAsync(fixture, CancellationToken, address);
        var (title, _, _) = await StageAsync(releaseId, model, viProfile: false);
        var catalog = await DirectoryCatalogAsync();
        var settings = new List<(string, string)>
        {
            ("Email:Smtp:Host", "smtp.invalid"),
            ("Email:Smtp:Port", "25"),
            ("Email:Smtp:UseTls", "false"),
            ("Email:From", "host@example.test"),
        };
        await using var worker = Worker(new LogCollector(), settings, catalog);

        await RunUntilIdleAsync(worker);

        // The server accepts a message only after a sign-in, so the delivery proves that the stored password was decrypted and used.
        var message = Assert.Single(await mailpit.MessagesToAsync(address, CancellationToken, authenticated: true));
        Assert.Equal($"Request {title}", message.Subject);
        Assert.Equal("sent", Assert.Single(await EmailsAsync(title)).State);
    }

    /// <summary>
    /// A directory catalog loaded from a platform database whose tenant has its own mail settings, which point at the Mailpit
    /// server that needs a password. The tenant's database is the fixture's.
    /// </summary>
    private async Task<DirectoryTenantCatalog> DirectoryCatalogAsync()
    {
        var platform = await fixture.CreateDatabaseAsync("platform");
        var cipher = AdminScenario.Cipher;
        var database = new NpgsqlConnectionStringBuilder(PostgreSqlServer.RuntimeConnectionString(fixture.ConnectionString));
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
            context.Tenants.Add(new TenantRow
            {
                Id = fixture.Tenant,
                Status = "active",
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = database.Host!,
                DatabasePort = database.Port,
                DatabaseName = database.Database!,
                RuntimeUser = database.Username!,
                RuntimePassword = cipher.Encrypt(fixture.Tenant, TenantSecretFields.RuntimePassword, database.Password!),
                SmtpHost = await mailpit.AuthSmtpHostAsync(),
                SmtpPort = await mailpit.AuthSmtpPortAsync(),
                SmtpUseTls = false,
                SmtpUserName = MailpitFixture.AuthUserName,
                SmtpPassword = cipher.Encrypt(fixture.Tenant, TenantSecretFields.SmtpPassword, MailpitFixture.AuthPassword),
                EmailFrom = Sender,
            });
            await context.SaveChangesAsync(CancellationToken);
        }

        return await DirectoryTenantCatalog.LoadAsync(platform, cipher, CancellationToken);
    }

    private static string UniqueAddress() => $"ops+{Guid.NewGuid():N}@example.test";

    private static int UnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The keys of the mail settings that point at Mailpit, under <paramref name="prefix"/>.</summary>
    private async Task<List<(string, string)>> MailpitSettingsAsync(string prefix) =>
    [
        ($"{prefix}:Smtp:Host", await mailpit.SmtpHostAsync()),
        ($"{prefix}:Smtp:Port", (await mailpit.SmtpPortAsync()).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ($"{prefix}:Smtp:UseTls", "false"),
        ($"{prefix}:From", Sender),
    ];

    /// <summary>
    /// Starts a request for the process NotifyMail and runs its steps, which stage the emails and queue their items.
    /// A vi profile for the title user adds a second email in Vietnamese.
    /// </summary>
    private async Task<(string Title, Guid SubjectId, Guid InstanceId)> StageAsync(Guid releaseId, ApplicationModel model, bool viProfile)
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var title = $"Request {subjectId:N}";
        if (viProfile)
        {
            await NotifyMailApp.UpsertProfileAsync(fixture, title, "vi", $"vi.{subjectId:N}@example.test", CancellationToken);
        }

        var instanceId = await fixture.StartAsync("NotifyMail", subjectId, releaseId, model);
        while (await fixture.RunNextStepAsync(CancellationToken))
        {
        }

        return (title, subjectId, instanceId);
    }

    /// <summary>A worker over the fixture's tenant database that has the given settings, with the host never started.</summary>
    private ServiceProvider Worker(LogCollector logs, IEnumerable<(string Key, string Value)> settings, ITenantCatalog? catalog = null)
    {
        var values = new Dictionary<string, string?>
        {
            [$"Tenants:{fixture.Tenant}:Hosts:0"] = $"{fixture.Tenant}.example.test",
            [$"Tenants:{fixture.Tenant}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(fixture.ConnectionString),
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddAxisWorker(configuration, new HostingEnvironment { EnvironmentName = "Testing" });
        if (catalog is not null)
        {
            // The last registration wins, so the handler finds the tenant in this catalog.
            services.AddSingleton(catalog);
        }

        return services.BuildServiceProvider();
    }

    private async Task RunUntilIdleAsync(ServiceProvider worker)
    {
        var runner = worker.GetRequiredService<WorkItemRunner>();
        while (await runner.RunNextAsync(fixture.Tenant, _lease, CancellationToken))
        {
        }
    }

    /// <summary>The number of <c>notification.email</c> items of <paramref name="emailIds"/> that also match <paramref name="condition"/>.</summary>
    private async Task<long> ItemCountAsync(IEnumerable<Guid> emailIds, string condition = "") =>
        await fixture.CountAsync(
            $"SELECT count(*) FROM axis.process_work_items WHERE kind = 'notification.email' AND email_id = ANY(@ids) {condition}",
            ("ids", emailIds.ToArray()));

    /// <summary>The application, process, instance and attempts of a dead letter.</summary>
    private async Task<(Guid ApplicationId, Guid ProcessId, Guid InstanceId, int Attempts)> DeadLetterAsync(Guid id)
    {
        await using var command = fixture.DataSource.CreateCommand(
            "SELECT application_id, process_id, process_instance_id, attempts FROM axis.dead_letters WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt32(3));
    }

    /// <summary>The outbox rows whose subject has <paramref name="title"/>, ordered by address.</summary>
    private async Task<IReadOnlyList<StagedEmail>> EmailsAsync(string title)
    {
        await using var command = fixture.DataSource.CreateCommand(
            "SELECT id, message_id, state, sent_at FROM axis.email_outbox WHERE subject LIKE '%' || @title || '%' ORDER BY address");
        command.Parameters.AddWithValue("title", title);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<StagedEmail>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new StagedEmail(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return rows;
    }

    private sealed record StagedEmail(Guid Id, string MessageId, string State, DateTimeOffset? SentAt);
}
