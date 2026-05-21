using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.WorkflowEngine;

[Collection("Api")]
public class ExecutionEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetAllExecutions_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/executions");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllExecutions_WhenAuthenticated_ReturnsEmptyPagedList()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "exec1");
        HttpResponseMessage resp = await client.GetAsync("/api/executions");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task StartExecution_WhenWorkflowNotActive_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "exec2");

        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/workflows", new
        {
            name = "Draft Flow",
        }, Json);
        createResp.EnsureSuccessStatusCode();
        JsonElement wf = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid workflowId = wf.GetProperty("id").GetGuid();

        HttpResponseMessage startResp = await client.PostAsJsonAsync(
            $"/api/workflows/{workflowId}/executions",
            new { input = new Dictionary<string, object?>() },
            Json);

        startResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
