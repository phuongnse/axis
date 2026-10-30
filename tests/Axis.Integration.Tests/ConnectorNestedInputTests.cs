using System.Text.Json.Nodes;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// A <c>callConnector</c> step builds the nested JSON body of its <c>object</c> and <c>list</c> inputs
/// from the subject and its stored child rows when it stages the call, so a retry sends the same body.
/// </summary>
public sealed class ConnectorNestedInputTests(ConnectorCallFixture fixture) : IClassFixture<ConnectorCallFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_list_input_sends_one_object_for_each_child_row_in_position_order_beside_the_object_input()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertShipmentAsync(("A-1", 3), ("B-2", 1), ("C-3", 7));
        var (instanceId, callId) = await StageAsync(subjectId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal(("POST", "/tenant-a/line-shipments"), (request.Method, request.Path));
        Assert.True(JsonNode.DeepEquals(
            Body(subjectId, ("A-1", 3), ("B-2", 1), ("C-3", 7)),
            JsonNode.Parse(request.Body)));
        Assert.Equal(ProcessStarts.Running, (await tenant.InstanceAsync(instanceId)).State);
    }

    [Fact]
    public async Task An_empty_child_collection_sends_an_empty_list()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertShipmentAsync();
        var (_, callId) = await StageAsync(subjectId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.True(JsonNode.DeepEquals(
            Body(subjectId),
            JsonNode.Parse(request.Body)));
        Assert.Empty(JsonNode.Parse(request.Body)!["lines"]!.AsArray());
    }

    [Fact]
    public async Task A_timed_out_call_is_resent_with_the_same_nested_body_and_the_same_idempotency_key()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertShipmentAsync(("A-1", 3), ("B-2", 1));
        var (_, callId) = await StageAsync(subjectId);

        var held = fixture.Stub.Hold();
        try
        {
            // LineWarehouse times out after PT1S, while the stub holds its answer.
            Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));
        }
        finally
        {
            held.Release();
        }

        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var requests = fixture.Stub.RequestsWithKey(callId);
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].IdempotencyKey, requests[1].IdempotencyKey);
        Assert.Equal(callId.ToString("D"), requests[1].IdempotencyKey);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(requests[0].Body), JsonNode.Parse(requests[1].Body)));
        Assert.Equal(2, JsonNode.Parse(requests[1].Body)!["lines"]!.AsArray().Count);
    }

    [Fact]
    public async Task A_required_nested_field_that_gives_null_fails_the_instance_with_its_path_and_stages_no_call()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertShipmentAsync(("A-1", 3), (null, 1));
        var instanceId = await tenant.StartOnSubjectAsync("ShipLines", subjectId, tenant.FirstReleaseId);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunStepAsync("a", CancellationToken));

        Assert.Equal((ProcessStarts.Failed, 2L, "shipLines", true), await tenant.InstanceAsync(instanceId));
        var row = Assert.Single(await tenant.HistoryAsync(instanceId));
        Assert.Equal("The required input 'lines/1/sku' of the step 'shipLines' gave no value.", row.Error);
        Assert.Equal(0, await tenant.CountAsync("SELECT count(*) FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceId)));
        Assert.Equal(0, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instanceId)));
    }

    private async Task<(Guid InstanceId, Guid CallId)> StageAsync(Guid subjectId)
    {
        var tenant = fixture.Tenant("a");
        var instanceId = await tenant.StartOnSubjectAsync("ShipLines", subjectId, tenant.FirstReleaseId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(instanceId)).State);
        var callId = (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceId)))!;
        return (instanceId, callId);
    }

    private static JsonObject Body(Guid subjectId, params (string Sku, long Quantity)[] lines)
    {
        var items = new JsonArray();
        foreach (var (sku, quantity) in lines)
        {
            items.Add(new JsonObject { ["sku"] = sku, ["quantity"] = quantity });
        }

        return new JsonObject { ["customer"] = new JsonObject { ["name"] = $"Shipment {subjectId:N}" }, ["lines"] = items };
    }
}
