using System.Net;
using System.Text.Json;
using Axis.Data.Audit;
using Axis.Server.Tests;
using Axis.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The authoring request routes store a request and its steps in the database of the host's tenant, count each send toward the
/// <c>authoringRequests</c> quota, and let only the request's author approve, change or drop it. Nothing here moves a
/// request out of <c>planning</c> by itself, so the tests set the state in the database, as the agent host will.
/// </summary>
public sealed class AuthoringRequestTests : IAsyncLifetime
{
    private const string Host = StudioPublishTests.HostA;

    private const string Url = "/api/studio/apps/PurchaseRequests/authoring-requests";

    private const string Text = """{ "text": "Add a cost centre to every purchase request." }""";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task A_sent_request_is_planning_and_counts_once_and_a_change_of_plan_does_not_count_again()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("authoring_requests_send", CancellationToken);
        await using var factory = NewCellFactory(folder.Path, connectionString);
        using var author = factory.CreateClient().SignedInAs("author");

        using var accepted = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url, Host, """{ "text": "Add a cost centre.", "autoAccept": true }""");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var sent = await StudioPublishTests.ReadAsync(accepted);
        var id = sent.RootElement.GetProperty("id").GetString()!;
        Assert.Equal($"{Url}/{id}", accepted.Headers.Location?.OriginalString);
        Assert.Equal("planning", sent.RootElement.GetProperty("state").GetString());
        Assert.Equal("PurchaseRequests", sent.RootElement.GetProperty("application").GetString());
        Assert.Equal("author", sent.RootElement.GetProperty("author").GetString());
        Assert.Equal("Author User", sent.RootElement.GetProperty("authorName").GetString());
        Assert.Equal("Add a cost centre.", sent.RootElement.GetProperty("text").GetString());
        Assert.True(sent.RootElement.GetProperty("autoAccept").GetBoolean());
        Assert.Equal(0, sent.RootElement.GetProperty("round").GetInt32());
        Assert.False(sent.RootElement.TryGetProperty("steps", out _));
        Assert.Equal(1L, await CountRequestsAsync(connectionString));

        await SetStateAsync(connectionString, id, "awaitingApproval");
        using var changed = await StudioPublishTests.SendAsync(
            author, HttpMethod.Post, $"{Url}/{id}/change-plan", Host, """{ "comment": "Also add it to the form." }""");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var changedBody = await StudioPublishTests.ReadAsync(changed);
        Assert.Equal("planning", changedBody.RootElement.GetProperty("state").GetString());
        Assert.Equal(1L, await CountRequestsAsync(connectionString));

        using var read = await StudioPublishTests.GetJsonAsync(author, $"{Url}/{id}", Host);
        var steps = read.RootElement.GetProperty("steps");
        Assert.Equal(1, steps.GetArrayLength());
        Assert.Equal("planChange", steps[0].GetProperty("kind").GetString());
        Assert.Equal("Also add it to the form.", steps[0].GetProperty("summary").GetString());
        Assert.Equal(JsonValueKind.Null, steps[0].GetProperty("outcome").ValueKind);

        using var refused = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url, Host, Text);
        await AssertQuotaRefusalAsync(refused, used: 1);
        Assert.Equal(1L, await CountRequestsAsync(connectionString));
    }

    [Fact]
    public async Task A_tenant_at_its_quota_gets_the_quota_403_and_no_row_or_audit_record_is_written()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("authoring_requests_quota", CancellationToken);
        await using var factory = NewCellFactory(folder.Path, connectionString);
        using var author = factory.CreateClient().SignedInAs("author");
        using var employee = factory.CreateClient().SignedInAs("employee");

        using var first = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url, Host, Text);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        // A dropped request still counts, and the quota is the tenant's, not the author's.
        var id = (await StudioPublishTests.ReadAsync(first)).RootElement.GetProperty("id").GetString()!;
        using var dropped = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{id}/drop", Host);
        Assert.Equal(HttpStatusCode.OK, dropped.StatusCode);
        foreach (var client in new[] { author, employee })
        {
            using var refused = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);
            await AssertQuotaRefusalAsync(refused, used: 1);
        }

        Assert.Equal(1L, await CountRequestsAsync(connectionString));
        Assert.Equal(1L, await CountAuditAsync(connectionString, AuditActions.AuthoringRequestSent));

        // The quota is checked before the application and the body, so a refused request evaluates nothing.
        using var unknown = await StudioPublishTests.SendAsync(author, HttpMethod.Post, "/api/studio/apps/NoSuchApplication/authoring-requests", Host, "not json");
        await AssertQuotaRefusalAsync(unknown, used: 1);

        // Sends at the same time cannot pass the quota together.
        var lastDatabase = await _database.CreateDatabaseAsync("authoring_requests_race", CancellationToken);
        await using var raceFactory = NewCellFactory(folder.Path, lastDatabase);
        using var racer = raceFactory.CreateClient().SignedInAs("author");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await StudioPublishTests.SendAsync(racer, HttpMethod.Post, Url, Host, Text);
            return response.StatusCode;
        }));
        Assert.Equal(1, results.Count(status => status == HttpStatusCode.Accepted));
        Assert.Equal(5, results.Count(status => status == HttpStatusCode.Forbidden));
        Assert.Equal(1L, await CountRequestsAsync(lastDatabase));
    }

    [Fact]
    public async Task Another_author_cannot_approve_change_or_drop_a_request_and_a_person_without_the_author_role_cannot_send()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("authoring_requests_roles", CancellationToken);
        await using var factory = NewCellFactory(folder.Path, connectionString);
        using var author = factory.CreateClient().SignedInAs("author");
        using var employee = factory.CreateClient().SignedInAs("employee");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var nobody = factory.CreateClient();

        using var sent = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url, Host, Text);
        var id = (await StudioPublishTests.ReadAsync(sent)).RootElement.GetProperty("id").GetString()!;
        await SetStateAsync(connectionString, id, "awaitingApproval");

        foreach (var (action, body) in new[] { ("approve", null), ("change-plan", """{ "comment": "No." }"""), ("drop", null) })
        {
            using var refused = await StudioPublishTests.SendAsync(employee, HttpMethod.Post, $"{Url}/{id}/{action}", Host, body);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("Only the author of the request may do this.", (await StudioPublishTests.ReadAsync(refused)).RootElement.GetProperty("title").GetString());
        }

        Assert.Equal("awaitingApproval", await StateAsync(connectionString, id));
        Assert.Equal(0L, await CountAuditAsync(connectionString, AuditActions.AuthoringRequestApproved));
        Assert.Equal(0L, await CountAuditAsync(connectionString, AuditActions.AuthoringRequestDropped));
        Assert.Equal(0L, await ScalarAsync(connectionString, "SELECT count(*) FROM axis.authoring_request_steps"));

        // Any author lists and reads the requests of an application.
        using var listed = await StudioPublishTests.GetJsonAsync(employee, Url, Host);
        Assert.Equal(id, listed.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
        using var read = await StudioPublishTests.GetJsonAsync(employee, $"{Url}/{id}", Host);
        Assert.Equal("author", read.RootElement.GetProperty("author").GetString());
        Assert.Equal("awaitingApproval", read.RootElement.GetProperty("state").GetString());

        foreach (var client in new[] { requester, nobody })
        {
            var expected = ReferenceEquals(client, nobody) ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            using var send = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);
            Assert.Equal(expected, send.StatusCode);
            using var list = await StudioPublishTests.SendAsync(client, HttpMethod.Get, Url, Host);
            Assert.Equal(expected, list.StatusCode);
            using var approve = await StudioPublishTests.SendAsync(client, HttpMethod.Post, $"{Url}/{id}/approve", Host);
            Assert.Equal(expected, approve.StatusCode);
        }

        Assert.Equal(1L, await CountRequestsAsync(connectionString));
        Assert.Equal("awaitingApproval", await StateAsync(connectionString, id));
    }

    [Fact]
    public async Task Sending_approving_and_dropping_each_write_one_audit_record_with_the_author_the_application_and_the_request()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("authoring_requests_audit", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", Host, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");

        var approved = await SendRequestAsync(author);
        var dropped = await SendRequestAsync(author);

        // A request in planning cannot be approved, and nothing is written for the refusal.
        using var early = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{approved}/approve", Host);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        using var earlyChange = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{approved}/change-plan", Host, """{ "comment": "x" }""");
        Assert.Equal(HttpStatusCode.Conflict, earlyChange.StatusCode);
        Assert.Equal(0L, await CountAuditAsync(connectionString, AuditActions.AuthoringRequestApproved));

        await SetStateAsync(connectionString, approved, "awaitingApproval");
        using var approve = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{approved}/approve", Host);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        using var approveBody = await StudioPublishTests.ReadAsync(approve);
        Assert.Equal("working", approveBody.RootElement.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, approveBody.RootElement.GetProperty("approvedAt").ValueKind);

        using var drop = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{dropped}/drop", Host);
        Assert.Equal(HttpStatusCode.OK, drop.StatusCode);
        using var dropBody = await StudioPublishTests.ReadAsync(drop);
        Assert.Equal("dropped", dropBody.RootElement.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, dropBody.RootElement.GetProperty("finishedAt").ValueKind);

        // A dropped request cannot be dropped again, approved or changed.
        using var again = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{dropped}/drop", Host);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        foreach (var (action, id) in new[] { (AuditActions.AuthoringRequestSent, approved), (AuditActions.AuthoringRequestSent, dropped), (AuditActions.AuthoringRequestApproved, approved), (AuditActions.AuthoringRequestDropped, dropped) })
        {
            var matching = await ScalarAsync(
                connectionString,
                $"""
                SELECT count(*) FROM axis.audit_records a JOIN axis.authoring_requests r ON r.application_id = a.application_id
                WHERE a.action = '{action}' AND a.actor = 'author' AND a.details->>'requestId' = '{id}' AND r.id = '{id}'
                  AND a.entity_id IS NULL AND a.record_id IS NULL AND a.process_instance_id IS NULL
                """);
            Assert.Equal(1L, matching);
        }

        Assert.Equal(1L, await CountAuditAsync(connectionString, AuditActions.AuthoringRequestApproved));
        Assert.Equal(1L, await CountAuditAsync(connectionString, AuditActions.AuthoringRequestDropped));
        Assert.Equal(2L, await CountAuditAsync(connectionString, AuditActions.AuthoringRequestSent));
    }

    [Fact]
    public async Task The_list_is_newest_first_and_the_body_and_the_ids_are_checked()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("authoring_requests_list", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", Host, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");

        var first = await SendRequestAsync(author);
        var second = await SendRequestAsync(author);
        using var list = await StudioPublishTests.GetJsonAsync(author, Url, Host);
        Assert.Equal([second, first], list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());

        foreach (var body in new[]
        {
            "{}",
            """{ "text": "   " }""",
            """{ "text": 5 }""",
            $$"""{ "text": "{{new string('x', 4001)}}" }""",
            """{ "text": "x", "autoAccept": "yes" }""",
            """{ "text": "x", "other": 1 }""",
            "[]",
            "not json",
        })
        {
            using var response = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url, Host, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var wrongType = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url, Host, Text, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        using var limit = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url, Host, $$"""{ "text": "{{new string('x', 4000)}}" }""");
        Assert.Equal(HttpStatusCode.Accepted, limit.StatusCode);
        using var unknown = await StudioPublishTests.SendAsync(author, HttpMethod.Post, "/api/studio/apps/NoSuchApplication/authoring-requests", Host, Text);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(3L, await CountRequestsAsync(connectionString));

        await SetStateAsync(connectionString, first, "awaitingApproval");
        foreach (var body in new[] { null, "{}", """{ "comment": "" }""", """{ "comment": 1 }""", $$"""{ "comment": "{{new string('x', 2001)}}" }""", """{ "comment": "x", "other": 1 }""" })
        {
            using var response = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{first}/change-plan", Host, body);
            Assert.Equal(body is null ? HttpStatusCode.UnsupportedMediaType : HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var plainBody = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{first}/change-plan", Host, """{ "comment": "x" }""", "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, plainBody.StatusCode);
        Assert.Equal(0L, await ScalarAsync(connectionString, "SELECT count(*) FROM axis.authoring_request_steps"));

        foreach (var id in new[] { "not-a-guid", Guid.NewGuid().ToString("D") })
        {
            using var read = await StudioPublishTests.SendAsync(author, HttpMethod.Get, $"{Url}/{id}", Host);
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            using var drop = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{id}/drop", Host);
            Assert.Equal(HttpStatusCode.NotFound, drop.StatusCode);
        }
    }

    private static WebApplicationFactory<Program> NewCellFactory(string folder, string connectionString) =>
        StudioPublishTests.NewFactory(folder, [("a", Host, connectionString)])
            .WithWebHostBuilder(builder => builder.InCell(1, QuotaKeys.AuthoringRequests));

    private static async Task<string> SendRequestAsync(HttpClient client)
    {
        using var response = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = await StudioPublishTests.ReadAsync(response);
        return body.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task AssertQuotaRefusalAsync(HttpResponseMessage response, long used)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = await StudioPublishTests.ReadAsync(response);
        Assert.Equal("quota-exceeded", problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal(QuotaKeys.AuthoringRequests, problem.RootElement.GetProperty("quota").GetString());
        Assert.Equal(1L, problem.RootElement.GetProperty("limit").GetInt64());
        Assert.Equal(used, problem.RootElement.GetProperty("used").GetInt64());
    }

    // Plays the agent host, which nothing else moves out of planning yet.
    private static async Task SetStateAsync(string connectionString, string id, string state) =>
        await PostgreSqlServer.AdminScalarAsync(connectionString, $"UPDATE axis.authoring_requests SET state = '{state}' WHERE id = '{id}'", CancellationToken);

    private static async Task<string?> StateAsync(string connectionString, string id) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT state FROM axis.authoring_requests WHERE id = '{id}'", CancellationToken);

    private static Task<long> CountRequestsAsync(string connectionString) =>
        ScalarAsync(connectionString, "SELECT count(*) FROM axis.authoring_requests");

    private static Task<long> CountAuditAsync(string connectionString, string action) =>
        ScalarAsync(connectionString, $"SELECT count(*) FROM axis.audit_records WHERE action = '{action}'");

    private static async Task<long> ScalarAsync(string connectionString, string sql) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, sql, CancellationToken))!;
}
