using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The creator field of the RecordsApp fixture: a ticket's <c>requester</c> is set to the id of the
/// signed-in user who creates it, and no body can set it.
/// </summary>
public sealed class RecordCreatorEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Tickets = "/api/apps/RecordsApp/entities/Ticket/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_sets_the_creator_field_to_the_signed_in_user_or_null_without_a_session()
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var annas = await PostAsync(anna, """{ "title": "Anna's" }""", HttpStatusCode.Created);
        Assert.Equal("anna", Requester(annas));
        using var annasRead = await GetJsonAsync(Id(annas));
        Assert.Equal("anna", Requester(annasRead));

        using var anonymous = await PostAsync(fixture.Client, """{ "title": "Nobody's" }""", HttpStatusCode.Created);
        Assert.Null(Requester(anonymous));
        using var anonymousRead = await GetJsonAsync(Id(anonymous));
        Assert.Null(Requester(anonymousRead));
    }

    [Fact]
    public async Task Body_that_sets_the_creator_field_is_400_at_that_field_and_writes_nothing()
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var create = await PostAsync(anna, """{ "title": "Set", "requester": "x" }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/requester", "Cannot be set.")], Errors(create));
        using var createNull = await PostAsync(anna, """{ "title": "Set", "requester": null }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/requester", "Cannot be set.")], Errors(createNull));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Ticket")).RowCount);

        using var created = await PostAsync(anna, """{ "title": "Kept" }""", HttpStatusCode.Created);
        var id = Id(created);

        using var update = await PatchAsync(anna, id, """{ "version": 1, "values": { "requester": "binh" } }""");
        Assert.Equal([("/values/requester", "Cannot be set.")], Errors(update));
        using var updateNull = await PatchAsync(anna, id, """{ "version": 1, "values": { "requester": null } }""");
        Assert.Equal([("/values/requester", "Cannot be set.")], Errors(updateNull));

        using var read = await GetJsonAsync(id);
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("anna", Requester(read));
    }

    private static string Id(JsonDocument record) => record.RootElement.GetProperty("id").GetString()!;

    private static string? Requester(JsonDocument record) =>
        record.RootElement.GetProperty("values").GetProperty("requester").GetString();

    private static async Task<JsonDocument> PostAsync(HttpClient client, string values, HttpStatusCode expected)
    {
        using var request = Request(HttpMethod.Post, Tickets, HostA, $$"""{ "values": {{values}} }""");
        using var response = await client.SendAsync(request, CancellationToken);
        if (expected != HttpStatusCode.Created)
        {
            return await ReadProblemAsync(response, expected);
        }

        Assert.Equal(expected, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> PatchAsync(HttpClient client, string id, string body)
    {
        using var request = Request(HttpMethod.Patch, $"{Tickets}/{id}", HostA, body);
        using var response = await client.SendAsync(request, CancellationToken);
        return await ReadProblemAsync(response, HttpStatusCode.BadRequest);
    }

    private async Task<JsonDocument> GetJsonAsync(string id)
    {
        using var request = Request($"{Tickets}/{id}", HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();
}
