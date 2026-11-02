using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>approvals</c> module's contracts on every write path, in ApprovalsApp. A requester who sets their own
/// submitted request to <c>approved</c>, <c>returned</c> or <c>rejected</c> gets the lifecycle <c>409</c> at
/// <c>/values/status</c>. A process step that sets <c>approved</c> after another user's task fails the instance while
/// <c>step</c> is below <c>stepsRequired</c>, and approves the request once they are equal. Two process writes that move
/// <c>step</c> after one task completion are refused on the second. A requester's change of <c>amount</c> on a
/// submitted request is a <c>400</c> at <c>/values/amount</c>, and the same change in <c>draft</c> succeeds. A request
/// type needs a step without <c>minAmount</c>. A member lists their own requests and the ones whose
/// <c>approverUser</c> or <c>onBehalfOf</c> is theirs, and no other. The tests use SQL to put requests into <c>submitted</c>
/// directly, which skips the contract.
/// </summary>
public sealed class ApprovalsContractTests(ApprovalsAppFixture fixture) : IClassFixture<ApprovalsAppFixture>
{
    private const string App = "/api/apps/ApprovalsApp";
    private const string RequestTypes = $"{App}/entities/approvals/RequestType/records";
    private const string Requests = $"{App}/entities/approvals/Request/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("approved")]
    [InlineData("returned")]
    [InlineData("rejected")]
    public async Task A_requester_who_decides_their_own_submitted_request_gets_the_lifecycle_409(string status)
    {
        var type = await CreateTypeAsync();
        var request = await fixture.InsertRequestAsync(type, "appr-ann", "submitted", stepsRequired: 1, step: 1);
        using var client = fixture.CreateClient().SignedInAs("appr-ann");
        var version = await VersionAsync(client, request);

        using var response = await PatchAsync(client, request, version, $$"""{ "status": "{{status}}" }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("The record's state does not allow this change.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(["/values/status"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(("submitted", 1L), await fixture.RequestAsync(request));
    }

    [Fact]
    public async Task A_process_step_approves_after_another_users_task_only_once_every_step_is_decided()
    {
        var type = await CreateTypeAsync();
        var early = await fixture.InsertRequestAsync(type, "appr-ann", "submitted", stepsRequired: 2, step: 1);
        var complete = await fixture.InsertRequestAsync(type, "appr-ann", "submitted", stepsRequired: 1, step: 1);

        var refused = await DecideAsync(early, "approve", "markApproved");
        var approved = await DecideAsync(complete, "approve", "markApproved");

        Assert.Equal("failed", (await fixture.Steps.InstanceAsync(refused)).State);
        Assert.Equal(("submitted", 1L), await fixture.RequestAsync(early));
        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(approved)).State);
        Assert.Equal(("approved", 1L), await fixture.RequestAsync(complete));
    }

    [Fact]
    public async Task Two_process_writes_that_move_the_step_after_one_task_fail_the_instance_on_the_second()
    {
        var type = await CreateTypeAsync();
        var request = await fixture.InsertRequestAsync(type, "appr-ann", "submitted", stepsRequired: 2, step: 0);

        var instance = await DecideAsync(request, "advance", "first");

        Assert.Equal("failed", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal(("submitted", 1L), await fixture.RequestAsync(request));
    }

    [Fact]
    public async Task A_requester_changes_the_amount_in_draft_and_not_after_the_submit()
    {
        var type = await CreateTypeAsync();
        var draft = await fixture.InsertRequestAsync(type, "appr-bob", "draft", amount: 5m);
        var submitted = await fixture.InsertRequestAsync(type, "appr-bob", "submitted", stepsRequired: 1, step: 0, amount: 5m);
        using var client = fixture.CreateClient().SignedInAs("appr-bob");

        using (var allowed = await PatchAsync(client, draft, await VersionAsync(client, draft), """{ "amount": 10 }"""))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await PatchAsync(client, submitted, await VersionAsync(client, submitted), """{ "amount": 10 }""");

        using var problem = await ReadProblemAsync(refused, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/amount", error.Name);
        Assert.Equal(["approvals.request.frozenAfterSubmit"], error.Value.EnumerateArray().Select(message => message.GetString()));
        using var read = await GetAsync(client, $"{Requests}/{submitted:D}");
        Assert.Equal(5m, read.RootElement.GetProperty("values").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task A_request_type_needs_a_step_without_a_minimum_amount()
    {
        using var admin = fixture.CreateClient().SignedInAs("appr-admin");

        using var refused = await SendAsync(
            admin,
            HttpMethod.Post,
            RequestTypes,
            $$"""
            { "values": { "name": "Capped", "code": "{{Code()}}", "steps": [
                { "sequence": 1, "approver": "lineManager", "minAmount": 100 },
                { "sequence": 2, "approver": "departmentHead", "minAmount": 1000 }
            ] } }
            """);
        using var allowed = await SendAsync(
            admin,
            HttpMethod.Post,
            RequestTypes,
            $$"""
            { "values": { "name": "Open", "code": "{{Code()}}", "steps": [
                { "sequence": 1, "approver": "lineManager" },
                { "sequence": 2, "approver": "departmentHead", "minAmount": 1000 }
            ] } }
            """);

        using var problem = await ReadProblemAsync(refused, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/steps", error.Name);
        Assert.Equal(["approvals.requestType.hasStepForEveryAmount"], error.Value.EnumerateArray().Select(message => message.GetString()));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task A_member_lists_their_own_requests_and_the_ones_they_are_to_decide_and_no_other()
    {
        var type = await CreateTypeAsync();
        var own = await fixture.InsertRequestAsync(type, "appr-carl", "draft");
        var toDecide = await fixture.InsertRequestAsync(type, "appr-ann", "submitted", stepsRequired: 1, step: 0, approverUser: "appr-carl");
        var forSomeone = await fixture.InsertRequestAsync(type, "appr-bob", "submitted", stepsRequired: 1, step: 0, approverUser: "appr-ann", onBehalfOf: "appr-carl");
        await fixture.InsertRequestAsync(type, "appr-ann", "submitted", stepsRequired: 1, step: 0, approverUser: "appr-bob");
        await fixture.InsertRequestAsync(type, "appr-bob", "draft");
        using var client = fixture.CreateClient().SignedInAs("appr-carl");

        using var list = await GetAsync(client, Requests);

        Assert.Equal(
            new[] { own, toDecide, forSomeone }.Order(),
            list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Order());
        Assert.Equal(3, list.RootElement.GetProperty("totalCount").GetInt32());
    }

    // Starts DecideRequest on the request as appr-ann, completes its task as appr-bob and runs the worker.
    private async Task<Guid> DecideAsync(Guid request, string outcome, string next)
    {
        var instance = await fixture.Steps.StartOnSubjectAsync("DecideRequest", request, fixture.Steps.FirstReleaseId, "appr-ann");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", (await fixture.Steps.InstanceAsync(instance)).State);
        await fixture.Steps.CompleteTaskAsync(instance, outcome, next, "appr-bob", CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        return instance;
    }

    private async Task<Guid> CreateTypeAsync()
    {
        using var admin = fixture.CreateClient().SignedInAs("appr-admin");
        using var response = await SendAsync(
            admin,
            HttpMethod.Post,
            RequestTypes,
            $$"""{ "values": { "name": "Type", "code": "{{Code()}}", "steps": [{ "sequence": 1, "approver": "lineManager" }] } }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static string Code() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static async Task<long> VersionAsync(HttpClient client, Guid id)
    {
        using var body = await GetAsync(client, $"{Requests}/{id:D}");
        return body.RootElement.GetProperty("version").GetInt64();
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, long version, string values) =>
        SendAsync(client, HttpMethod.Patch, $"{Requests}/{id:D}", $$"""{ "version": {{version}}, "values": {{values}} }""");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null) =>
        client.SendAsync(Request(method, path, ApprovalsAppFixture.Host, body), CancellationToken);
}
