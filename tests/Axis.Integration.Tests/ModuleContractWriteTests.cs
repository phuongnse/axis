using System.Net;
using System.Text.Json;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>requests</c> module holds a contract with a lifecycle on its <c>Request</c> entity: a request is
/// created as a draft, a draft goes to submitted when its amount is above zero, a submitted request goes to
/// approved by a guard that divides by the amount, and an approved one goes to archived without a guard. Only a
/// draft and an archived request can be deleted. The record API refuses every other write with a 409 and
/// writes nothing.
/// </summary>
public sealed class ModuleContractWriteTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string Records = "/api/apps/ModuleContractApp/entities/requests/Request/records";
    private const string Title = "The record's state does not allow this change.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_in_a_state_that_is_not_initial_is_refused_and_writes_nothing()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var title = $"Request {Guid.NewGuid():N}";

        using var response = await SendAsync(client, HttpMethod.Post, Records, $$"""{ "values": { "title": "{{title}}", "status": "submitted" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        AssertRefusal(problem, "A record cannot be created in the state 'submitted'.");
        Assert.DoesNotContain(await ListTitlesAsync(client), listed => listed == title);
    }

    [Fact]
    public async Task A_create_in_an_initial_state_is_written()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        var (_, version) = await CreateAsync(client, "draft", amount: 0);

        Assert.Equal(1, version);
    }

    [Fact]
    public async Task An_update_along_no_transition_is_refused_with_a_message_naming_both_states()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client, "draft", amount: 5);

        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "approved" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        AssertRefusal(problem, "No transition goes from 'draft' to 'approved'.");
        await AssertUnchangedAsync(client, id, version, "draft");
    }

    [Fact]
    public async Task An_update_whose_guard_is_false_is_refused_with_the_transitions_message()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client, "draft", amount: 0);

        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "submitted" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        AssertRefusal(problem, "requests.request.amountRequired");
        await AssertUnchangedAsync(client, id, version, "draft");
    }

    [Fact]
    public async Task An_update_whose_guard_is_true_is_written()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client, "draft", amount: 5);

        var next = await PatchAsync(client, id, version, "submitted");

        Assert.Equal(version + 1, next);
        await AssertUnchangedAsync(client, id, next, "submitted");
    }

    [Fact]
    public async Task A_guard_that_stops_with_a_run_time_error_refuses_the_write()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client, "draft", amount: 5);
        version = await PatchAsync(client, id, version, "submitted");
        version = await PatchAsync(client, id, version, null, """{ "amount": 0 }""");

        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { "status": "approved" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        AssertRefusal(problem, "The transition 'approve' does not allow this change.");
        await AssertUnchangedAsync(client, id, version, "submitted");
    }

    [Fact]
    public async Task A_transition_without_a_guard_is_written()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client, "draft", amount: 5);
        version = await PatchAsync(client, id, version, "submitted");
        version = await PatchAsync(client, id, version, "approved");

        version = await PatchAsync(client, id, version, "archived");

        await AssertUnchangedAsync(client, id, version, "archived");
    }

    [Fact]
    public async Task An_update_that_keeps_the_state_needs_no_transition()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client, "draft", amount: 5);
        version = await PatchAsync(client, id, version, "submitted");

        var next = await PatchAsync(client, id, version, "submitted", """{ "title": "Renamed" }""");

        Assert.Equal(version + 1, next);
        await AssertUnchangedAsync(client, id, next, "submitted");
    }

    [Fact]
    public async Task A_delete_in_a_state_that_is_not_deletable_is_refused_and_a_delete_in_a_deletable_state_is_written()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (submitted, version) = await CreateAsync(client, "draft", amount: 5);
        version = await PatchAsync(client, submitted, version, "submitted");
        var (draft, _) = await CreateAsync(client, "draft", amount: 5);

        using (var refused = await SendAsync(client, HttpMethod.Delete, $"{Records}/{submitted}"))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(refused, HttpStatusCode.Conflict);
            AssertRefusal(problem, "A record cannot be deleted in the state 'submitted'.");
        }

        await AssertUnchangedAsync(client, submitted, version, "submitted");
        using (var deleted = await SendAsync(client, HttpMethod.Delete, $"{Records}/{draft}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using var gone = await SendAsync(client, HttpMethod.Get, $"{Records}/{draft}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task A_person_without_the_mapped_role_is_denied_before_the_lifecycle_is_checked()
    {
        using var client = fixture.CreateClient().SignedInAs("manager");

        using var response = await SendAsync(client, HttpMethod.Post, Records, """{ "values": { "title": "Denied", "status": "submitted" } }""");

        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
    }

    private static void AssertRefusal(JsonDocument problem, string message)
    {
        Assert.Equal(Title, problem.RootElement.GetProperty("title").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        var error = Assert.Single(errors.EnumerateObject());
        Assert.Equal("/values/status", error.Name);
        Assert.Equal([message], error.Value.EnumerateArray().Select(text => text.GetString()));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null)
    {
        using var request = RecordApiFixture.Request(method, path, ModuleAppFixture.Host, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<(Guid Id, long Version)> CreateAsync(HttpClient client, string status, int amount)
    {
        using var response = await SendAsync(
            client, HttpMethod.Post, Records, $$"""{ "values": { "title": "Request {{Guid.NewGuid():N}}", "amount": {{amount}}, "status": "{{status}}" } }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64());
    }

    // Updates the record and returns its new version. The status is left out when it is null.
    private static async Task<long> PatchAsync(HttpClient client, Guid id, long version, string? status, string? more = null)
    {
        var values = new List<string>();
        if (more is not null)
        {
            values.Add(more.Trim().TrimStart('{').TrimEnd('}').Trim());
        }

        if (status is not null)
        {
            values.Add($"\"status\": \"{status}\"");
        }

        using var response = await SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": { {{string.Join(", ", values)}} } }""");
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

    private static async Task<List<string?>> ListTitlesAsync(HttpClient client)
    {
        using var response = await SendAsync(client, HttpMethod.Get, Records);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("values").GetProperty("title").GetString()).ToList();
    }
}
