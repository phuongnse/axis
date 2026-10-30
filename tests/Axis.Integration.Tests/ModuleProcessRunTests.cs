using System.Net;
using System.Text.Json;
using Axis.Configuration.Modules;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The worker runs an instance of a module process on the application's copy of the module's entity,
/// whether a sub-process step or an event trigger starts it, and a waiting instance finishes on the
/// release it was started on. The tests share one database and each uses records of its own. The pinned
/// release test activates a newer release, which the other tests do not depend on, because the processes
/// they use are the same in both.
/// </summary>
public sealed class ModuleProcessRunTests(ModuleProcessFixture fixture) : IClassFixture<ModuleProcessFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_start_process_action_runs_the_module_process_through_a_sub_process_step_on_the_module_record()
    {
        var review = await fixture.InsertReviewAsync();
        var order = await fixture.InsertOrderAsync(review);
        using var client = fixture.CreateClient().SignedInAs("clerk");

        var parent = await StartAsync(client, "CloseReview", order);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(parent)).State);
        var parentRelease = await fixture.Steps.ScalarAsync("SELECT release_id FROM axis.process_instances WHERE id = @id", ("id", parent));
        Assert.True(fixture.Steps.Model.TryGetProcess("reviews/Close", out var close));
        Assert.Equal(
            1L,
            await fixture.Steps.CountAsync(
                """
                SELECT count(*) FROM axis.process_instances
                WHERE parent_instance_id = @parent AND state = 'completed' AND release_id = @release
                  AND process_id = @process AND subject_id = @review
                """,
                ("parent", parent),
                ("release", parentRelease!),
                ("process", close.Id),
                ("review", review)));
        Assert.Equal("closed", await fixture.ReviewStateAsync(review));
    }

    [Fact]
    public async Task A_module_event_raised_by_an_application_process_starts_the_module_process_through_the_trigger()
    {
        var review = await fixture.InsertReviewAsync();
        using var client = fixture.CreateClient().SignedInAs("clerk");

        var flag = await StartAsync(client, "FlagReview", review);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(flag)).State);
        Assert.True(fixture.Steps.Model.TryGetProcess("reviews/Close", out var close));
        var started = await fixture.Steps.ScalarAsync(
            "SELECT id FROM axis.process_instances WHERE process_id = @process AND subject_id = @review",
            ("process", close.Id),
            ("review", review));
        Assert.NotNull(started);
        Assert.Equal("completed", (await fixture.Steps.InstanceAsync((Guid)started)).State);
        Assert.Equal("closed", await fixture.ReviewStateAsync(review));
    }

    [Fact]
    public async Task A_waiting_module_process_finishes_on_its_first_release_after_a_newer_module_version_is_activated()
    {
        var review = await fixture.InsertReviewAsync();
        var instance = await fixture.Steps.StartOnSubjectAsync("reviews/Hold", review, fixture.Steps.FirstReleaseId);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", (await fixture.Steps.InstanceAsync(instance)).State);

        using var modules = new TemporaryFolder();
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules", "reviews", "1.0.0");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var content = await File.ReadAllTextAsync(file, CancellationToken);
            modules.With(Path.Combine("reviews", "1.0.0", relative), content);
            modules.With(
                Path.Combine("reviews", "1.1.0", relative),
                content
                    .Replace("\"version\": \"1.0.0\"", "\"version\": \"1.1.0\"", StringComparison.Ordinal)
                    .Replace("'held'", "'held-v2'", StringComparison.Ordinal));
        }

        var (newer, _) = await fixture.Steps.ActivateAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleProcessApp"),
            [],
            modules: ModuleCatalog.Load(modules.Path).Modules);
        Assert.NotEqual(fixture.Steps.FirstReleaseId, newer);

        await fixture.Steps.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE process_instance_id = @id AND kind = @kind RETURNING 1",
            ("id", instance),
            ("kind", ProcessStarts.TimerWorkItemKind));
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal(
            fixture.Steps.FirstReleaseId,
            await fixture.Steps.ScalarAsync("SELECT release_id FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal("held", await fixture.ReviewStateAsync(review));
    }

    [Fact]
    public async Task A_person_without_the_clerk_role_cannot_start_the_application_process()
    {
        var order = await fixture.InsertOrderAsync(await fixture.InsertReviewAsync());
        using var client = fixture.CreateClient().SignedInAs("outsider");

        using var request = Request(
            HttpMethod.Post,
            "/api/apps/ModuleProcessApp/processes/CloseReview/instances",
            ModuleProcessFixture.Host,
            $$"""{ "subjectId": "{{order:D}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            0L,
            await fixture.Steps.CountAsync("SELECT count(*) FROM axis.process_instances WHERE subject_id = @id", ("id", order)));
    }

    [Fact]
    public async Task A_module_task_assigned_to_a_slot_goes_to_the_people_who_hold_the_mapped_role_who_decide_it()
    {
        var review = await fixture.InsertReviewAsync();
        var instance = await fixture.Steps.StartOnSubjectAsync("reviews/Approve", review, fixture.Steps.FirstReleaseId);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var task = await TaskIdAsync(instance);
        Assert.Equal("waiting", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal(
            "role/manager",
            await fixture.Steps.ScalarAsync("SELECT assignee_kind || '/' || assignee FROM axis.process_tasks WHERE id = @id", ("id", task)));
        using var manager = fixture.CreateClient().SignedInAs("manager");

        Assert.Contains(task, await InboxAsync(manager));
        using (var read = Request(HttpMethod.Get, $"{Tasks}/{task:D}", ModuleProcessFixture.Host))
        using (var response = await manager.SendAsync(read, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var complete = Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", ModuleProcessFixture.Host, """{ "outcome": "approve" }"""))
        using (var response = await manager.SendAsync(complete, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal("approved", await fixture.ReviewStateAsync(review));
    }

    [Theory]
    [InlineData("clerk")]
    [InlineData("elsewhere")]
    [InlineData("outsider")]
    public async Task A_person_without_the_mapped_role_in_the_application_neither_sees_nor_decides_a_module_task(string user)
    {
        var review = await fixture.InsertReviewAsync();
        var instance = await fixture.Steps.StartOnSubjectAsync("reviews/Approve", review, fixture.Steps.FirstReleaseId);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var task = await TaskIdAsync(instance);
        using var client = fixture.CreateClient().SignedInAs(user);

        Assert.DoesNotContain(task, await InboxAsync(client));
        using (var read = Request(HttpMethod.Get, $"{Tasks}/{task:D}", ModuleProcessFixture.Host))
        using (var response = await client.SendAsync(read, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var complete = Request(HttpMethod.Post, $"{Tasks}/{task:D}/complete", ModuleProcessFixture.Host, """{ "outcome": "approve" }"""))
        using (var response = await client.SendAsync(complete, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Null(await fixture.ReviewStateAsync(review));
    }

    [Fact]
    public async Task An_endpoint_creates_a_module_record_and_starts_the_module_process_once_per_idempotency_key()
    {
        var title = $"Imported {Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString("N");

        var (recordId, instanceId) = await ImportAsync("review-importer", title, key);
        var (repeatedRecordId, repeatedInstanceId) = await ImportAsync("review-importer", title, key);

        Assert.Equal((recordId, instanceId), (repeatedRecordId, repeatedInstanceId));
        Assert.Equal(1L, await fixture.CountReviewsAsync(title));
        Assert.True(fixture.Steps.Model.TryGetProcess("reviews/Close", out var close));
        Assert.Equal(
            1L,
            await fixture.Steps.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND subject_id = @record",
                ("process", close.Id),
                ("record", recordId)));
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(instanceId)).State);
        Assert.Equal("closed", await fixture.ReviewStateAsync(recordId));
    }

    [Fact]
    public async Task A_caller_without_the_invoke_grant_cannot_call_the_endpoint_and_no_record_is_written()
    {
        var title = $"Denied {Guid.NewGuid():N}";
        using var client = fixture.CreateClient().CallingAs("outsider-system");

        using var request = Request(
            HttpMethod.Post,
            "/api/apps/ModuleProcessApp/endpoints/ImportReview",
            ModuleProcessFixture.Host,
            $$"""{ "input": { "title": "{{title}}" } }""");
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0L, await fixture.CountReviewsAsync(title));
    }

    /// <summary>Calls the endpoint <c>ImportReview</c> as <paramref name="caller"/> and returns the new record and instance.</summary>
    private async Task<(Guid RecordId, Guid InstanceId)> ImportAsync(string caller, string title, string key)
    {
        using var client = fixture.CreateClient().CallingAs(caller);
        using var request = Request(
            HttpMethod.Post,
            "/api/apps/ModuleProcessApp/endpoints/ImportReview",
            ModuleProcessFixture.Host,
            $$"""{ "input": { "title": "{{title}}" } }""");
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        using var body = JsonDocument.Parse(content);
        return (body.RootElement.GetProperty("recordId").GetGuid(), body.RootElement.GetProperty("instanceId").GetGuid());
    }

    private const string Tasks = "/api/apps/ModuleProcessApp/tasks";

    private async Task<Guid> TaskIdAsync(Guid instance) =>
        (Guid)(await fixture.Steps.ScalarAsync("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id", ("id", instance))
            ?? throw new InvalidOperationException("The instance has no task."));

    private static async Task<IReadOnlyList<Guid>> InboxAsync(HttpClient client)
    {
        using var request = Request(HttpMethod.Get, $"{Tasks}?pageSize=100", ModuleProcessFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    private static async Task<Guid> StartAsync(HttpClient client, string process, Guid subject)
    {
        using var request = Request(
            HttpMethod.Post,
            $"/api/apps/ModuleProcessApp/processes/{process}/instances",
            ModuleProcessFixture.Host,
            $$"""{ "subjectId": "{{subject:D}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
