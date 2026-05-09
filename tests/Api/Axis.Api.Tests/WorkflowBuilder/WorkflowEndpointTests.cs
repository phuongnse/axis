using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Endpoints;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.WorkflowBuilder;

[Collection("Api")]
public class WorkflowEndpointTests(ApiTestFixture fixture)
{
    // GET /api/workflows
    [Fact]
    public async Task GetWorkflows_without_token_returns_401()
    {
        var resp = await fixture.Client.GetAsync("/api/workflows");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflows_returns_empty_list_for_new_org()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf_get_empty");

        var resp = await client.GetAsync("/api/workflows");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await resp.Content.ReadFromJsonAsync<List<object>>(ApiTestFixture.JsonOptions);
        workflows.Should().BeEmpty();
    }

    // POST /api/workflows
    [Fact]
    public async Task CreateWorkflow_without_token_returns_401()
    {
        var request = new CreateWorkflowRequest("Test Workflow", null);
        var resp = await fixture.Client.PostAsJsonAsync("/api/workflows", request, ApiTestFixture.JsonOptions);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateWorkflow_returns_201_and_id_for_valid_request()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf_create");

        var request = new CreateWorkflowRequest("Employee Onboarding", "Onboarding process");
        var resp = await client.PostAsJsonAsync("/api/workflows", request, ApiTestFixture.JsonOptions);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ApiTestFixture.JsonOptions);
        body.TryGetProperty("id", out var idProp).Should().BeTrue();
        idProp.GetGuid().Should().NotBeEmpty();

        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().Contain($"/api/workflows/{idProp.GetGuid()}");
    }

    [Fact]
    public async Task CreateWorkflow_with_duplicate_name_returns_422()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf_dup");

        var request = new CreateWorkflowRequest("Duplicate Name", null);

        var resp1 = await client.PostAsJsonAsync("/api/workflows", request, ApiTestFixture.JsonOptions);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        var resp2 = await client.PostAsJsonAsync("/api/workflows", request, ApiTestFixture.JsonOptions);
        resp2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await resp2.Content.ReadFromJsonAsync<JsonElement>(ApiTestFixture.JsonOptions);
        body.GetProperty("message").GetString().Should().Contain("already exists");
    }

    // POST /api/workflows/{id}/publish
    [Fact]
    public async Task PublishWorkflow_without_token_returns_401()
    {
        var resp = await fixture.Client.PostAsync($"/api/workflows/{Guid.NewGuid()}/publish", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublishWorkflow_fails_for_nonexistent_workflow()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf_pub_notfound");

        var resp = await client.PostAsync($"/api/workflows/{Guid.NewGuid()}/publish", null);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ApiTestFixture.JsonOptions);
        body.GetProperty("message").GetString().Should().Contain("Workflow not found");
    }
}
