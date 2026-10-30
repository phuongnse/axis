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
