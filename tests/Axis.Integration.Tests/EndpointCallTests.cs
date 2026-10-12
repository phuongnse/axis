using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.EndpointCallFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The endpoint call: a caller with the <c>invoke</c> grant creates a note through
/// <c>ImportNote</c> and starts <c>Close</c> on it, in one transaction. Every request goes
/// without the CSRF header, as another system sends it.
/// </summary>
public sealed class EndpointCallTests(EndpointCallFixture fixture) : IClassFixture<EndpointCallFixture>
{
    private const string Call = "/api/apps/EndpointApp/endpoints/ImportNote";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_caller_with_the_invoke_grant_creates_the_record_and_starts_its_process()
    {
        using var response = await CallAsync(
            """
            { "input": { "title": "Desk", "quantity": 2, "status": "open", "lines": [{ "text": "Legs", "hours": 3 }, { "text": "Top" }] } }
            """,
            path: "/api/apps/endpointapp/endpoints/IMPORTNOTE");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(["recordId", "instanceId", "state"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("running", root.GetProperty("state").GetString());
        var recordId = root.GetProperty("recordId").GetGuid();
        var instanceId = root.GetProperty("instanceId").GetGuid();
        Assert.Equal(recordId.ToString("D"), root.GetProperty("recordId").GetString());

        // The creator field holds the caller's id, as a record API create holds the user's.
        Assert.Equal(1L, await fixture.CountAsync(
            $"""
            SELECT count(*) FROM {fixture.NoteTable}
            WHERE {Column("id")} = @id AND {Column("title")} = 'Desk' AND {Column("quantity")} = 2
              AND {Column("status")} = 'open' AND {Column("createdBy")} = 'importer-system'
            """,
            ("id", recordId)));
        Assert.Equal(2L, await fixture.CountAsync(
            $"SELECT count(*) FROM {fixture.NoteLineTable} WHERE {EntityNaming.Quote(EntityNaming.OwnerColumn)} = @id",
            ("id", recordId)));

        Assert.True(fixture.Model.TryGetProcess("Close", out var process));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE id = @instance AND subject_id = @record AND release_id = @release AND process_id = @process
              AND state = 'running' AND revision = 1 AND step = 'done'
            """,
            ("instance", instanceId), ("record", recordId), ("release", fixture.ReleaseId), ("process", process.Id)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @instance AND tenant_id = 'a' AND kind = 'process.step'",
            ("instance", instanceId)));
    }

    [Fact]
    public async Task The_create_and_the_start_are_audited_with_the_caller_as_the_actor_and_the_endpoint_in_their_details()
    {
        using var response = await CallAsync("""{ "input": { "title": "Chair", "status": "open" } }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        var instanceId = json.RootElement.GetProperty("instanceId").GetGuid();
        Assert.True(fixture.Model.TryGetEntity("Note", out var note));
        Assert.True(fixture.Model.TryGetProcess("Close", out var process));

        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records
            WHERE action = 'record.created' AND record_id = @record AND entity_id = @entity AND process_instance_id IS NULL
              AND actor = 'importer-system' AND actor_name = 'Importer System'
              AND details = jsonb_build_object('version', 1, 'endpoint', 'ImportNote')
            """,
            ("record", recordId), ("entity", note.Id)));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records
            WHERE action = 'process.started' AND record_id = @record AND process_instance_id = @instance
              AND actor = 'importer-system' AND actor_name = 'Importer System'
              AND details = jsonb_build_object('processId', @process::text, 'releaseId', @release::text, 'endpoint', 'ImportNote')
            """,
            ("record", recordId), ("instance", instanceId), ("process", process.Id), ("release", fixture.ReleaseId)));
        Assert.Equal(2L, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE record_id = @record", ("record", recordId)));
    }

    [Fact]
    public async Task A_caller_without_an_invoke_grant_gets_a_403_before_the_body_and_nothing_is_written()
    {
        var before = await CountWritesAsync();

        // The reader may read notes, but no entity grant lets a caller call the endpoint.
        using var valid = await CallAsync("""{ "input": { "title": "Desk", "status": "open" } }""", caller: "reader-system");
        using var malformed = await CallAsync("not json", caller: "reader-system");
        using var notJson = await CallAsync("""{ "input": { "title": "Desk", "status": "open" } }""", caller: "reader-system", contentType: "text/plain");

        await AssertProblemAsync(valid, HttpStatusCode.Forbidden, "You have no grant for this action.");
        await AssertProblemAsync(malformed, HttpStatusCode.Forbidden, "You have no grant for this action.");
        await AssertProblemAsync(notJson, HttpStatusCode.Forbidden, "You have no grant for this action.");
        Assert.Equal(before, await CountWritesAsync());
    }

    [Fact]
    public async Task A_caller_with_the_grant_gets_a_415_for_a_body_that_is_not_json()
    {
        using var response = await CallAsync("""{ "input": { "title": "Desk", "status": "open" } }""", contentType: "text/plain");

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "The request body must be application/json.");
    }

