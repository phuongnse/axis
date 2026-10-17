using System.Net;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A task pinned to release A writes its subject while release B is active. When B computes the entity
/// differently, the write leaves a row that B's backfill must take, so it marks the entity pending again.
/// When B computes it the same way, the write adds nothing.
/// </summary>
public sealed class OlderReleaseWriteTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tasks = "/api/apps/StepApp/tasks";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Completing_a_task_of_an_older_release_with_different_computed_definitions_marks_the_entity_pending_and_the_backfill_restamps_the_row()
    {
        var (task, subject, instance) = await CreateTaskAsync(TenantA, "Decide");
        var other = await fixture.InsertRequestAsync(TenantA, null);
        var waiting = await fixture.SnapshotAsync(TenantA, instance);
        var releaseB = await fixture.ActivateEditedAsync(TenantA, (path, content) =>
            path == Path.Combine("entities", "request.json")
                ? content.Replace(
                    "\"fields\": [",
                    "\"fields\": [\n    { \"name\": \"doubled\", \"type\": \"integer\", \"expression\": \"divisor * 2\" },",
                    StringComparison.Ordinal)
                : content);
        Assert.Equal(0L, await fixture.CountPendingComputedFieldsAsync(TenantA));
        Assert.Equal(releaseB, await fixture.ComputedReleaseAsync(TenantA, subject));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await CompleteAsync(
            maria, TenantA, task, $$"""{ "outcome": "approve", "values": { "comment": "ok" }, "version": {{waiting.SubjectVersion}} }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fixture.ReleaseId(TenantA), await fixture.ComputedReleaseAsync(TenantA, subject));
        Assert.Equal(1L, await fixture.CountPendingComputedFieldsAsync(TenantA));
        Assert.Equal(1L, await fixture.CountBackfillItemsAsync(TenantA));

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal(releaseB, await fixture.ComputedReleaseAsync(TenantA, subject));
        Assert.Equal(releaseB, await fixture.ComputedReleaseAsync(TenantA, other));
        Assert.Equal(0L, await fixture.CountPendingComputedFieldsAsync(TenantA));
        Assert.Equal(0L, await fixture.CountBackfillItemsAsync(TenantA));
    }

    [Fact]
    public async Task Completing_a_task_of_an_older_release_with_the_same_computed_definitions_marks_nothing_pending()
    {
        var (task, subject, instance) = await CreateTaskAsync(TenantB, "Decide");
        var waiting = await fixture.SnapshotAsync(TenantB, instance);
        await fixture.ActivateEditedAsync(TenantB, (path, content) =>
            path == Path.Combine("texts", "en.json")
                ? content.Replace("Approve the request", "Approve this request", StringComparison.Ordinal)
                : content);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await CompleteAsync(
            maria, TenantB, task, $$"""{ "outcome": "approve", "values": { "comment": "ok" }, "version": {{waiting.SubjectVersion}} }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fixture.ReleaseId(TenantB), await fixture.ComputedReleaseAsync(TenantB, subject));
        Assert.Equal(0L, await fixture.CountPendingComputedFieldsAsync(TenantB));
        Assert.Equal(0L, await fixture.CountBackfillItemsAsync(TenantB));
    }

    // Creates a request in a department managed by maria, starts the process on release A and runs the tenant's
    // work items, so the instance waits on its task. Returns the task's, the request's and the instance's ids.
    private async Task<(Guid Task, Guid Subject, Guid Instance)> CreateTaskAsync(string tenant, string process)
    {
        var department = await fixture.InsertDepartmentAsync(tenant, "maria");
        var subject = await fixture.InsertRequestAsync(tenant, department);
        var instance = await fixture.StartAsync(tenant, process, subject);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        return (await fixture.TaskIdAsync(tenant, instance), subject, instance);
    }

    private static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, string tenant, Guid task, string body)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", Host(tenant), body);
        return await client.SendAsync(request, CancellationToken);
    }
}
