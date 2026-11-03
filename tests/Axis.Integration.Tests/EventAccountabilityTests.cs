using System.Net;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An event keeps the accountable user of whatever raised it, and an event trigger starts its instance
/// with it. <c>FlagAfterApproval</c> asks <c>maria</c>, who holds the role <c>finance</c>, to approve,
/// then raises <c>RequestFlagged</c>. A <c>sync</c> trigger starts <c>RoleApprove</c> and an <c>async</c>
/// trigger starts <c>Review</c>. A trigger-started instance has no deciding task.
/// </summary>
public sealed class EventAccountabilityTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_event_raised_after_a_task_completion_starts_its_triggered_instances_with_the_completing_user()
    {
        var subject = await fixture.InsertRequestAsync(TenantA, null, divisor: 2, amount: 10m);
        var instance = await fixture.StartAsync(TenantA, "FlagAfterApproval", subject);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        Assert.True(await fixture.RunNextStepAsync(TenantA, CancellationToken), "No process step is due.");
        var task = (Guid)(await fixture.ScalarAsync(TenantA, "SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;
        using (var request = RecordApiFixture.Request(
            HttpMethod.Post, $"/api/apps/StepApp/tasks/{task:D}/complete", Host(TenantA), """{ "outcome": "approve" }"""))
        {
            using var response = await maria.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal("completed", await fixture.ScalarAsync(TenantA, "SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal(
            "maria",
            await fixture.ScalarAsync(TenantA, "SELECT accountable_user FROM axis.events WHERE subject_id = @subject AND event = 'RequestFlagged'", ("subject", subject)));
        foreach (var process in new[] { "RoleApprove", "Review" })
        {
            Assert.True(fixture.Model.TryGetProcess(process, out var model));
            var started = await fixture.ScalarAsync(
                TenantA,
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND subject_id = @subject AND accountable_user = 'maria' AND decision_task IS NULL",
                ("process", model.Id),
                ("subject", subject));
            Assert.Equal(1L, started);
        }
    }
}