    [Theory]
    [InlineData("""{ "input": { "title": "Desk", "quantity": "5", "status": "open" } }""", "/input/quantity", "Must be an integral number within the signed 64-bit range.")]
    [InlineData("""{ "input": { "quantity": 5, "status": "open" } }""", "/input/title", "Required.")]
    [InlineData("""{ "input": { "title": "Desk", "status": "open", "lines": [{ "hours": 1 }] } }""", "/input/lines/0/text", "Required.")]
    [InlineData("""{ "input": { "title": "Desk", "status": "open", "createdBy": "someone" } }""", "/input/createdBy", "Unknown property.")]
    [InlineData("""{ "input": { "title": "Desk", "status": "open" }, "values": {} }""", "/values", "Unknown property.")]
    [InlineData("""{ "values": { "title": "Desk", "status": "open" } }""", "/input", "Required.")]
    [InlineData("""{ "input": { "title": "Desk", "quantity": 0, "status": "open" } }""", "/values/quantity", "note.quantityPositive")]
    public async Task Input_that_the_endpoint_or_the_record_refuses_is_a_400_at_its_path_and_nothing_is_written(string body, string key, string message)
    {
        var before = await CountWritesAsync();

        using var response = await CallAsync(body);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "One or more validation errors occurred.");
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Contains(key, errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal([message], errors.GetProperty(key).EnumerateArray().Select(error => error.GetString()));
        Assert.Equal(before, await CountWritesAsync());
    }

    [Fact]
    public async Task Input_that_fails_the_start_condition_is_a_400_at_input_and_the_record_is_rolled_back()
    {
        var before = await CountWritesAsync();

        using var response = await CallAsync("""{ "input": { "title": "Closed already", "quantity": 1, "status": "closed", "lines": [{ "text": "Legs" }] } }""");

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "One or more validation errors occurred.");
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["/input"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal(["note.cannotClose"], errors.GetProperty("/input").EnumerateArray().Select(error => error.GetString()));
        Assert.Equal(before, await CountWritesAsync());
        Assert.Equal(0L, await fixture.CountAsync(
            $"SELECT count(*) FROM {fixture.NoteTable} WHERE {Column("title")} = 'Closed already'"));
    }

    [Theory]
    [InlineData("/api/apps/EndpointApp/endpoints/ImportMissing")]
    [InlineData("/api/apps/MissingApp/endpoints/ImportNote")]
    public async Task A_valid_caller_gets_a_404_for_an_unknown_endpoint_or_application(string path)
    {
        var before = await CountWritesAsync();

        using var response = await CallAsync("""{ "input": { "title": "Desk", "status": "open" } }""", path: path);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "No endpoint exists with this name.");
        Assert.Equal(before, await CountWritesAsync());
    }

    [Fact]
    public async Task A_request_without_a_caller_is_a_401_before_the_path_is_read()
    {
        using var known = await CallAsync("""{ "input": { "title": "Desk", "status": "open" } }""", caller: null);
        using var unknown = await CallAsync("""{ "input": { "title": "Desk", "status": "open" } }""", caller: null, path: "/api/apps/MissingApp/endpoints/ImportNote");

        await AssertProblemAsync(known, HttpStatusCode.Unauthorized, "A valid bearer token is needed to call this endpoint.");
        await AssertProblemAsync(unknown, HttpStatusCode.Unauthorized, "A valid bearer token is needed to call this endpoint.");
        Assert.Equal("Bearer", known.Headers.WwwAuthenticate.ToString());
    }

    /// <summary>Sends a call without the CSRF header, as <paramref name="caller"/>, or as nobody when it is null.</summary>
    private async Task<HttpResponseMessage> CallAsync(
        string body, string? caller = "importer-system", string path = Call, string contentType = "application/json")
    {
        using var client = fixture.CreateClient();
        if (caller is not null)
        {
            client.CallingAs(caller);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        request.Headers.Host = Host;
        return await client.SendAsync(request, CancellationToken);
    }

    /// <summary>The notes, note lines, instances, work items and audit records stored now.</summary>
    private async Task<(long Notes, long Lines, long Instances, long WorkItems, long Audits)> CountWritesAsync() =>
        (await fixture.CountAsync($"SELECT count(*) FROM {fixture.NoteTable}"),
            await fixture.CountAsync($"SELECT count(*) FROM {fixture.NoteLineTable}"),
            await fixture.CountAsync("SELECT count(*) FROM axis.process_instances"),
            await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items"),
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records"));

    private static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == statusCode, $"{response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(body);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        return problem;
    }
}
