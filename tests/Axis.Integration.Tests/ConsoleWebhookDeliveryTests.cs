using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ConsoleWebhookDeliveryTests(EndpointOutputFixture fixture) : IClassFixture<EndpointOutputFixture>
{
    private const string Path = "/api/console/webhook-deliveries";

    private const string PayloadMarker = "payload-marker-7f3a91c2";

    private static int _stages;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deliveries_are_refused_without_a_session()
    {
        await using var factory = OperatorRoleIs("importer");
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deliveries_are_refused_to_a_user_without_the_operator_role()
    {
        await using var factory = OperatorRoleIs("platform-operator");
        using var client = factory.CreateClient().SignedInAs("ivy");

        using var response = await SendAsync(client, Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deliveries_answer_the_operator_the_state_attempts_and_status_code_of_each_delivery()
    {
        var staged = await StageAsync();
        await using var factory = OperatorRoleIs("importer");
        using var client = factory.CreateClient().SignedInAs("ivy");
        var webhook = fixture.Model.Webhooks.Single();

        using var response = await SendAsync(client, $"{Path}?application={fixture.Model.Manifest.Id}&webhook={webhook.Id}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var ids = staged.Select(row => row.Id).ToList();
        var items = answer.RootElement.GetProperty("items").EnumerateArray().Where(item => ids.Contains(item.GetProperty("id").GetGuid())).ToList();
        Assert.True(answer.RootElement.GetProperty("totalCount").GetInt64() >= staged.Count);
        Assert.Equal(ids, items.Select(item => item.GetProperty("id").GetGuid()));
        for (var index = 0; index < staged.Count; index++)
        {
            var item = items[index];
            var expected = staged[index];
            Assert.Equal(expected.State, item.GetProperty("state").GetString());
            Assert.Equal(expected.Attempts, item.GetProperty("attempts").GetInt32());
            Assert.Equal(expected.StatusCode, item.GetProperty("statusCode").GetInt32());
            Assert.Equal("AnnounceHook", item.GetProperty("webhook").GetString());
            Assert.Equal(webhook.Id, item.GetProperty("webhookId").GetGuid());
            Assert.Equal("EndpointApp", item.GetProperty("application").GetString());
            Assert.Equal("NoteAnnounced", item.GetProperty("event").GetString());
            Assert.Equal(expected.EventId, item.GetProperty("eventId").GetGuid());
        }

        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("deliveredAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("deliveredAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, items[0].GetProperty("deliveredAt").ValueKind);
        var offered = Assert.Single(answer.RootElement.GetProperty("webhooks").EnumerateArray());
        Assert.Equal(webhook.Id, offered.GetProperty("id").GetGuid());
        Assert.Equal("AnnounceHook", offered.GetProperty("name").GetString());
        Assert.Equal("EndpointApp", offered.GetProperty("application").GetString());
    }

    [Fact]
    public async Task Deliveries_are_narrowed_by_application_and_webhook()
    {
        var staged = await StageAsync();
        await using var factory = OperatorRoleIs("importer");
        using var client = factory.CreateClient().SignedInAs("ivy");

        foreach (var filter in new[] { $"webhook={fixture.Model.Webhooks.Single().Id}", $"application={fixture.Model.Manifest.Id}" })
        {
            using var kept = await SendAsync(client, $"{Path}?{filter}&pageSize=100");
            using var keptAnswer = JsonDocument.Parse(await kept.Content.ReadAsStringAsync(CancellationToken));
            var ids = keptAnswer.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
            Assert.All(staged, row => Assert.Contains(row.Id, ids));
        }

        foreach (var name in new[] { "application", "webhook" })
        {
            using var none = await SendAsync(client, $"{Path}?{name}={Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.OK, none.StatusCode);
            using var noneAnswer = JsonDocument.Parse(await none.Content.ReadAsStringAsync(CancellationToken));
            Assert.Empty(noneAnswer.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(0, noneAnswer.RootElement.GetProperty("totalCount").GetInt64());
        }

        using var malformed = await SendAsync(client, $"{Path}?webhook=nope");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var problem = JsonDocument.Parse(await malformed.Content.ReadAsStringAsync(CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("webhook", out _));
    }

    [Fact]
    public async Task Deliveries_are_paged_newest_first()
    {
        var staged = await StageAsync();
        await using var factory = OperatorRoleIs("importer");
        using var client = factory.CreateClient().SignedInAs("ivy");

        using var response = await SendAsync(client, $"{Path}?pageSize=1&page=2");

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(2, answer.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, answer.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(answer.RootElement.GetProperty("totalCount").GetInt64() >= staged.Count);
        var item = Assert.Single(answer.RootElement.GetProperty("items").EnumerateArray());
        Assert.NotEqual(staged[0].Id, item.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Deliveries_show_a_discarded_dead_letter_as_discarded()
    {
        var discarded = await StageDeliveryAsync("pending", 5, 503, NextCreatedAt(), deadLetter: "discarded");
        await using var factory = OperatorRoleIs("importer");
        using var client = factory.CreateClient().SignedInAs("ivy");

        using var response = await SendAsync(client, $"{Path}?pageSize=1");

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var item = Assert.Single(answer.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(discarded.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("discarded", item.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Deliveries_never_answer_a_body_a_url_or_a_secret()
    {
        await StageAsync();
        await using var factory = OperatorRoleIs("importer");
        using var client = factory.CreateClient().SignedInAs("ivy");

        using var response = await SendAsync(client, $"{Path}?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain(PayloadMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain("/announce", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Receiver.BaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("whsec_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", text, StringComparison.OrdinalIgnoreCase);
    }

    // Each call is one hour later than the one before, so a test's rows are the newest and its three never mix with another's.
    private static DateTimeOffset NextCreatedAt() =>
        DateTimeOffset.UtcNow.AddDays(1).AddHours(Interlocked.Increment(ref _stages));

    private WebApplicationFactory<Program> OperatorRoleIs(string role) =>
        fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Console:OperatorRole", role));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = EndpointOutputFixture.Host;
        return await client.SendAsync(request, CancellationToken);
    }

    /// <summary>
    /// Stages a delivered, a pending and a dead-lettered delivery of the AnnounceHook webhook, newest first.
    /// They are newer than any delivery an earlier call staged.
    /// </summary>
    private async Task<IReadOnlyList<StagedRow>> StageAsync()
    {
        var created = NextCreatedAt();
        var delivered = await StageDeliveryAsync("delivered", 1, 200, created.AddMinutes(3));
        var pending = await StageDeliveryAsync("pending", 1, 500, created.AddMinutes(2));
        var deadLettered = await StageDeliveryAsync("pending", 5, 503, created.AddMinutes(1), deadLetter: "open");
        return [delivered, pending, deadLettered];
    }

    private async Task<StagedRow> StageDeliveryAsync(string state, int attempts, int statusCode, DateTimeOffset createdAt, string? deadLetter = null)
    {
        var applicationId = fixture.Model.Manifest.Id;
        var webhook = fixture.Model.Webhooks.Single();
        var eventId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await using var connection = await fixture.OpenConnectionAsync();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO axis.events (id, application_id, event, release_id, entity_id, subject_id, payload, process_instance_id, raised_at)
            VALUES (@event, @application, 'NoteAnnounced', @release, @entity, @subject, @payload::jsonb, NULL, @created)
            """,
            connection))
        {
            insert.Parameters.AddWithValue("event", eventId);
            insert.Parameters.AddWithValue("application", applicationId);
            insert.Parameters.AddWithValue("release", fixture.ReleaseId);
            insert.Parameters.AddWithValue("entity", Guid.NewGuid());
            insert.Parameters.AddWithValue("subject", Guid.NewGuid());
            insert.Parameters.AddWithValue("payload", $$"""{"title":"{{PayloadMarker}}"}""");
            insert.Parameters.AddWithValue("created", createdAt);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO axis.webhook_deliveries (id, application_id, release_id, webhook_id, event_id, state, attempts, status_code, created_at, delivered_at)
            VALUES (@id, @application, @release, @webhook, @event, @state, @attempts, @status, @created, @delivered)
            """,
            connection))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("application", applicationId);
            insert.Parameters.AddWithValue("release", fixture.ReleaseId);
            insert.Parameters.AddWithValue("webhook", webhook.Id);
            insert.Parameters.AddWithValue("event", eventId);
            insert.Parameters.AddWithValue("state", state);
            insert.Parameters.AddWithValue("attempts", attempts);
            insert.Parameters.AddWithValue("status", statusCode);
            insert.Parameters.AddWithValue("created", createdAt);
            insert.Parameters.Add(new NpgsqlParameter("delivered", NpgsqlTypes.NpgsqlDbType.TimestampTz)
            {
                Value = state == "delivered" ? createdAt : DBNull.Value,
            });
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        if (deadLetter is not null)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO axis.dead_letters (id, kind, application_id, event_id, webhook_id, attempts, last_error, state, created_at)
                VALUES (@id, 'webhook.deliver', @application, @event, @webhook, @attempts, 'failed', @state, @created)
                """,
                connection);
            insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("application", applicationId);
            insert.Parameters.AddWithValue("event", eventId);
            insert.Parameters.AddWithValue("webhook", webhook.Id);
            insert.Parameters.AddWithValue("attempts", attempts);
            insert.Parameters.AddWithValue("state", deadLetter);
            insert.Parameters.AddWithValue("created", createdAt);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        var expectedState = deadLetter switch
        {
            "open" => "deadLettered",
            "discarded" => "discarded",
            _ => state,
        };
        return new StagedRow(id, eventId, expectedState, attempts, statusCode);
    }

    private sealed record StagedRow(Guid Id, Guid EventId, string State, int Attempts, int StatusCode);
}
