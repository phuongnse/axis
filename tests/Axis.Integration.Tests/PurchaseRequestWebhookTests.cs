using System.Net;
using System.Text.Json;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An approved purchase request delivers its <c>PurchaseRequestApproved</c> event, signed, to the purchasing system stub.
/// A receiver that keeps failing ends the delivery in an open dead letter of the approval process, and the request still
/// gets its purchase order.
/// </summary>
public sealed class PurchaseRequestWebhookTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    /// <summary>The most rounds the failing delivery gets: its attempt limit plus a margin.</summary>
    private const int MaxRounds = 7;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_approved_request_below_the_threshold_delivers_one_signed_PurchaseRequestApproved_webhook_to_the_purchasing_system()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        var request = await CreateRequestAsync(requester, 500m);
        var (number, _) = await NumbersAsync(requester, request);
        await StartAsync(requester, request);
        await fixture.RunUntilIdleAsync(CancellationToken);

        await ApproveAsync(manager2, request);
        await fixture.RunUntilIdleAsync(CancellationToken);

        // The stub answers 401 to a bad signature, so a listed delivery proves the signature.
        var delivery = Assert.Single(await DeliveriesAsync(number));
        Assert.Equal("PurchaseRequestApproved", delivery.GetProperty("type").GetString());
        Assert.Equal("delivered", await fixture.ScalarAsync(
            "SELECT d.state FROM axis.webhook_deliveries d JOIN axis.events e ON e.id = d.event_id WHERE e.subject_id = @subject",
            ("subject", request)));
    }

    [Fact]
    public async Task A_receiver_that_keeps_failing_leaves_an_open_dead_letter_of_the_approval_and_the_request_still_gets_its_order()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        var request = await CreateRequestAsync(requester, 500m);
        var (number, _) = await NumbersAsync(requester, request);
        using (var purchasing = new HttpClient { BaseAddress = new Uri(fixture.PurchasingUrl) })
        {
            using var fail = await purchasing.PostAsync($"/test/fail-webhook/{number}", null, CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, fail.StatusCode);
        }

        var instance = await StartAsync(requester, request);
        await fixture.RunUntilIdleAsync(CancellationToken);
        await ApproveAsync(manager2, request);
        await fixture.RunUntilIdleAsync(CancellationToken);

        await RunDeliveriesUntilNoItemAsync();

        var processId = (Guid)(await fixture.ScalarAsync("SELECT process_id FROM axis.process_instances WHERE id = @instance", ("instance", instance)))!;
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.dead_letters WHERE kind = 'webhook.deliver' AND state = 'open' AND process_instance_id = @instance AND process_id = @process",
            ("instance", instance),
            ("process", processId)));
        Assert.Empty(await DeliveriesAsync(number));
        Assert.Single(await fixture.OrdersAsync(number));
    }

    /// <summary>Makes the rescheduled delivery items due and runs them, round after round, until none is left.</summary>
    private async Task RunDeliveriesUntilNoItemAsync()
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            await fixture.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE kind = 'webhook.deliver'");
            await fixture.RunKindsUntilIdleAsync(CancellationToken, ProcessStarts.WebhookDeliverWorkItemKind);
            if (await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver'") == 0)
            {
                return;
            }
        }

        Assert.Fail("A webhook delivery item was still queued after every attempt.");
    }

    /// <summary>The deliveries that the purchasing system stub holds for the request <paramref name="requestNumber"/>, as the stub lists them.</summary>
    private async Task<IReadOnlyList<JsonElement>> DeliveriesAsync(string requestNumber)
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.PurchasingUrl) };
        using var body = JsonDocument.Parse(await client.GetStringAsync("/webhooks/purchase-request-approved", CancellationToken));
        return [.. body.RootElement.EnumerateArray()
            .Where(delivery => delivery.GetProperty("requestNumber").GetString() == requestNumber)
            .Select(delivery => delivery.Clone())];
    }

    /// <summary>Completes the open task of <paramref name="subject"/> with the approve outcome.</summary>
    private static async Task ApproveAsync(HttpClient client, Guid subject)
    {
        Guid task;
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{App}/tasks?pageSize=100", Host);
        using (var list = await client.SendAsync(request, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync(CancellationToken));
            task = Assert.Single(
                body.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("subject").GetProperty("id").GetGuid() == subject).GetProperty("id").GetGuid();
        }

        using var completed = await CompleteAsync(client, task, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }
}
