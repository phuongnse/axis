using System.Text.Json.Nodes;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

public sealed class ModuleConnectorCallTests(ModuleConnectorCallFixture fixture) : IClassFixture<ModuleConnectorCallFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Module_process_of_each_application_calls_the_connector_its_slot_maps_to_with_that_connectors_auth_and_writes_the_output()
    {
        var (orderA, instanceA) = await fixture.StageAsync("a", CancellationToken);
        var (orderB, instanceB) = await fixture.StageAsync("b", CancellationToken);
        var callA = await CallIdAsync("a", instanceA);
        var callB = await CallIdAsync("b", instanceB);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("b", CancellationToken));

        // Application a maps the slot to PurchasingA, which sends the identity in a header.
        var requestA = Assert.Single(fixture.Stub.RequestsFor(callA));
        Assert.Equal(
            ("POST", "/tenant-a/purchase-orders", $"Bearer {fixture.Token("a")}", callA.ToString("D")),
            (requestA.Method, requestA.Path, requestA.Authorization, requestA.IdempotencyKey));
        Assert.Equal($"Order {orderA:N}", Assert.IsType<JsonObject>(JsonNode.Parse(requestA.Body))["title"]!.GetValue<string>());

        // Application b maps it to PurchasingB, which has another path, no header and its own base URL and token.
        var requestB = Assert.Single(fixture.Stub.RequestsFor(callB));
        Assert.Equal(
            ("POST", $"/tenant-b/orders/{callB:D}", $"Bearer {fixture.Token("b")}", ""),
            (requestB.Method, requestB.Path, requestB.Authorization, requestB.IdempotencyKey));
        Assert.Equal($"Order {orderB:N}", Assert.IsType<JsonObject>(JsonNode.Parse(requestB.Body))["title"]!.GetValue<string>());

        Assert.Equal("PO-1", await fixture.OrderNumberAsync("a", orderA));
        Assert.Equal("PO-1", await fixture.OrderNumberAsync("b", orderB));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await fixture.Tenant("a").InstanceAsync(instanceA));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await fixture.Tenant("b").InstanceAsync(instanceB));
    }

    [Fact]
    public async Task Dropped_connection_of_a_module_call_is_reconciled_through_the_mapped_connectors_lookup()
    {
        var (orderId, instanceId) = await fixture.StageAsync("b", CancellationToken);
        var callId = await CallIdAsync("b", instanceId);
        fixture.Stub.DropNext();

        // PurchasingB declares no idempotency header, so the unknown outcome goes to its lookup at once.
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("b", CancellationToken));
        Assert.Equal(ConnectorCalls.Unknown, await CallStateAsync("b", callId));
        Assert.Null(await fixture.OrderNumberAsync("b", orderId));

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("b", CancellationToken));

        var create = Assert.Single(fixture.Stub.CreateRequestsFor(callId));
        Assert.Equal(("POST", $"/tenant-b/orders/{callId:D}"), (create.Method, create.Path));
        var lookup = Assert.Single(fixture.Stub.RequestsFor(callId), request => request.Method == "GET");
        Assert.Equal(
            ($"/tenant-b/orders/by-key/{callId:D}", $"Bearer {fixture.Token("b")}", ""),
            (lookup.Path, lookup.Authorization, lookup.Body));
        Assert.Equal(ConnectorCalls.Succeeded, await CallStateAsync("b", callId));
        Assert.Equal("PO-1", await fixture.OrderNumberAsync("b", orderId));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await fixture.Tenant("b").InstanceAsync(instanceId));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("b", CancellationToken));
        Assert.Equal((ProcessStarts.Completed, 4L, "done", true), await fixture.Tenant("b").InstanceAsync(instanceId));
    }

    private async Task<Guid> CallIdAsync(string tenantId, Guid instanceId) =>
        (Guid)(await fixture.Tenant(tenantId).ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    private async Task<string> CallStateAsync(string tenantId, Guid callId) =>
        (string)(await fixture.Tenant(tenantId).ScalarAsync("SELECT state FROM axis.connector_calls WHERE id = @id", ("id", callId)))!;
}
