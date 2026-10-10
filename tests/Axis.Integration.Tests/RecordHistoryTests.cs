using System.Net;
using System.Text.Json;
using Axis.Data.Audit;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordHistoryTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Records = "/api/apps/StepApp/entities/Request/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_an_update_a_process_start_and_a_task_decision_are_listed_newest_first_with_their_actor()
    {
        using var maria = await SignInAsync("maria", TenantA);
        var department = await fixture.InsertDepartmentAsync(TenantA, "maria");
        var subject = await CreateAsync(maria, TenantA, $$"""{ "values": { "title": "Chairs", "department": "{{department:D}}" } }""");
        await UpdateAsync(maria, subject, TenantA, """{ "version": 1, "values": { "title": "Desks" } }""");

        Guid instance;
        using (var start = RecordApiFixture.Request(
            HttpMethod.Post, "/api/apps/StepApp/processes/Decide/instances", Host(TenantA), $$"""{ "subjectId": "{{subject:D}}" }"""))
        {
            start.Headers.TryAddWithoutValidation("Idempotency-Key", $"history-{subject:N}");
            using var started = await maria.SendAsync(start, CancellationToken);
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
            using var body = await ReadJsonAsync(started);
            instance = body.RootElement.GetProperty("id").GetGuid();
        }

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
        var task = await fixture.TaskIdAsync(TenantA, instance);
        using (var complete = RecordApiFixture.Request(
            HttpMethod.Post, $"/api/apps/StepApp/tasks/{task:D}/complete", Host(TenantA), """{ "outcome": "approve", "values": { "comment": "Fine" }, "version": 2 }"""))
        using (var completed = await maria.SendAsync(complete, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        }

        using var history = await GetJsonAsync(maria, $"{Records}/{subject:D}/history", TenantA);

        var root = history.RootElement;
        Assert.Equal(["items", "page", "pageSize", "totalCount"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(items.Count, root.GetProperty("totalCount").GetInt64());
        Assert.Equal(
            ["id", "occurredAt", "actor", "actorName", "action", "processInstanceId", "details"],
            items[0].EnumerateObject().Select(property => property.Name));
        var times = items.Select(item => item.GetProperty("occurredAt")).ToList();
        Assert.All(times, time => Assert.EndsWith("Z", time.GetString(), StringComparison.Ordinal));
        Assert.Equal(times.Select(time => time.GetDateTimeOffset()).OrderDescending(), times.Select(time => time.GetDateTimeOffset()));

        string[] actions = [AuditActions.TaskCompleted, AuditActions.ProcessStarted, AuditActions.RecordUpdated, AuditActions.RecordCreated];
        var listed = items.Where(item => actions.Contains(item.GetProperty("action").GetString())).ToList();
        Assert.Equal(actions, listed.Select(item => item.GetProperty("action").GetString()));
        Assert.All(listed, item =>
        {
            Assert.Equal("maria", item.GetProperty("actor").GetString());
            Assert.Equal("Maria Manager", item.GetProperty("actorName").GetString());
        });
        Assert.Equal("approve", listed[0].GetProperty("details").GetProperty("outcome").GetString());
        Assert.Equal(instance, listed[0].GetProperty("processInstanceId").GetGuid());
        Assert.Equal(instance, listed[1].GetProperty("processInstanceId").GetGuid());
        Assert.Equal(["title"], listed[2].GetProperty("details").GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
        Assert.Equal(JsonValueKind.Null, listed[3].GetProperty("processInstanceId").ValueKind);

        // The worker's entries have no test user, so they have no display name.
        Assert.All(
            items.Where(item => item.GetProperty("actor").GetString() == AuditActors.System),
            item => Assert.Equal(JsonValueKind.Null, item.GetProperty("actorName").ValueKind));

        using var second = await GetJsonAsync(maria, $"{Records}/{subject:D}/history?page=2&pageSize=1", TenantA);
        Assert.Equal(
            items[1].GetProperty("id").GetGuid(),
            Assert.Single(second.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(items.Count, second.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_records_history_holds_only_its_own_tenants_audit_records()
    {
        using var maria = await SignInAsync("maria", TenantA);
        using var fiona = await SignInAsync("fiona", TenantB);
        var shared = await CreateAsync(maria, TenantA, """{ "values": { "title": "Shared" } }""");
        await UpdateAsync(maria, shared, TenantA, """{ "version": 1, "values": { "title": "Shared A" } }""");
        await fixture.InsertRequestAsync(TenantB, department: null, id: shared);
        await UpdateAsync(fiona, shared, TenantB, """{ "version": 1, "values": { "title": "Shared B" } }""");

        using var historyA = await GetJsonAsync(maria, $"{Records}/{shared:D}/history", TenantA);
        using var historyB = await GetJsonAsync(fiona, $"{Records}/{shared:D}/history", TenantB);

        var itemsA = historyA.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            [(AuditActions.RecordUpdated, "maria"), (AuditActions.RecordCreated, "maria")],
            itemsA.Select(item => (item.GetProperty("action").GetString(), item.GetProperty("actor").GetString())));
        Assert.Equal(2, historyA.RootElement.GetProperty("totalCount").GetInt64());
        var itemB = Assert.Single(historyB.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AuditActions.RecordUpdated, itemB.GetProperty("action").GetString());
        Assert.Equal("fiona", itemB.GetProperty("actor").GetString());
        Assert.Equal("Fiona Finance", itemB.GetProperty("actorName").GetString());
        Assert.Equal(1, historyB.RootElement.GetProperty("totalCount").GetInt64());
        Assert.DoesNotContain(itemB.GetProperty("id").GetGuid(), itemsA.Select(item => item.GetProperty("id").GetGuid()));

        var onlyA = await CreateAsync(maria, TenantA, """{ "values": { "title": "Only A" } }""");
        using var request = RecordApiFixture.Request($"{Records}/{onlyA:D}/history", Host(TenantB));
        using var response = await fiona.SendAsync(request, CancellationToken);
        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal("No record has this id.", problem.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(Records + "/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01/history", HttpStatusCode.NotFound, "No record has this id.", "")]
    [InlineData(Records + "/not-a-uuid/history", HttpStatusCode.NotFound, "No record has this id.", "")]
    [InlineData(Records + "/not-a-uuid/history?pageSize=0", HttpStatusCode.NotFound, "No record has this id.", "")]
    [InlineData("/api/apps/StepApp/entities/Nope/records/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01/history", HttpStatusCode.NotFound, "The application has no entity with this name.", "")]
    [InlineData("/api/apps/NoSuchApp/entities/Request/records/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01/history", HttpStatusCode.NotFound, "No application is active under this name.", "")]
    [InlineData("{record}/history?pageSize=0", HttpStatusCode.BadRequest, null, "pageSize")]
    [InlineData("{record}/history?page=0&pageSize=101", HttpStatusCode.BadRequest, null, "page,pageSize")]
    public async Task An_unknown_record_is_404_and_invalid_paging_is_400(string path, HttpStatusCode status, string? title, string keys)
    {
        using var client = fixture.CreateClient();
        if (path.StartsWith("{record}", StringComparison.Ordinal))
        {
            var record = await CreateAsync(client, TenantA, """{ "values": { "title": "Paged" } }""");
            path = path.Replace("{record}", $"{Records}/{record:D}", StringComparison.Ordinal);
        }

        using var request = RecordApiFixture.Request(path, Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, status);
        if (title is not null)
        {
            Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        }

        if (keys.Length > 0)
        {
            Assert.Equal(keys.Split(','), problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        }
    }

    [Fact]
    public async Task A_deleted_records_history_is_404()
    {
        using var client = fixture.CreateClient();
        var record = await CreateAsync(client, TenantA, """{ "values": { "title": "Gone" } }""");
        using (var delete = RecordApiFixture.Request(HttpMethod.Delete, $"{Records}/{record:D}", Host(TenantA)))
        using (var deleted = await client.SendAsync(delete, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using var request = RecordApiFixture.Request($"{Records}/{record:D}/history", Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> SignInAsync(string user, string tenant)
    {
        var client = fixture.CreateClient();
        using var signIn = RecordApiFixture.Request(HttpMethod.Post, "/api/test-users/sign-in", Host(tenant), $$"""{ "id": "{{user}}" }""");
        using var response = await client.SendAsync(signIn, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string tenant, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, Records, Host(tenant), body);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = await ReadJsonAsync(response);
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task UpdateAsync(HttpClient client, Guid id, string tenant, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Patch, $"{Records}/{id:D}", Host(tenant), body);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, string tenant)
    {
        using var request = RecordApiFixture.Request(path, Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
