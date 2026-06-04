using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.FormBuilder;

[Collection("Api")]
public class FormEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    // ── GET /api/forms ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetForms_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/forms");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetForms_WhenOrgHasNoForms_ReturnsEmptyPagedResult()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm1");

        HttpResponseMessage resp = await client.GetAsync("/api/forms");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("items").EnumerateArray().ToList().Should().BeEmpty();
        body.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    // ── POST /api/forms ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateForm_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PostAsJsonAsync("/api/forms",
            new { name = "Test Form" }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateForm_WhenRequestIsValid_Returns201AndAppearsInList()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm2");

        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/forms", new
        {
            name = "Employee Onboarding",
            description = "New hire form",
        }, Json);

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        HttpResponseMessage listResp = await client.GetAsync("/api/forms");
        JsonElement list = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        list.GetProperty("items").EnumerateArray().ToList().Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateForm_WhenNameIsDuplicate_Returns409()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm3");
        await CreateFormAsync(client, "Duplicate Form");

        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/forms",
            new { name = "Duplicate Form", description = (string?)null }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── GET /api/forms/picker ─────────────────────────────────────────────────

    [Fact]
    public async Task GetFormPicker_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/forms/picker");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFormPicker_WhenFormsExist_ReturnsFlatList()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm4");
        await CreateFormAsync(client, "Form A");
        await CreateFormAsync(client, "Form B");

        HttpResponseMessage resp = await client.GetAsync("/api/forms/picker");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement[]? items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(Json);
        items.Should().HaveCount(2);
        items.Should().Contain(i => i.GetProperty("name").GetString() == "Form A");
        items.Should().Contain(i => i.GetProperty("name").GetString() == "Form B");
    }

    // ── GET /api/forms/{formId} ───────────────────────────────────────────────

    [Fact]
    public async Task GetFormById_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync($"/api/forms/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFormById_WhenIdIsUnknown_Returns404()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm5");
        HttpResponseMessage resp = await client.GetAsync($"/api/forms/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFormById_WhenFormExists_ReturnsDetail()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm6");
        string id = await CreateFormAsync(client, "Detail Form", "A description");

        HttpResponseMessage resp = await client.GetAsync($"/api/forms/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement form = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        form.GetProperty("name").GetString().Should().Be("Detail Form");
        form.GetProperty("description").GetString().Should().Be("A description");
        form.GetProperty("fields").GetArrayLength().Should().Be(0);
    }

    // ── PUT /api/forms/{formId} ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateForm_WhenRequestIsValid_Returns204AndChangesArePersisted()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm7");
        string id = await CreateFormAsync(client, "Old Name");

        HttpResponseMessage updateResp = await client.PutAsJsonAsync($"/api/forms/{id}",
            new { name = "New Name", description = "Updated" }, Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement detail = await (await client.GetAsync($"/api/forms/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("name").GetString().Should().Be("New Name");
        detail.GetProperty("description").GetString().Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateForm_WhenFormNotFound_Returns404()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm8");
        HttpResponseMessage resp = await client.PutAsJsonAsync($"/api/forms/{Guid.NewGuid()}",
            new { name = "X", description = (string?)null }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/forms/{formId} ────────────────────────────────────────────

    [Fact]
    public async Task DeleteForm_WhenFormExists_Returns204AndFormIsGone()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm9");
        string id = await CreateFormAsync(client, "To Delete");

        HttpResponseMessage deleteResp = await client.DeleteAsync($"/api/forms/{id}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage getResp = await client.GetAsync($"/api/forms/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteForm_WhenFormNotFound_Returns404()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm10");
        HttpResponseMessage resp = await client.DeleteAsync($"/api/forms/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/forms/{formId}/fields ───────────────────────────────────────

    [Fact]
    public async Task AddField_WhenRequestIsValid_Returns201AndFieldAppearsInForm()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm11");
        string formId = await CreateFormAsync(client, "Field Form");

        HttpResponseMessage addResp = await client.PostAsJsonAsync($"/api/forms/{formId}/fields", new
        {
            key = "first_name",
            label = "First Name",
            type = "Text",
            required = true,
            config = new { max_length = 100, placeholder = (string?)null, multiline = false },
        }, Json);

        addResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await addResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        JsonElement detail = await (await client.GetAsync($"/api/forms/{formId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("fields").GetArrayLength().Should().Be(1);
        JsonElement field = detail.GetProperty("fields").EnumerateArray().First();
        field.GetProperty("key").GetString().Should().Be("first_name");
        field.GetProperty("label").GetString().Should().Be("First Name");
        field.GetProperty("type").GetString().Should().Be("Text");
        field.GetProperty("required").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AddField_WhenFormNotFound_Returns404()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm12");
        HttpResponseMessage resp = await client.PostAsJsonAsync($"/api/forms/{Guid.NewGuid()}/fields", new
        {
            key = "field1",
            label = "Field 1",
            type = "Text",
            required = false,
            config = (object?)null,
        }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddField_WhenKeyIsDuplicate_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm13");
        string formId = await CreateFormAsync(client, "Dup Key Form");
        await AddTextFieldAsync(client, formId, "email", "Email");

        HttpResponseMessage resp = await client.PostAsJsonAsync($"/api/forms/{formId}/fields", new
        {
            key = "email",
            label = "Email Address",
            type = "Text",
            required = false,
            config = (object?)null,
        }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── DELETE /api/forms/{formId}/fields/{fieldId} ───────────────────────────

    [Fact]
    public async Task RemoveField_WhenFieldExists_Returns204AndFieldIsGone()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm14");
        string formId = await CreateFormAsync(client, "Remove Field Form");
        string fieldId = await AddTextFieldAsync(client, formId, "notes", "Notes");

        HttpResponseMessage deleteResp = await client.DeleteAsync($"/api/forms/{formId}/fields/{fieldId}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement detail = await (await client.GetAsync($"/api/forms/{formId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("fields").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task RemoveField_WhenFieldNotFound_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm15");
        string formId = await CreateFormAsync(client, "Remove Field 422");
        HttpResponseMessage resp = await client.DeleteAsync($"/api/forms/{formId}/fields/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── PUT /api/forms/{formId}/fields/reorder ────────────────────────────────

    [Fact]
    public async Task ReorderFields_WhenFieldIdsAreValid_Returns204AndOrderChanges()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm16");
        string formId = await CreateFormAsync(client, "Reorder Form");
        string fieldA = await AddTextFieldAsync(client, formId, "field_a", "Field A");
        string fieldB = await AddTextFieldAsync(client, formId, "field_b", "Field B");

        HttpResponseMessage reorderResp = await client.PutAsJsonAsync($"/api/forms/{formId}/fields/reorder",
            new { field_ids = new[] { fieldB, fieldA } }, Json);

        reorderResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement detail = await (await client.GetAsync($"/api/forms/{formId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        List<JsonElement> fields = detail.GetProperty("fields").EnumerateArray().ToList();
        fields[0].GetProperty("id").GetString().Should().Be(fieldB);
        fields[1].GetProperty("id").GetString().Should().Be(fieldA);
    }

    [Fact]
    public async Task ReorderFields_WhenFieldIdsMissing_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "fm17");
        string formId = await CreateFormAsync(client, "Reorder 422");
        await AddTextFieldAsync(client, formId, "only_field", "Only Field");

        // Send empty list — must contain all field IDs
        HttpResponseMessage resp = await client.PutAsJsonAsync($"/api/forms/{formId}/fields/reorder",
            new { field_ids = Array.Empty<string>() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> CreateFormAsync(HttpClient client, string name, string? description = null)
    {
        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/forms",
            new { name, description }, Json);
        resp.EnsureSuccessStatusCode();
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetString()!;
    }

    private async Task<string> AddTextFieldAsync(HttpClient client, string formId, string key, string label)
    {
        HttpResponseMessage resp = await client.PostAsJsonAsync($"/api/forms/{formId}/fields", new
        {
            key,
            label,
            type = "Text",
            required = false,
            config = (object?)null,
        }, Json);
        resp.EnsureSuccessStatusCode();
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetString()!;
    }
}
