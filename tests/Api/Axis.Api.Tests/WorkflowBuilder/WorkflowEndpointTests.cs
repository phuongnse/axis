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
        var resp = await fixture.Client.GetAsync("/api/workflows");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflows_WhenOrgHasNoWorkflows_ReturnsEmptyList()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf1");

        var resp = await client.GetAsync("/api/workflows");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("items").EnumerateArray().ToList().Should().BeEmpty();
    }

    // ── POST /api/workflows ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkflow_WhenNoToken_Returns401()
    {
        var resp = await fixture.Client.PostAsJsonAsync("/api/workflows",
            new { name = "My Workflow" }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateWorkflow_WhenRequestIsValid_Returns201AndAppearsInList()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf2");

        var createResp = await client.PostAsJsonAsync("/api/workflows", new
        {
            name = "Onboarding Flow",
            description = "New hire onboarding",
        }, Json);

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        var listResp = await client.GetAsync("/api/workflows");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        list.GetProperty("items").EnumerateArray().ToList().Should().HaveCount(1);
    }

    // ── GET /api/workflows/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task GetWorkflow_WhenIdIsUnknown_Returns404()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf3");
        var resp = await client.GetAsync($"/api/workflows/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWorkflow_WhenWorkflowExists_ReturnsDetailWithStartAndEndNodes()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf4");

        var createResp = await client.PostAsJsonAsync("/api/workflows",
            new { name = "Approval Flow", description = (string?)null }, Json);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var resp = await client.GetAsync($"/api/workflows/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var wf = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        wf.GetProperty("name").GetString().Should().Be("Approval Flow");
        wf.GetProperty("status").GetString().Should().Be("Draft");
        // New workflows start with Start + End nodes
        wf.GetProperty("steps").GetArrayLength().Should().Be(2);
    }

    // ── PUT /api/workflows/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkflow_WhenRequestIsValid_ReturnsNoContent()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf5");

        var createResp = await client.PostAsJsonAsync("/api/workflows",
            new { name = "Old Name", description = (string?)null }, Json);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var updateResp = await client.PutAsJsonAsync($"/api/workflows/{id}",
            new { name = "New Name", description = "Updated description" }, Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("name").GetString().Should().Be("New Name");
    }

    // ── POST /api/workflows/{id}/publish ─────────────────────────────────────

    [Fact]
    public async Task PublishWorkflow_WhenWorkflowHasNoTrigger_Returns422()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf6");

        var createResp = await client.PostAsJsonAsync("/api/workflows",
            new { name = "Untriggered", description = (string?)null }, Json);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var publishResp = await client.PostAsync($"/api/workflows/{id}/publish", null);

        publishResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── POST /api/workflows/{id}/archive + unarchive ──────────────────────────

    [Fact]
    public async Task ArchiveWorkflow_WhenWorkflowExists_Returns204()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf7");

        var id = await CreateWorkflowAsync(client, "To Archive");

        var resp = await client.PostAsync($"/api/workflows/{id}/archive", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("status").GetString().Should().Be("Archived");
    }

    [Fact]
    public async Task UnarchiveWorkflow_WhenWorkflowIsArchived_Returns204()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf8");

        var id = await CreateWorkflowAsync(client, "To Unarchive");
        await client.PostAsync($"/api/workflows/{id}/archive", null);

        var resp = await client.PostAsync($"/api/workflows/{id}/unarchive", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("status").GetString().Should().Be("Active");
    }

    // ── POST /api/workflows/{id}/duplicate ───────────────────────────────────

    [Fact]
    public async Task DuplicateWorkflow_WhenWorkflowExists_Returns201WithNewId()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf9");

        var id = await CreateWorkflowAsync(client, "Original");

        var dupResp = await client.PostAsync($"/api/workflows/{id}/duplicate", null);
        dupResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dupBody = await dupResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var newId = dupBody.GetProperty("id").GetString()!;
        newId.Should().NotBe(id);

        var detail = await (await client.GetAsync($"/api/workflows/{newId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("name").GetString().Should().Be("Copy of Original");
    }

    // ── Steps ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddStep_WhenRequestIsValid_Returns201AndStepAppearsInWorkflow()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf10");

        var id = await CreateWorkflowAsync(client, "Step Flow");

        var addResp = await client.PostAsJsonAsync($"/api/workflows/{id}/steps", new
        {
            name = "Send Form",
            step_type = "Form",
            config = (object?)null,
        }, Json);

        addResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var stepBody = await addResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        stepBody.GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        var detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        // Start + End + new Form step
        detail.GetProperty("steps").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task RemoveStep_WhenStepExists_Returns204AndStepDisappears()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf11");

        var id = await CreateWorkflowAsync(client, "Remove Step Flow");
        var addResp = await client.PostAsJsonAsync($"/api/workflows/{id}/steps",
            new { name = "Temp Step", step_type = "Script", config = (object?)null }, Json);
        var stepId = (await addResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var removeResp = await client.DeleteAsync($"/api/workflows/{id}/steps/{stepId}");
        removeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("steps").GetArrayLength().Should().Be(2);
    }

    // ── Transitions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTransition_WhenStepsExist_Returns204()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf12");

        var id = await CreateWorkflowAsync(client, "Transition Flow");
        var detail = await (await client.GetAsync($"/api/workflows/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        var steps = detail.GetProperty("steps").EnumerateArray().ToList();
        var startId = steps.First(s => s.GetProperty("name").GetString() == "Start").GetProperty("id").GetString()!;
        var endId = steps.First(s => s.GetProperty("name").GetString() == "End").GetProperty("id").GetString()!;

        var resp = await client.PostAsJsonAsync($"/api/workflows/{id}/transitions",
            new { from_step_id = startId, to_step_id = endId, label = (string?)null }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Triggers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTrigger_WhenRequestIsValid_Returns204()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf13");

        var id = await CreateWorkflowAsync(client, "Triggered Flow");

        var resp = await client.PostAsJsonAsync($"/api/workflows/{id}/triggers",
            new { trigger_type = "Manual", config = (object?)null }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveTrigger_WhenTriggerExists_Returns204()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf14");

        var id = await CreateWorkflowAsync(client, "Remove Trigger Flow");
        await client.PostAsJsonAsync($"/api/workflows/{id}/triggers",
            new { trigger_type = "Manual", config = (object?)null }, Json);

        var resp = await client.DeleteAsync($"/api/workflows/{id}/triggers/Manual");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Export / Import ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportWorkflow_WhenWorkflowExists_ReturnsJsonFile()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf15");

        var id = await CreateWorkflowAsync(client, "Export Me");

        var resp = await client.GetAsync($"/api/workflows/{id}/export");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        resp.Content.Headers.ContentDisposition!.FileName.Should().Contain("export-me");

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Export Me");
    }

    [Fact]
    public async Task ImportWorkflow_WhenExportDataIsValid_Returns201()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf16");

        var exportPayload = new
        {
            name = "Imported Flow",
            description = (string?)null,
            steps = Array.Empty<object>(),
            transitions = Array.Empty<object>(),
            triggers = Array.Empty<object>(),
        };

        var resp = await client.PostAsJsonAsync("/api/workflows/import", exportPayload, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task BulkExportWorkflows_WhenWorkflowsExist_ReturnsZipFile()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "wf17");

        await CreateWorkflowAsync(client, "Bulk A");
        await CreateWorkflowAsync(client, "Bulk B");

        var resp = await client.GetAsync("/api/workflows/export-all");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<string> CreateWorkflowAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/workflows",
            new { name, description = (string?)null }, Json);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetString()!;
    }
}
