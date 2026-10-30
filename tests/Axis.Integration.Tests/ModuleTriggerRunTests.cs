using System.Net;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A trigger of the <c>followups</c> module starts the module's own process in each application that
/// uses the module, on that application's copy. <c>OpenNewFollowups</c> starts <c>Open</c> for a created
/// <c>Followup</c>, <c>NoteFlaggedReviews</c> starts <c>NoteFlagged</c> when the declared <c>reviews</c>
/// module's <c>ReviewFlagged</c> event is raised, and <c>LogDeletedFollowups</c> starts <c>LogDeletion</c>
/// with the title of a deleted <c>Followup</c>. The tests share one database and each uses records of its own.
/// </summary>
public sealed class ModuleTriggerRunTests(ModuleTriggerFixture fixture) : IClassFixture<ModuleTriggerFixture>
{
    private const string App = "ModuleTriggerApp";

    private const string OtherApp = "ModuleTriggerOtherApp";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private ApplicationModel Model => fixture.Steps.Model;

    [Fact]
    public async Task A_created_module_record_starts_the_module_process_once_and_it_completes_on_the_record()
    {
        using var client = fixture.CreateClient().SignedInAs("clerk");

        var followup = await CreateFollowupAsync(client, App, $"Created {Guid.NewGuid():N}");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(1L, await fixture.InstanceCountAsync(Model, "followups/Open", followup));
        Assert.Equal(
            1L,
            await fixture.Steps.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE subject_id = @subject AND state = 'completed'",
                ("subject", followup)));
        Assert.Equal("open", await fixture.StateAsync(Model, "followups/Followup", followup));
    }

    [Fact]
    public async Task A_module_event_of_a_declared_module_starts_the_module_process_on_the_record()
    {
        using var client = fixture.CreateClient().SignedInAs("clerk");
        var review = await fixture.InsertReviewAsync(Model);

        var flag = await StartFlagAsync(client, App, review);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", (await fixture.Steps.InstanceAsync(flag)).State);
        Assert.Equal(1L, await fixture.InstanceCountAsync(Model, "followups/NoteFlagged", review));
        Assert.Equal(
            1L,
            await fixture.Steps.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE subject_id = @subject AND state = 'completed' AND process_id = @process",
                ("subject", review),
                ("process", ProcessId(Model, "followups/NoteFlagged"))));
        Assert.Equal("noted", await fixture.StateAsync(Model, "reviews/Review", review));
    }

    [Fact]
    public async Task A_deleted_module_record_starts_the_subject_less_module_process_with_parameters_from_the_record()
    {
        using var client = fixture.CreateClient().SignedInAs("clerk");
        var title = $"Deleted {Guid.NewGuid():N}";
        var followup = await CreateFollowupAsync(client, App, title);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        using var request = Request(HttpMethod.Delete, $"/api/apps/{App}/entities/followups/Followup/records/{followup:D}", ModuleTriggerFixture.Host);
        using var deleted = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(
            1L,
            await fixture.Steps.CountAsync(
                """
                SELECT count(*) FROM axis.process_instances
                WHERE process_id = @process AND application_id = @app AND subject_id IS NULL AND parameters ->> 'title' = @title
                """,
                ("process", ProcessId(Model, "followups/LogDeletion")),
                ("app", AppIdOf(Model)),
                ("title", title)));
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal(1L, await fixture.CountByTitleAsync(Model, "followups/FollowupDeletion", title));
    }

    [Fact]
    public async Task Two_applications_start_separate_instances_of_their_own_copy_of_the_process()
    {
        using var client = fixture.CreateClient().SignedInAs("clerk");

        var first = await CreateFollowupAsync(client, App, $"First {Guid.NewGuid():N}");
        var second = await CreateFollowupAsync(client, OtherApp, $"Second {Guid.NewGuid():N}");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.NotEqual(ProcessId(Model, "followups/Open"), ProcessId(fixture.OtherModel, "followups/Open"));
        Assert.Equal(1L, await fixture.InstanceCountAsync(Model, "followups/Open", first));
        Assert.Equal(0L, await fixture.InstanceCountAsync(Model, "followups/Open", second));
        Assert.Equal(1L, await fixture.InstanceCountAsync(fixture.OtherModel, "followups/Open", second));
        Assert.Equal(0L, await fixture.InstanceCountAsync(fixture.OtherModel, "followups/Open", first));
        Assert.Equal("open", await fixture.StateAsync(Model, "followups/Followup", first));
        Assert.Equal("open", await fixture.StateAsync(fixture.OtherModel, "followups/Followup", second));
    }

    [Fact]
    public async Task A_module_event_raised_in_one_application_starts_nothing_in_the_other()
    {
        using var client = fixture.CreateClient().SignedInAs("clerk");
        var review = await fixture.InsertReviewAsync(Model);
        var otherReview = await fixture.InsertReviewAsync(fixture.OtherModel);
        var before = await fixture.InstanceCountAsync(fixture.OtherModel, "followups/NoteFlagged");

        await StartFlagAsync(client, App, review);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(1L, await fixture.InstanceCountAsync(Model, "followups/NoteFlagged", review));
        Assert.Equal(before, await fixture.InstanceCountAsync(fixture.OtherModel, "followups/NoteFlagged"));
        Assert.Equal("noted", await fixture.StateAsync(Model, "reviews/Review", review));
        Assert.Null(await fixture.StateAsync(fixture.OtherModel, "reviews/Review", otherReview));
    }

    [Fact]
    public async Task A_person_without_a_create_grant_gets_forbidden_on_the_module_record_and_no_instance_starts()
    {
        using var client = fixture.CreateClient().SignedInAs("outsider");
        var title = $"Refused {Guid.NewGuid():N}";
        var before = await fixture.InstanceCountAsync(Model, "followups/Open");

        using var request = Request(
            HttpMethod.Post,
            $"/api/apps/{App}/entities/followups/Followup/records",
            ModuleTriggerFixture.Host,
            $$"""{ "values": { "title": "{{title}}" } }""");
        using var response = await client.SendAsync(request, CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0L, await fixture.CountByTitleAsync(Model, "followups/Followup", title));
        Assert.Equal(before, await fixture.InstanceCountAsync(Model, "followups/Open"));
    }

    private static Guid AppIdOf(ApplicationModel model) => model.Manifest.Id;

    private static Guid ProcessId(ApplicationModel model, string process)
    {
        Assert.True(model.TryGetProcess(process, out var found));
        return found.Id;
    }

    private static async Task<Guid> CreateFollowupAsync(HttpClient client, string app, string title)
    {
        using var request = Request(
            HttpMethod.Post,
            $"/api/apps/{app}/entities/followups/Followup/records",
            ModuleTriggerFixture.Host,
            $$"""{ "values": { "title": "{{title}}" } }""");
        using var response = await client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> StartFlagAsync(HttpClient client, string app, Guid review)
    {
        using var request = Request(
            HttpMethod.Post,
            $"/api/apps/{app}/processes/FlagReview/instances",
            ModuleTriggerFixture.Host,
            $$"""{ "subjectId": "{{review:D}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
