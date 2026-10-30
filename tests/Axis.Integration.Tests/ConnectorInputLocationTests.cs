using System.Text.Json.Nodes;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// Where a connector call sends each input of its operation: in the path, the query, a header or the
/// JSON body, as the input's <c>in</c> says.
/// </summary>
public sealed class ConnectorInputLocationTests(ConnectorCallFixture fixture) : IClassFixture<ConnectorCallFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_get_sends_its_path_input_escaped_as_one_segment_its_query_input_and_its_header_input_and_no_body()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken, "FindShipment");
        var callId = await CallIdAsync(tenant, instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal("GET", request.Method);
        Assert.Equal("/tenant-a/shipments/a%2Fb%20c?state=open%20now", request.RawTarget);
        Assert.Equal("web", request.Headers!["Channel"]);
        Assert.Equal("", request.Body);
        Assert.Equal(ConnectorCalls.Succeeded, await CallStateAsync(tenant, callId));
    }

    [Fact]
    public async Task A_post_sends_its_path_input_in_the_path_and_only_its_body_inputs_in_the_json_body()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken, "ShipWarehouseOrder");
        var callId = await CallIdAsync(tenant, instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal(("POST", "/tenant-a/warehouses/north/orders"), (request.Method, request.RawTarget));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["reference"] = $"Request {subjectId:N}", ["amount"] = 500m },
            JsonNode.Parse(request.Body)));
        Assert.Equal(ConnectorCalls.Succeeded, await CallStateAsync(tenant, callId));
    }

    private static async Task<Guid> CallIdAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    private static async Task<string> CallStateAsync(ProcessStepFixture tenant, Guid callId) =>
        (string)(await tenant.ScalarAsync("SELECT state FROM axis.connector_calls WHERE id = @id", ("id", callId)))!;
}
