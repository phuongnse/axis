using System.Net;
using System.Text.Json;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>signoffs</c> module's <c>approve</c> guard, <c>accountableUser() != null and accountableUser() != requester</c>,
/// reads who answers for the write on every path. A process step that approves before any task was decided runs as
/// the instance's starter, the requester, and the guard refuses it with the named point in the instance's error. The
/// same step after the user <c>approver</c> completed a task runs as that user and passes. A step of an instance that
/// a schedule trigger started has no accountable user and is refused. The record API refuses the requester's own
/// approval with a <c>409</c>, allows another user who holds the grant, and refuses a user without it with a <c>403</c>.
/// </summary>
public sealed class ModuleAccountabilityGuardTests(ModuleAccountabilityFixture fixture) : IClassFixture<ModuleAccountabilityFixture>
{
    private const string App = "/api/apps/ModuleAccountabilityApp";
    private const string Records = $"{App}/entities/signoffs/Request/records";
    private const string Named = "named point Request/transitions/approve";

    private static readonly DateTimeOffset _occurrence = DateTimeOffset.Parse("2026-01-01T05:00:00Z");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_step_before_any_task_runs_as_the_starter_and_the_guard_refuses_it()
    {
        var request = await fixture.InsertRequestAsync($"Starter {Guid.NewGuid():N}", "requester", "submitted");
        var instance = await StartAsync("ApproveAtOnce", request);

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        await AssertRefusedAsync("ApproveAtOnce", instance, request);
    }

    [Fact]
    public async Task The_same_step_after_another_users_task_completion_passes_the_guard()
    {
        var request = await fixture.InsertRequestAsync($"Task {Guid.NewGuid():N}", "requester", "submitted");
        var instance = await StartAsync("ReviewThenApprove", request);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal("submitted", (await fixture.RequestAsync(request)).Status);

        await fixture.Steps.CompleteTaskAsync(instance, "approve", "approve", "approver", CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal("approved", (await fixture.RequestAsync(request)).Status);
    }

    [Fact]
    public async Task A_step_of_an_instance_a_schedule_started_has_no_accountable_user_and_the_guard_refuses_it()
    {
        var request = await fixture.InsertRequestAsync($"Scheduled {Guid.NewGuid():N}", "requester", "submitted");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var trigger = fixture.Steps.Model.Triggers.Single(candidate => candidate.Name == "ApproveDaily").Id;
        await fixture.Steps.ScalarAsync("UPDATE axis.trigger_schedules SET next_occurrence_at = @at WHERE trigger_id = @trigger", ("at", _occurrence), ("trigger", trigger));
        await fixture.Steps.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = @at WHERE kind = @kind AND trigger_id = @trigger",
            ("at", _occurrence),
            ("kind", ProcessStarts.TriggerScheduleWorkItemKind),
            ("trigger", trigger));

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        var instance = (Guid)(await fixture.Steps.ScalarAsync(
            "SELECT id FROM axis.process_instances WHERE subject_id = @subject",
            ("subject", request)) ?? throw new InvalidOperationException("The schedule started no instance."));
        await AssertRefusedAsync("ApproveAtOnce", instance, request);
    }

    [Fact]
    public async Task The_record_api_refuses_the_requester_allows_another_user_with_the_grant_and_refuses_a_user_without_it()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(requester);
        version = await PatchAsync(requester, id, version, "submitted");

        using (var own = await PatchResponseAsync(requester, id, version))
        {
            using var problem = await ReadProblemAsync(own, HttpStatusCode.Conflict);
            Assert.Equal(
                ["/values/status"],
                problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        using (var outsider = fixture.CreateClient().SignedInAs("outsider"))
        using (var denied = await PatchResponseAsync(outsider, id, version))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        Assert.Equal(("submitted", version), await fixture.RequestAsync(id));

        using var approver = fixture.CreateClient().SignedInAs("approver");
        using (var allowed = await PatchResponseAsync(approver, id, version))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        Assert.Equal("approved", (await fixture.RequestAsync(id)).Status);
    }

    private async Task AssertRefusedAsync(string process, Guid instance, Guid request)
    {
        Assert.Equal("failed", (await fixture.Steps.InstanceAsync(instance)).State);
        using var client = fixture.CreateClient().SignedInAs("requester");
        using var response = await SendAsync(client, HttpMethod.Get, $"{App}/processes/{process}/instances/{instance:D}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var path = Assert.Single(detail.RootElement.GetProperty("path").EnumerateArray());
        Assert.Equal("approve", path.GetProperty("step").GetString());
        Assert.Contains(Named, path.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal("submitted", (await fixture.RequestAsync(request)).Status);
    }

    private async Task<Guid> StartAsync(string process, Guid subject)
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        using var response = await SendAsync(client, HttpMethod.Post, $"{App}/processes/{process}/instances", $$"""{ "subjectId": "{{subject:D}}" }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, long Version)> CreateAsync(HttpClient client)
    {
        using var response = await SendAsync(
            client, HttpMethod.Post, Records, $$"""{ "values": { "title": "Request {{Guid.NewGuid():N}}", "requester": "requester", "status": "draft" } }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64());
    }

    // Moves the record to the state and returns its new version.
    private static async Task<long> PatchAsync(HttpClient client, Guid id, long version, string status)
    {
        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "{{status}}" } }""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("version").GetInt64();
    }

    private static Task<HttpResponseMessage> PatchResponseAsync(HttpClient client, Guid id, long version) =>
        SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "approved" } }""");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body) =>
        client.SendAsync(Request(method, path, ModuleAccountabilityFixture.Host, body), CancellationToken);
}
