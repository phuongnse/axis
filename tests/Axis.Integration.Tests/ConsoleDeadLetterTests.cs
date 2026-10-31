using System.Net;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The operator console's routes that list, redeliver and discard the dead letters of the tenant. The tests seed
/// dead letters of every kind into two applications of tenant A, one of them a <c>webhook.deliver</c> letter with no
/// process, and name <c>reader</c> as the operator role: <c>rita</c> is the operator and <c>requester</c> is not.
/// The fixture is shared, so the tests find their letters by id.
/// </summary>
public sealed class ConsoleDeadLetterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Path = "/api/console/dead-letters";

    private const string Reason = "The receiving system is back.";

    private const string ReasonBody = $$"""{"reason":"{{Reason}}"}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_operator_lists_the_dead_letters_of_every_kind_across_applications_and_filters_by_application()
    {
        var (first, second) = await ApplicationsAsync();
        var event_ = await SeedAsync(first, "event.deliver", processId: Guid.NewGuid(), eventId: Guid.NewGuid(), triggerId: Guid.NewGuid());
        var fanOut = await SeedAsync(first, "trigger.fanOut", processId: Guid.NewGuid(), triggerId: Guid.NewGuid());
        var email = await SeedAsync(first, "notification.email", processId: Guid.NewGuid(), emailId: Guid.NewGuid(), instanceId: Guid.NewGuid());
        var webhook = await SeedAsync(second, "webhook.deliver", processId: null, eventId: Guid.NewGuid(), webhookId: Guid.NewGuid());
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient().SignedInAs("rita");

        using var all = await ListOkAsync(client, "?pageSize=100");
        foreach (var id in new[] { event_, fanOut, email, webhook })
        {
            FindItem(all, id);
        }

        var item = FindItem(all, webhook);
        Assert.Equal(
            ["id", "kind", "applicationId", "processId", "eventId", "triggerId", "processInstanceId", "emailId", "webhookId", "occurrenceAt", "attempts", "lastError", "state", "reason", "settledBy", "createdAt", "settledAt", "commands"],
            item.EnumerateObject().Select(property => property.Name));
        Assert.Equal("webhook.deliver", item.GetProperty("kind").GetString());
        Assert.Equal(second, item.GetProperty("applicationId").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("processId").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("webhookId").ValueKind);
        Assert.Equal(5, item.GetProperty("attempts").GetInt32());
        Assert.Equal("open", item.GetProperty("state").GetString());
        Assert.Equal(["redeliver", "discard"], Commands(item));
        Assert.Equal(first, FindItem(all, email).GetProperty("applicationId").GetGuid());
        Assert.Equal(1, all.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(100, all.RootElement.GetProperty("pageSize").GetInt32());

        using var onlyFirst = await ListOkAsync(client, $"?application={first:D}&pageSize=100");
        Assert.All(onlyFirst.RootElement.GetProperty("items").EnumerateArray(), row => Assert.Equal(first, row.GetProperty("applicationId").GetGuid()));
        FindItem(onlyFirst, event_);
        Assert.DoesNotContain(onlyFirst.RootElement.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == webhook);

        using var onlySecond = await ListOkAsync(client, $"?application={second:D}&state=open&pageSize=100");
        FindItem(onlySecond, webhook);
        Assert.DoesNotContain(onlySecond.RootElement.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == event_);

        using var paged = await ListOkAsync(client, "?pageSize=1");
        Assert.Single(paged.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(paged.RootElement.GetProperty("totalCount").GetInt64() >= 4);

        using var invalid = await client.SendAsync(Request($"{Path}?application=nope&state=closed&pageSize=0", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            ["application", "pageSize", "state"],
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_console_routes_refuse_a_signed_out_user_with_401_and_a_user_without_the_operator_role_with_403()
    {
        var (first, _) = await ApplicationsAsync();
        var id = await SeedAsync(first, "event.deliver", processId: Guid.NewGuid(), eventId: Guid.NewGuid(), triggerId: Guid.NewGuid());
        await using var factory = OperatorRoleIsReader();
        var routes = new[]
        {
            (HttpMethod.Get, Path, (string?)null),
            (HttpMethod.Post, $"{Path}/{id:D}/redeliver", ReasonBody),
            (HttpMethod.Post, $"{Path}/{id:D}/discard", ReasonBody),
        };

        using var signedOut = factory.CreateClient();
        using var requester = factory.CreateClient().SignedInAs("requester");
        foreach (var (method, path, body) in routes)
        {
            using var unauthorized = await signedOut.SendAsync(Request(method, path, HostA, body), CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            using var forbidden = await requester.SendAsync(Request(method, path, HostA, body), CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal("open", await StateAsync(id));
        Assert.Empty(await AuditAsync("deadLetter.discarded", id));
        Assert.Empty(await AuditAsync("deadLetter.redelivered", id));
    }

    [Fact]
    public async Task The_operator_redelivers_a_letter_with_no_process_and_discards_another_and_the_audit_names_the_operator()
    {
        var (first, second) = await ApplicationsAsync();
        var eventId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var webhook = await SeedAsync(second, "webhook.deliver", processId: null, eventId: eventId, webhookId: webhookId);
        await fixture.ExecuteAsync(
            TenantA,
            $"""
            INSERT INTO axis.webhook_deliveries (id, application_id, release_id, webhook_id, event_id)
            VALUES ('{deliveryId:D}', '{second:D}', '{Guid.NewGuid():D}', '{webhookId:D}', '{eventId:D}')
            """);
        var other = await SeedAsync(first, "event.deliver", processId: Guid.NewGuid(), eventId: Guid.NewGuid(), triggerId: Guid.NewGuid());
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient().SignedInAs("rita");

        using var redeliver = await client.SendAsync(Request(HttpMethod.Post, $"{Path}/{webhook:D}/redeliver", HostA, ReasonBody), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, redeliver.StatusCode);
        using var redelivered = JsonDocument.Parse(await redeliver.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(webhook, redelivered.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("redelivered", redelivered.RootElement.GetProperty("state").GetString());
        Assert.Equal(Reason, redelivered.RootElement.GetProperty("reason").GetString());
        Assert.Equal("rita", redelivered.RootElement.GetProperty("settledBy").GetString());
        Assert.Empty(Commands(redelivered.RootElement));
        Assert.Equal("redelivered", await StateAsync(webhook));
        Assert.Equal(
            1L,
            await fixture.ScalarAsync(
                TenantA, $"SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver' AND webhook_delivery_id = '{deliveryId:D}'"));

        using var discard = await client.SendAsync(Request(HttpMethod.Post, $"{Path}/{other:D}/discard", HostA, ReasonBody), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
        using var discarded = JsonDocument.Parse(await discard.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("discarded", discarded.RootElement.GetProperty("state").GetString());
        Assert.Equal("rita", discarded.RootElement.GetProperty("settledBy").GetString());
        Assert.Equal("discarded", await StateAsync(other));

        var redeliveredAudit = Assert.Single(await AuditAsync("deadLetter.redelivered", webhook));
        Assert.Equal(("rita", second), (redeliveredAudit.Actor, redeliveredAudit.ApplicationId));
        using (var details = JsonDocument.Parse(redeliveredAudit.Details))
        {
            Assert.Equal(Reason, details.RootElement.GetProperty("reason").GetString());
            Assert.Equal("webhook.deliver", details.RootElement.GetProperty("kind").GetString());
            Assert.Equal(eventId, details.RootElement.GetProperty("eventId").GetGuid());
        }

        var discardedAudit = Assert.Single(await AuditAsync("deadLetter.discarded", other));
        Assert.Equal(("rita", first), (discardedAudit.Actor, discardedAudit.ApplicationId));
        using (var details = JsonDocument.Parse(discardedAudit.Details))
        {
            Assert.Equal(Reason, details.RootElement.GetProperty("reason").GetString());
        }

        foreach (var command in new[] { "redeliver", "discard" })
        {
            using var again = await client.SendAsync(Request(HttpMethod.Post, $"{Path}/{other:D}/{command}", HostA, ReasonBody), CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }

        using var unknown = await client.SendAsync(Request(HttpMethod.Post, $"{Path}/{Guid.NewGuid():D}/discard", HostA, ReasonBody), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var malformed = await client.SendAsync(Request(HttpMethod.Post, $"{Path}/not-an-id/discard", HostA, ReasonBody), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [Fact]
    public async Task A_console_command_without_a_reason_or_the_csrf_header_is_refused_and_leaves_the_letter_open()
    {
        var (first, _) = await ApplicationsAsync();
        var id = await SeedAsync(first, "event.deliver", processId: Guid.NewGuid(), eventId: Guid.NewGuid(), triggerId: Guid.NewGuid());
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient().SignedInAs("rita");

        foreach (var command in new[] { "redeliver", "discard" })
        {
            using var missingReason = await client.SendAsync(Request(HttpMethod.Post, $"{Path}/{id:D}/{command}", HostA, "{}"), CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

            using var notJson = await client.SendAsync(Request(HttpMethod.Post, $"{Path}/{id:D}/{command}", HostA, ReasonBody, "text/plain"), CancellationToken);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, notJson.StatusCode);

            using var withoutCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri($"{Path}/{id:D}/{command}", UriKind.Relative))
            {
                Content = new StringContent(ReasonBody, System.Text.Encoding.UTF8, "application/json"),
            };
            withoutCsrf.Headers.Host = HostA;
            using var refused = await client.SendAsync(withoutCsrf, CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.Equal("open", await StateAsync(id));
        Assert.Empty(await AuditAsync("deadLetter.discarded", id));
        Assert.Empty(await AuditAsync("deadLetter.redelivered", id));
    }

    private WebApplicationFactory<Program> OperatorRoleIsReader() =>
        fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Console:OperatorRole", "reader"));

    /// <summary>The ids of two active applications of tenant A.</summary>
    private async Task<(Guid First, Guid Second)> ApplicationsAsync()
    {
        await using var configuration = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(fixture.ConnectionString(TenantA)).Options);
        var applications = await new ActiveReleaseStore(configuration).ListAsync(CancellationToken);
        Assert.True(applications.Count >= 2);
        return (applications[0].ApplicationId, applications[1].ApplicationId);
    }

    // The table has no foreign keys, so a letter can name ids that exist nowhere else.
    private async Task<Guid> SeedAsync(
        Guid applicationId,
        string kind,
        Guid? processId,
        Guid? eventId = null,
        Guid? triggerId = null,
        Guid? emailId = null,
        Guid? instanceId = null,
        Guid? webhookId = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.dead_letters
                (id, kind, application_id, process_id, event_id, trigger_id, process_instance_id, email_id, webhook_id, attempts, last_error, state)
            VALUES (@id, @kind, @application, @process, @event, @trigger, @instance, @email, @webhook, 5, 'Npgsql.PostgresException with SQLSTATE P0001', 'open')
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", (object?)processId ?? DBNull.Value);
        command.Parameters.AddWithValue("event", (object?)eventId ?? DBNull.Value);
        command.Parameters.AddWithValue("trigger", (object?)triggerId ?? DBNull.Value);
        command.Parameters.AddWithValue("instance", (object?)instanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("email", (object?)emailId ?? DBNull.Value);
        command.Parameters.AddWithValue("webhook", (object?)webhookId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<JsonDocument> ListOkAsync(HttpClient client, string query)
    {
        using var response = await client.SendAsync(Request($"{Path}{query}", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static JsonElement FindItem(JsonDocument list, Guid id) =>
        Assert.Single(list.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

    private static string[] Commands(JsonElement item) =>
        [.. item.GetProperty("commands").EnumerateArray().Select(command => command.GetString()!)];

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private async Task<string> StateAsync(Guid id) => (string)(await fixture.ScalarAsync(TenantA, $"SELECT state FROM axis.dead_letters WHERE id = '{id:D}'"))!;

    private async Task<IReadOnlyList<(string Actor, Guid ApplicationId, string Details)>> AuditAsync(string action, Guid deadLetterId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT actor, application_id, details::text FROM axis.audit_records WHERE action = @action AND details->>'deadLetterId' = @id",
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("id", deadLetterId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(string, Guid, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));
        }

        return rows;
    }
}
