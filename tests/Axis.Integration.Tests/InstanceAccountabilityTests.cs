using System.Globalization;
using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A process instance keeps its accountable user and its deciding task. The start endpoint stores its
/// user and no task, and each task completion replaces both with the completing user and the task.
/// </summary>
public sealed class InstanceAccountabilityTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_started_instance_holds_its_starter_and_a_task_completion_replaces_both_values_with_the_completing_user_and_the_task()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager = fixture.CreateClient().SignedInAs("manager-2");
        var subject = await CreateAsync(requester, 10000);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var starter = await fixture.ScalarAsync(
            "SELECT actor FROM axis.audit_records WHERE process_instance_id = @instance AND action = 'process.started'",
            ("instance", instance));
        Assert.Equal("requester", starter);
        Assert.Equal((starter, (Guid?)null), await AccountabilityAsync(instance));

        var task = (Guid)(await fixture.ScalarAsync(
            "SELECT id FROM axis.process_tasks WHERE process_instance_id = @instance AND state = 'open' AND step = 'managerApproval'",
            ("instance", instance)))!;
        using var response = await CompleteAsync(manager, task, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The completion writes both values in its own transaction, before the worker runs the next step.
        Assert.Equal(("manager-2", (Guid?)task), await AccountabilityAsync(instance));
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal(("manager-2", (Guid?)task), await AccountabilityAsync(instance));
    }

    private async Task<(string? AccountableUser, Guid? DecisionTask)> AccountabilityAsync(Guid instance)
    {
        var user = await fixture.ScalarAsync("SELECT accountable_user FROM axis.process_instances WHERE id = @id", ("id", instance));
        var task = await fixture.ScalarAsync("SELECT decision_task FROM axis.process_instances WHERE id = @id", ("id", instance));
        return (user as string, task as Guid?);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, decimal unitPrice)
    {
        using var request = RecordApiFixture.Request(
            HttpMethod.Post,
            $"{App}/entities/PurchaseRequest/records",
            Host,
            $$"""
            {
              "values": {
                "title": "Request {{Guid.NewGuid():N}}",
                "status": "draft",
                "department": "{{Operations:D}}",
                "lineItems": [{ "description": "Desk", "quantity": 1, "unitPrice": {{unitPrice.ToString(CultureInfo.InvariantCulture)}} }]
              }
            }
            """);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> StartAsync(HttpClient client, Guid subject)
    {
        using var request = RecordApiFixture.Request(
            HttpMethod.Post,
            $"{App}/processes/PurchaseRequestApproval/instances",
            Host,
            $$"""{ "subjectId": "{{subject:D}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
