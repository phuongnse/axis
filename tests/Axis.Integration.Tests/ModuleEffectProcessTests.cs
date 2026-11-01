using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A process makes the transition of a module entity the way the record API does, and its effect records are audited and
/// fail the write the same way. Completing a task that sets a claim to <c>approved</c> creates one entry for the completing
/// user and writes its <c>record.created</c> audit record with the effect's point. An <c>updateRecord</c> step that approves
/// a claim creates one entry for the instance's accountable user, audited as <c>system</c>. An effect that fails answers
/// <c>409</c> with the point and leaves the task open, or ends the instance <c>failed</c> with the point in its error, and
/// no entry is left.
/// </summary>
public sealed class ModuleEffectProcessTests(ModuleEffectFixture fixture) : IClassFixture<ModuleEffectFixture>
{
    private const string Point = "Claim/effects/entryRecorded";
    private const string Complete = """{ "outcome": "done", "version": 1, "values": { "status": "approved" } }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_task_completion_audits_its_effect_record_for_the_completing_user()
    {
        var claim = await fixture.InsertClaimAsync("submitted", 40);
        var task = await StartReviewAsync(claim);
        using var client = fixture.CreateClient().SignedInAs("clerk");

        using var response = await CompleteAsync(client, task);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([((long?)40, "clerk", "open")], await fixture.EntriesAsync(claim));
        var entry = Assert.Single(await fixture.EntryIdsAsync(claim));
        var (actor, _, details) = Assert.Single(await fixture.AuditsAsync(entry), audit => audit.Action == "record.created");
        Assert.Equal("clerk", actor);
        AssertEffectDetails(details);
    }

    [Fact]
    public async Task A_task_completion_whose_effect_fails_is_409_with_the_point_and_the_task_stays_open()
    {
        var claim = await fixture.InsertClaimAsync("submitted", -1);
        var task = await StartReviewAsync(claim);
        using var client = fixture.CreateClient().SignedInAs("clerk");

        using var response = await CompleteAsync(client, task);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("A required change could not be recorded.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(Point, problem.RootElement.GetProperty("point").GetString());
        Assert.Equal("open", (await fixture.Steps.TasksAsync(task.Instance)).Single().State);
        Assert.Equal(("submitted", 1), await fixture.ClaimAsync(claim));
        Assert.Empty(await fixture.EntryIdsAsync(claim));
    }

    [Fact]
    public async Task A_step_audits_its_effect_record_as_system_and_creates_it_for_the_accountable_user()
    {
        var claim = await fixture.InsertClaimAsync("submitted", 40);

        var instance = await fixture.Steps.StartOnSubjectAsync("ApproveClaim", claim, fixture.Steps.FirstReleaseId, "starter-1");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal([((long?)40, "starter-1", "open")], await fixture.EntriesAsync(claim));
        var entry = Assert.Single(await fixture.EntryIdsAsync(claim));
        var (actor, _, details) = Assert.Single(await fixture.AuditsAsync(entry), audit => audit.Action == "record.created");
        Assert.Equal("system", actor);
        AssertEffectDetails(details);
    }

    [Fact]
    public async Task A_step_whose_effect_fails_ends_the_instance_failed_with_the_point_and_leaves_no_entry()
    {
        var claim = await fixture.InsertClaimAsync("submitted", -1);

        var instance = await fixture.Steps.StartOnSubjectAsync("ApproveClaim", claim, fixture.Steps.FirstReleaseId, "starter-1");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("failed", (await fixture.Steps.InstanceAsync(instance)).State);
        var error = Assert.Single(await fixture.Steps.HistoryAsync(instance), row => row.Error is not null).Error;
        Assert.Contains($"named point {Point}", error!, StringComparison.Ordinal);
        Assert.Equal(("submitted", 1), await fixture.ClaimAsync(claim));
        Assert.Empty(await fixture.EntryIdsAsync(claim));
    }

    private async Task<(Guid Instance, Guid Task)> StartReviewAsync(Guid claim)
    {
        var instance = await fixture.Steps.StartOnSubjectAsync("ReviewClaim", claim, fixture.Steps.FirstReleaseId);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var task = Assert.Single(await fixture.Steps.TasksAsync(instance));
        Assert.Equal("open", task.State);
        return (instance, task.Id);
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, (Guid Instance, Guid Task) task) =>
        client.SendAsync(
            Request(HttpMethod.Post, $"/api/apps/ModuleEffectApp/tasks/{task.Task:D}/complete", ModuleEffectFixture.Host, Complete),
            CancellationToken);

    private static void AssertEffectDetails(string details)
    {
        using var parsed = JsonDocument.Parse(details);
        Assert.Equal(["effect", "version"], parsed.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(1, parsed.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(Point, parsed.RootElement.GetProperty("effect").GetString());
    }
}
