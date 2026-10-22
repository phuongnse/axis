using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>requiredWhen</c> conditions of the ItemRequirements form of the RecordsApp fixture, which
/// a create or update enforces when it names the form in <c>?form=</c>. A closed item needs a
/// price, and a date when its quantity is above zero (the section's condition), and an order time
/// when its quantity is above ten (the field's condition).
/// </summary>
public sealed class RecordFormRequirementEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Form = "?form=ItemRequirements";
    private const string Required = "Required.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_that_leaves_required_fields_empty_is_400_at_each_field_and_stores_nothing()
    {
        await fixture.ResetAsync();

        using var problem = await SendAsync(HttpMethod.Post, $"{Items}{Form}", """{ "values": { "name": "Desk", "quantity": 2, "status": "closed" } }""", HttpStatusCode.BadRequest);

        Assert.Equal([("/values/neededBy", Required), ("/values/price", Required)], Errors(problem));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
    }

    [Fact]
    public async Task Same_create_without_a_form_is_201()
    {
        await fixture.ResetAsync();

        using var created = await SendAsync(HttpMethod.Post, Items, """{ "values": { "name": "Desk", "quantity": 2, "status": "closed" } }""", HttpStatusCode.Created);

        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Equal("Desk", created.RootElement.GetProperty("values").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Field_hidden_by_its_section_does_not_block_a_create()
    {
        await fixture.ResetAsync();

        // The quantity is null, so the dates section is hidden and only the price is required.
        using var refused = await SendAsync(HttpMethod.Post, $"{Items}{Form}", """{ "values": { "name": "Desk", "status": "closed" } }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/price", Required)], Errors(refused));

        using var created = await SendAsync(HttpMethod.Post, $"{Items}{Form}", """{ "values": { "name": "Desk", "status": "closed", "price": 3 } }""", HttpStatusCode.Created);
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
    }

    [Fact]
    public async Task Field_hidden_by_its_own_condition_is_required_only_once_it_shows()
    {
        await fixture.ResetAsync();
        const string Filled = """ "name": "Desk", "status": "closed", "price": 3, "neededBy": "2026-01-02" """;

        using var created = await SendAsync(HttpMethod.Post, $"{Items}{Form}", $$"""{ "values": { {{Filled}}, "quantity": 2 } }""", HttpStatusCode.Created);
        using var refused = await SendAsync(HttpMethod.Post, $"{Items}{Form}", $$"""{ "values": { {{Filled}}, "quantity": 11 } }""", HttpStatusCode.BadRequest);

        Assert.Equal([("/values/orderedAt", Required)], Errors(refused));
    }

    [Fact]
    public async Task Condition_that_does_not_hold_requires_nothing()
    {
        await fixture.ResetAsync();

        using var created = await SendAsync(HttpMethod.Post, $"{Items}{Form}", """{ "values": { "name": "Desk", "quantity": 2, "status": "open" } }""", HttpStatusCode.Created);

        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
    }

    [Fact]
    public async Task Update_is_checked_against_the_stored_record_and_a_refusal_leaves_the_version()
    {
        await fixture.ResetAsync();
        using var created = await SendAsync(HttpMethod.Post, Items, """{ "values": { "name": "Desk", "quantity": 2, "status": "closed" } }""", HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString();

        // The body touches only the quantity, but the stored status makes the price and date required.
        using var refused = await SendAsync(HttpMethod.Patch, $"{Items}/{id}{Form}", """{ "version": 1, "values": { "quantity": 3 } }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/neededBy", Required), ("/values/price", Required)], Errors(refused));
        using var unchanged = await SendAsync(HttpMethod.Get, $"{Items}/{id}", null, HttpStatusCode.OK);
        Assert.Equal(1, unchanged.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("2", unchanged.RootElement.GetProperty("values").GetProperty("quantity").GetRawText());

        using var updated = await SendAsync(
            HttpMethod.Patch, $"{Items}/{id}{Form}", """{ "version": 1, "values": { "price": 4, "neededBy": "2026-01-02" } }""", HttpStatusCode.OK);
        Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());

        // The stored price now satisfies the form, so an unrelated change passes.
        using var again = await SendAsync(HttpMethod.Patch, $"{Items}/{id}{Form}", """{ "version": 2, "values": { "quantity": 5 } }""", HttpStatusCode.OK);
        Assert.Equal(3, again.RootElement.GetProperty("version").GetInt64());
    }

    [Theory]
    [InlineData("?form=Missing")]
    [InlineData("?form=")]
    [InlineData("?form=ItemRequirements&form=ItemSections")]
    public async Task Name_that_is_no_form_of_the_entity_is_400_at_form_before_the_body(string query)
    {
        await fixture.ResetAsync();
        using var created = await SendAsync(HttpMethod.Post, Items, """{ "values": { "name": "Desk" } }""", HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString();

        // A body that is not JSON would be a 415, so the 400 shows that the form is checked first.
        using var create = Request(HttpMethod.Post, $"{Items}{query}", HostA, "name=Desk", "text/plain");
        using var createResponse = await fixture.Client.SendAsync(create, CancellationToken);
        using var createProblem = await ReadProblemAsync(createResponse, HttpStatusCode.BadRequest);
        Assert.Equal([("form", "Must name a form of this entity.")], Errors(createProblem));

        using var update = await SendAsync(HttpMethod.Patch, $"{Items}/{id}{query}", """{ "version": 1, "values": { "name": "Chair" } }""", HttpStatusCode.BadRequest);
        Assert.Equal([("form", "Must name a form of this entity.")], Errors(update));
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
    }

    [Fact]
    public async Task User_without_the_grant_gets_403_for_an_unknown_form()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });
        using var rita = fixture.CreateClient().SignedInAs("rita");

        using var create = Request(HttpMethod.Post, $"{Items}?form=Missing", HostA, """{ "values": { "name": "Chair" } }""");
        using var createResponse = await rita.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        using var update = Request(HttpMethod.Patch, $"{Items}/{id:D}?form=Missing", HostA, """{ "version": 1, "values": { "name": "Table" } }""");
        using var updateResponse = await rita.SendAsync(update, CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? body, HttpStatusCode expected)
    {
        using var request = Request(method, path, HostA, body);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        if (expected is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            Assert.Equal(expected, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }

        return await ReadProblemAsync(response, expected);
    }

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();
}
