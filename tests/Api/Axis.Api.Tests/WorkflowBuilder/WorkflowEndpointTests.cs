using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.WorkflowBuilder;

[Collection("Api")]
public class WorkflowEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    // ── GET /api/workflows ────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkflows_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/workflows");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflows_WhenOrgHasNoWorkflows_ReturnsEmptyList()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf1");

        HttpResponseMessage resp = await client.GetAsync("/api/workflows");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("items").EnumerateArray().ToList().Should().BeEmpty();
    }

    // ── POST /api/workflows ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkflow_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PostAsJsonAsync("/api/workflows",
            new { name = "My Workflow" }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateWorkflow_WhenRequestIsValid_Returns201AndAppearsInList()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf2");

        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/workflows", new
        {
            name = "Onboarding Flow",
            description = "New hire onboarding",
        }, Json);

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        HttpResponseMessage listResp = await client.GetAsync("/api/workflows");
        JsonElement list = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        list.GetProperty("items").EnumerateArray().ToList().Should().HaveCount(1);
    }

    // ── GET /api/workflows/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task GetWorkflow_WhenIdIsUnknown_Returns404()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf3");
        HttpResponseMessage resp = await client.GetAsync($"/api/workflows/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWorkflow_WhenWorkflowExists_ReturnsDetailWithStartAndEndNodes()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf4");

        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/workflows",
            new { name = "Approval Flow", description = (string?)null }, Json);
        string id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        HttpResponseMessage resp = await client.GetAsync($"/api/workflows/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement wf = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        wf.GetProperty("name").GetString().Should().Be("Approval Flow");
        wf.GetProperty("status").GetString().Should().Be("Draft");
        // New workflows start with Start + End nodes
        wf.GetProperty("steps").GetArrayLength().Should().Be(2);
    }

    // ── PUT /api/workflows/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkflow_WhenRequestIsValid_ReturnsNoContent()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf5");

        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/workflows",
            new { name = "Old Name", description = (string?)null }, Json);
        string id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        HttpResponseMessage updateResp = await client.PutAsJsonAsync($"/api/workflows/{id}",
            new { name = "New Name", description = "Updated description" }, Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("name").GetString().Should().Be("New Name");
    }

    // ── POST /api/workflows/{id}/publish ─────────────────────────────────────

    [Fact]
    public async Task PublishWorkflow_WhenWorkflowHasNoTrigger_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf6");

        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/workflows",
            new { name = "Untriggered", description = (string?)null }, Json);
        string id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        HttpResponseMessage publishResp = await client.PostAsync($"/api/workflows/{id}/publish", null);

        publishResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── POST /api/workflows/{id}/archive + unarchive ──────────────────────────

    [Fact]
    public async Task ArchiveWorkflow_WhenWorkflowIsActive_Returns204()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf7");

        string id = await CreateAndPublishWorkflowAsync(client, "To Archive");

        HttpResponseMessage resp = await client.PostAsync($"/api/workflows/{id}/archive", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("status").GetString().Should().Be("Archived");
    }

    [Fact]
    public async Task ArchiveWorkflow_WhenWorkflowIsDraft_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf7b");

        string id = await CreateWorkflowAsync(client, "Draft Flow");

        HttpResponseMessage resp = await client.PostAsync($"/api/workflows/{id}/archive", null);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UnarchiveWorkflow_WhenWorkflowIsArchived_Returns204()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf8");

        string id = await CreateAndPublishWorkflowAsync(client, "To Unarchive");
        await client.PostAsync($"/api/workflows/{id}/archive", null);

        HttpResponseMessage resp = await client.PostAsync($"/api/workflows/{id}/unarchive", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("status").GetString().Should().Be("Active");
    }

    // ── DELETE /api/workflows/{id} ────────────────────────────────────────────

    [Fact]
    public async Task DeleteWorkflow_WhenWorkflowIsDraft_Returns204()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf7c");

        string id = await CreateWorkflowAsync(client, "Draft to Delete");

        HttpResponseMessage resp = await client.DeleteAsync($"/api/workflows/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage getResp = await client.GetAsync($"/api/workflows/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWorkflow_WhenWorkflowIsActive_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf7d");

        string id = await CreateAndPublishWorkflowAsync(client, "Active Flow");

        HttpResponseMessage resp = await client.DeleteAsync($"/api/workflows/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── POST /api/workflows/{id}/duplicate ───────────────────────────────────

    [Fact]
    public async Task DuplicateWorkflow_WhenWorkflowExists_Returns201WithNewId()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf9");

        string id = await CreateWorkflowAsync(client, "Original");

        HttpResponseMessage dupResp = await client.PostAsync($"/api/workflows/{id}/duplicate", null);
        dupResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement dupBody = await dupResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string newId = dupBody.GetProperty("id").GetString()!;
        newId.Should().NotBe(id);

        JsonElement detail = await (await client.GetAsync($"/api/workflows/{newId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("name").GetString().Should().Be("Copy of Original");
    }

    // ── Steps ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddStep_WhenRequestIsValid_Returns201AndStepAppearsInWorkflow()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf10");

        string id = await CreateWorkflowAsync(client, "Step Flow");

        HttpResponseMessage addResp = await client.PostAsJsonAsync($"/api/workflows/{id}/steps", new
        {
            name = "Send Form",
            stepType = "Form",
            config = (object?)null,
        }, Json);

        addResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement stepBody = await addResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        stepBody.GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        JsonElement detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        // Start + End + new Form step
        detail.GetProperty("steps").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task RemoveStep_WhenStepExists_Returns204AndStepDisappears()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf11");

        string id = await CreateWorkflowAsync(client, "Remove Step Flow");
        HttpResponseMessage addResp = await client.PostAsJsonAsync($"/api/workflows/{id}/steps",
            new { name = "Temp Step", stepType = "Script", config = (object?)null }, Json);
        string stepId = (await addResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        HttpResponseMessage removeResp = await client.DeleteAsync($"/api/workflows/{id}/steps/{stepId}");
        removeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("steps").GetArrayLength().Should().Be(2);
    }

    // ── Transitions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTransition_WhenStepsExist_Returns204()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf12");

        string id = await CreateWorkflowAsync(client, "Transition Flow");
        JsonElement detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        List<JsonElement> steps = detail.GetProperty("steps").EnumerateArray().ToList();
        string startId = steps.First(s => s.GetProperty("name").GetString() == "Start").GetProperty("id").GetString()!;
        string endId = steps.First(s => s.GetProperty("name").GetString() == "End").GetProperty("id").GetString()!;

        HttpResponseMessage resp = await client.PostAsJsonAsync($"/api/workflows/{id}/transitions",
            new { fromStepId = startId, toStepId = endId, label = (string?)null }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Triggers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTrigger_WhenRequestIsValid_Returns204()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf13");

        string id = await CreateWorkflowAsync(client, "Triggered Flow");

        HttpResponseMessage resp = await client.PostAsJsonAsync($"/api/workflows/{id}/triggers",
            new { triggerType = "Manual", config = (object?)null }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveTrigger_WhenTriggerExists_Returns204()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf14");

        string id = await CreateWorkflowAsync(client, "Remove Trigger Flow");
        await client.PostAsJsonAsync($"/api/workflows/{id}/triggers",
            new { triggerType = "Manual", config = (object?)null }, Json);

        HttpResponseMessage resp = await client.DeleteAsync($"/api/workflows/{id}/triggers/Manual");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Export / Import ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportWorkflow_WhenWorkflowExists_ReturnsJsonFile()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf15");

        string id = await CreateWorkflowAsync(client, "Export Me");

        HttpResponseMessage resp = await client.GetAsync($"/api/workflows/{id}/export");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        resp.Content.Headers.ContentDisposition!.FileName.Should().Contain("export-me");

        string json = await resp.Content.ReadAsStringAsync();
        JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Export Me");
    }

    [Fact]
    public async Task ImportWorkflow_WhenExportDataIsValid_Returns201()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf16");

        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/workflows/import", new
        {
            name = "Imported Flow",
            description = (string?)null,
            steps = Array.Empty<object>(),
            transitions = Array.Empty<object>(),
            triggers = Array.Empty<object>(),
        }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task BulkExportWorkflows_WhenWorkflowsExist_ReturnsZipFile()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "wf17");

        await CreateWorkflowAsync(client, "Bulk A");
        await CreateWorkflowAsync(client, "Bulk B");

        HttpResponseMessage resp = await client.GetAsync("/api/workflows/export-all");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<string> CreateWorkflowAsync(HttpClient client, string name)
    {
        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/workflows",
            new { name, description = (string?)null }, Json);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetString()!;
    }

    private async Task<string> CreateAndPublishWorkflowAsync(HttpClient client, string name)
    {
        string id = await CreateWorkflowAsync(client, name);

        await client.PostAsJsonAsync($"/api/workflows/{id}/triggers",
            new { triggerType = "Manual", config = (object?)null }, Json);

        JsonElement detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        List<JsonElement> steps = detail.GetProperty("steps").EnumerateArray().ToList();
        string startId = steps.First(s => s.GetProperty("type").GetString() == "Start").GetProperty("id").GetString()!;
        string endId = steps.First(s => s.GetProperty("type").GetString() == "End").GetProperty("id").GetString()!;

        HttpResponseMessage stepResp = await client.PostAsJsonAsync($"/api/workflows/{id}/steps",
            new { name = "Task", stepType = "Form", config = (object?)null }, Json);
        string stepId = (await stepResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/workflows/{id}/transitions",
            new { fromStepId = startId, toStepId = stepId, label = (string?)null }, Json);
        await client.PostAsJsonAsync($"/api/workflows/{id}/transitions",
            new { fromStepId = stepId, toStepId = endId, label = (string?)null }, Json);

        await client.PostAsync($"/api/workflows/{id}/publish", null);

        return id;
    }
}
