using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The form state endpoint on the <c>ItemSections</c> form of the RecordsApp fixture. Its
/// <c>pricing</c> section and its <c>price</c> field are visible when the quantity is above 0, and
/// <c>price</c> is required when the quantity is above 10. An item's quantity is positive. <c>fia</c>
/// reads only an item's <c>name</c> and <c>department</c>, <c>fern</c> reads only items with a status,
/// and <c>manager</c> holds no grant.
/// </summary>
public sealed class FormStateEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string State = "/api/apps/RecordsApp/forms/ItemSections/state";
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Draft_with_no_quantity_hides_the_pricing_section_and_price()
    {
        using var state = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "name": "Desk", "quantity": 0 }, "touched": [] }""");

        var root = state.RootElement;
        Assert.Equal(
            [("main", true), ("pricing", false)],
            root.GetProperty("sections").EnumerateArray().Select(section => (section.GetProperty("name").GetString()!, section.GetProperty("visible").GetBoolean())));
        Assert.Equal(["name", "quantity", "price", "total"], root.GetProperty("fields").EnumerateObject().Select(field => field.Name));
        Assert.False(root.GetProperty("fields").GetProperty("price").GetProperty("visible").GetBoolean());
        Assert.False(root.GetProperty("fields").GetProperty("total").GetProperty("visible").GetBoolean());
        Assert.True(root.GetProperty("fields").GetProperty("name").GetProperty("visible").GetBoolean());
        Assert.True(root.GetProperty("fields").GetProperty("quantity").GetProperty("readOnly").GetBoolean());
        Assert.True(root.GetProperty("fields").GetProperty("total").GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task Touched_empty_price_is_required_when_the_quantity_is_high()
    {
        using var state = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "name": "Desk", "quantity": 20, "price": null }, "touched": ["price"] }""");

        var price = state.RootElement.GetProperty("fields").GetProperty("price");
        Assert.True(price.GetProperty("visible").GetBoolean());
        Assert.True(price.GetProperty("required").GetBoolean());
        Assert.Equal(["Required."], Messages(state, "/values/price"));
        Assert.Single(state.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task Validation_message_comes_only_for_a_touched_field()
    {
        using var touched = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "name": "Desk", "quantity": -1 }, "touched": ["quantity"] }""");
        using var untouched = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "name": "Desk", "quantity": -1 }, "touched": ["name"] }""");

        Assert.Equal(["item.quantityPositive"], Messages(touched, "/values/quantity"));
        Assert.Empty(untouched.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task Bad_value_is_a_message_in_the_answer_and_the_other_fields_still_get_their_states()
    {
        using var state = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "name": "Desk", "quantity": 20, "price": "cheap" }, "touched": ["price"] }""");

        Assert.Single(Messages(state, "/values/price"));
        Assert.True(state.RootElement.GetProperty("fields").GetProperty("price").GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Draft_of_a_stored_record_is_laid_over_it()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "20" });

        using var kept = await PostAsync(fixture.Client, $$"""{ "recordId": "{{id:D}}", "values": {}, "touched": ["price"] }""");
        using var changed = await PostAsync(fixture.Client, $$"""{ "recordId": "{{id:D}}", "values": { "quantity": 2 }, "touched": ["price"] }""");

        Assert.True(kept.RootElement.GetProperty("fields").GetProperty("price").GetProperty("required").GetBoolean());
        Assert.Equal(["Required."], Messages(kept, "/values/price"));
        Assert.False(changed.RootElement.GetProperty("fields").GetProperty("price").GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Fielded_user_gets_only_the_readable_fields_and_the_sections_that_hold_them()
    {
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using var state = await PostAsync(fia, """{ "recordId": null, "values": { "name": "Desk", "quantity": -1 }, "touched": ["name", "quantity"] }""");

        Assert.Equal(["name"], state.RootElement.GetProperty("fields").EnumerateObject().Select(field => field.Name));
        Assert.Equal(["main"], state.RootElement.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        Assert.Empty(state.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task User_without_a_grant_is_403()
    {
        using var manager = fixture.CreateClient().SignedInAs("manager");

        using var response = await SendAsync(manager, """{ "recordId": null, "values": {}, "touched": [] }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Reader_without_create_or_update_is_403_even_for_a_malformed_body()
    {
        using var rita = fixture.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(rita, "[]");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Record_outside_the_read_filters_is_404()
    {
        await fixture.ResetAsync();
        var hidden = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });
        var shown = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Lamp", ["status"] = "open" });
        using var fern = fixture.CreateClient().SignedInAs("fern");

        using var refused = await SendAsync(fern, $$"""{ "recordId": "{{hidden:D}}", "values": {}, "touched": [] }""");
        using var allowed = await SendAsync(fern, $$"""{ "recordId": "{{shown:D}}", "values": {}, "touched": [] }""");
        using var missing = await SendAsync(fern, $$"""{ "recordId": "{{Guid.NewGuid():D}}", "values": {}, "touched": [] }""");

        using var problem = await ReadProblemAsync(refused, HttpStatusCode.NotFound);
        Assert.Equal("No record has this id.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var missingProblem = await ReadProblemAsync(missing, HttpStatusCode.NotFound);
        Assert.Equal("No record has this id.", missingProblem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Request_that_is_refused_before_the_body_says_why()
    {
        using var signedOut = fixture.CreateClient();
        using var unauthorized = await SendAsync(signedOut, "{}");
        using var wrongType = Request(HttpMethod.Post, State, HostA, """{ "recordId": null, "values": {}, "touched": [] }""", "text/plain");
        using var unsupported = await fixture.Client.SendAsync(wrongType, CancellationToken);
        using var noForm = Request(HttpMethod.Post, "/api/apps/RecordsApp/forms/Nothing/state", HostA, "{}");
        using var unknownForm = await fixture.Client.SendAsync(noForm, CancellationToken);
        using var noApp = Request(HttpMethod.Post, "/api/apps/Nothing/forms/ItemSections/state", HostA, "{}");
        using var unknownApp = await fixture.Client.SendAsync(noApp, CancellationToken);

        using var signIn = await ReadProblemAsync(unauthorized, HttpStatusCode.Unauthorized);
        using var type = await ReadProblemAsync(unsupported, HttpStatusCode.UnsupportedMediaType);
        using var form = await ReadProblemAsync(unknownForm, HttpStatusCode.NotFound);
        using var app = await ReadProblemAsync(unknownApp, HttpStatusCode.NotFound);
        Assert.Equal("The application has no form with this name.", form.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("[]", "")]
    [InlineData("""{ "recordId": null, "touched": [] }""", "/values")]
    [InlineData("""{ "recordId": null, "values": {} }""", "/touched")]
    [InlineData("""{ "recordId": "nope", "values": {}, "touched": [] }""", "/recordId")]
    [InlineData("""{ "recordId": null, "values": [], "touched": [] }""", "/values")]
    [InlineData("""{ "recordId": null, "values": {}, "touched": [1] }""", "/touched/0")]
    [InlineData("""{ "recordId": null, "values": {}, "touched": [], "extra": true }""", "/extra")]
    public async Task Malformed_body_is_400_at_its_pointer(string body, string key)
    {
        using var response = await SendAsync(fixture.Client, body);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(key, out _), problem.RootElement.GetRawText());
    }

    [Fact]
    public async Task Body_without_a_record_id_is_a_new_record()
    {
        using var state = await PostAsync(fixture.Client, """{ "values": { "name": "Desk" }, "touched": [] }""");

        Assert.True(state.RootElement.GetProperty("fields").GetProperty("name").GetProperty("visible").GetBoolean());
    }

    [Fact]
    public async Task Answer_writes_nothing()
    {
        await fixture.ResetAsync();
        using var created = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk", "quantity": 2 } }""");
        using var createdResponse = await fixture.Client.SendAsync(created, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var id = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("id").GetGuid();
        var versionBefore = await VersionAsync(id);
        var auditBefore = (await fixture.AuditRecordsAsync(TenantA, id)).Count;
        var totalAuditBefore = await fixture.AuditRecordCountAsync(TenantA);

        using var state = await PostAsync(fixture.Client, $$"""{ "recordId": "{{id:D}}", "values": { "name": "Table", "quantity": 30 }, "touched": ["price"] }""");

        Assert.Equal(["Required."], Messages(state, "/values/price"));
        Assert.Equal(versionBefore, await VersionAsync(id));
        Assert.Equal(auditBefore, (await fixture.AuditRecordsAsync(TenantA, id)).Count);
        Assert.Equal(totalAuditBefore, await fixture.AuditRecordCountAsync(TenantA));
        using var read = Request(HttpMethod.Get, $"{Items}/{id:D}", HostA);
        using var readResponse = await fixture.Client.SendAsync(read, CancellationToken);
        using var stored = JsonDocument.Parse(await readResponse.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("Desk", stored.RootElement.GetProperty("values").GetProperty("name").GetString());
    }

    private async Task<long> VersionAsync(Guid id)
    {
        using var request = Request(HttpMethod.Get, $"{Items}/{id:D}", HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        using var record = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return record.RootElement.GetProperty("version").GetInt64();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string body)
    {
        using var request = Request(HttpMethod.Post, State, HostA, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> PostAsync(HttpClient client, string body)
    {
        using var response = await SendAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<string> Messages(JsonDocument state, string key) =>
        [.. state.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Select(message => message.GetString()!)];
}
