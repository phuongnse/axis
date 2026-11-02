using System.Net;
using System.Text.Json;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// ModuleContractApp's layers add the <c>onHold</c> state to the <c>requests</c> module's <c>Request</c> lifecycle, with a
/// <c>hold</c> transition from <c>submitted</c> guarded by <c>amount &lt; 1000</c> and a <c>resume</c> transition back. The
/// record API enforces them like the module's own transitions, and no transition leaves <c>onHold</c> for the module's
/// protected <c>approved</c> state.
/// </summary>
public sealed class ModuleLifecycleLayerTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string Records = "/api/apps/ModuleContractApp/entities/requests/Request/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_record_moves_from_submitted_to_on_hold_and_back_under_the_added_transitions()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateSubmittedAsync(client, amount: 500);

        version = await PatchAsync(client, id, version, "onHold");
        await AssertUnchangedAsync(client, id, version, "onHold");
        version = await PatchAsync(client, id, version, "submitted");

        await AssertUnchangedAsync(client, id, version, "submitted");
    }

    [Fact]
    public async Task A_hold_whose_guard_is_false_is_refused_and_writes_nothing()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateSubmittedAsync(client, amount: 5000);

        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "onHold" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        AssertRefusal(problem, "The transition 'hold' does not allow this change.");
        await AssertUnchangedAsync(client, id, version, "submitted");
    }

    [Fact]
    public async Task An_update_from_on_hold_straight_to_the_protected_approved_state_is_refused_and_writes_nothing()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateSubmittedAsync(client, amount: 500);
        version = await PatchAsync(client, id, version, "onHold");

        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "approved" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        AssertRefusal(problem, "No transition goes from 'onHold' to 'approved'.");
        await AssertUnchangedAsync(client, id, version, "onHold");
    }

    [Fact]
    public async Task A_record_cannot_be_created_in_the_added_state()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var response = await SendAsync(client, HttpMethod.Post, Records, """{ "values": { "title": "Held", "amount": 5, "status": "onHold" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        AssertRefusal(problem, "A record cannot be created in the state 'onHold'.");
    }

    private static void AssertRefusal(JsonDocument problem, string message)
    {
        Assert.Equal("The record's state does not allow this change.", problem.RootElement.GetProperty("title").GetString());
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/status", error.Name);
        Assert.Equal([message], error.Value.EnumerateArray().Select(text => text.GetString()));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null)
    {
        using var request = RecordApiFixture.Request(method, path, ModuleAppFixture.Host, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<(Guid Id, long Version)> CreateSubmittedAsync(HttpClient client, int amount)
    {
        using var response = await SendAsync(
            client, HttpMethod.Post, Records, $$"""{ "values": { "title": "Request {{Guid.NewGuid():N}}", "amount": {{amount}}, "status": "draft" } }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var id = body.RootElement.GetProperty("id").GetGuid();
        return (id, await PatchAsync(client, id, body.RootElement.GetProperty("version").GetInt64(), "submitted"));
    }

    // Updates the record's state and returns its new version.
    private static async Task<long> PatchAsync(HttpClient client, Guid id, long version, string status)
    {
        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "{{status}}" } }""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("version").GetInt64();
    }

    private static async Task AssertUnchangedAsync(HttpClient client, Guid id, long version, string status)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Records}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(version, body.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(status, body.RootElement.GetProperty("values").GetProperty("status").GetString());
    }
}
