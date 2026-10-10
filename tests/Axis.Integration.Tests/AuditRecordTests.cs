using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Audit records of record API writes. Audit records cannot be removed, so every test reads them
/// by the id of a record it created and never cleans them up.
/// </summary>
public sealed class AuditRecordTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Departments = "/api/apps/RecordsApp/entities/Department/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_update_and_delete_each_add_one_audit_record()
    {
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        var id = await CreateItemAsync(fixture.Client, HostA, """{ "name": "Desk", "quantity": 3, "price": 10 }""");

        var created = Assert.Single(await fixture.AuditRecordsAsync(TenantA, id));

        Assert.Equal(new AuditRecord("anonymous", "record.created", fixture.Model.Manifest.Id, item.Id, id, created.Details), created);
        AssertDetails("""{ "version": 1 }""", created.Details);

        // The body names the fields out of declaration order. The computed total changes too, but
        // only the fields and collections the body sets are listed, in declaration order.
        using var update = Request(
            HttpMethod.Patch,
            $"{Items}/{id:D}",
            HostA,
            $$"""{ "version": 1, "values": { "parts": [{ "name": "Leg {{id:N}}" }], "quantity": 5, "name": "Desk" } }""");
        using var updated = await fixture.Client.SendAsync(update, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var delete = Request(HttpMethod.Delete, $"{Items}/{id:D}", HostA);
        using var deleted = await fixture.Client.SendAsync(delete, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var audits = await fixture.AuditRecordsAsync(TenantA, id);

        Assert.Equal(["record.created", "record.updated", "record.deleted"], audits.Select(audit => audit.Action));
        Assert.All(audits, audit => Assert.Equal(("anonymous", fixture.Model.Manifest.Id, item.Id, id), (audit.Actor, audit.ApplicationId, audit.EntityId, audit.RecordId)));
        AssertDetails("""{ "version": 2, "fields": ["name", "quantity", "parts"] }""", audits[1].Details);
        // A deleted record has no new version: the details hold the version it had.
        AssertDetails("""{ "version": 2 }""", audits[2].Details);
    }

    [Fact]
    public async Task The_actor_is_the_signed_in_test_user_or_anonymous()
    {
        using var client = fixture.CreateClient();
        using var signIn = Request(HttpMethod.Post, "/api/test-users/sign-in", HostA, """{ "id": "anna" }""");
        using var signedIn = await client.SendAsync(signIn, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        var annas = await CreateItemAsync(client, HostA, """{ "name": "Chair" }""");
        var anonymous = await CreateItemAsync(fixture.Client, HostA, """{ "name": "Lamp" }""");

        Assert.Equal("anna", Assert.Single(await fixture.AuditRecordsAsync(TenantA, annas)).Actor);
        Assert.Equal("anonymous", Assert.Single(await fixture.AuditRecordsAsync(TenantA, anonymous)).Actor);
    }

    [Fact]
    public async Task A_write_that_fails_adds_no_audit_record()
    {
        var departmentId = await CreateDepartmentAsync($"Sales {Guid.NewGuid():N}");
        var id = await CreateItemAsync(fixture.Client, HostA, $$"""{ "name": "Desk", "department": "{{departmentId:D}}" }""");

        using var stale = Request(HttpMethod.Patch, $"{Items}/{id:D}", HostA, """{ "version": 2, "values": { "quantity": 5 } }""");
        using var staleResponse = await fixture.Client.SendAsync(stale, CancellationToken);
        using var staleProblem = await ReadProblemAsync(staleResponse, HttpStatusCode.Conflict);

        // Storage rejects the reference inside the write's transaction.
        using var missing = Request(HttpMethod.Patch, $"{Items}/{id:D}", HostA, $$"""{ "version": 1, "values": { "department": "{{Guid.NewGuid():D}}" } }""");
        using var missingResponse = await fixture.Client.SendAsync(missing, CancellationToken);
        using var missingProblem = await ReadProblemAsync(missingResponse, HttpStatusCode.BadRequest);

        using var referenced = Request(HttpMethod.Delete, $"{Departments}/{departmentId:D}", HostA);
        using var referencedResponse = await fixture.Client.SendAsync(referenced, CancellationToken);
        using var referencedProblem = await ReadProblemAsync(referencedResponse, HttpStatusCode.Conflict);

        Assert.Equal("record.created", Assert.Single(await fixture.AuditRecordsAsync(TenantA, id)).Action);
        Assert.Equal("record.created", Assert.Single(await fixture.AuditRecordsAsync(TenantA, departmentId)).Action);
    }

    [Theory]
    [InlineData("UPDATE axis.audit_records SET actor = 'mallory' WHERE record_id = @record")]
    [InlineData("DELETE FROM axis.audit_records WHERE record_id = @record")]
    [InlineData("TRUNCATE axis.audit_records")]
    public async Task The_database_rejects_any_change_to_an_audit_record(string statement)
    {
        var recordId = Guid.NewGuid();
        await fixture.ExecuteAsync(
            TenantA,
            $$"""
            INSERT INTO axis.audit_records (id, occurred_at, actor, action, application_id, entity_id, record_id, process_instance_id, details)
            VALUES ('{{Guid.CreateVersion7():D}}', now(), 'anna', 'record.created', '{{fixture.Model.Manifest.Id:D}}', NULL, '{{recordId:D}}', NULL, '{"version": 1}')
            """);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => fixture.ExecuteAsync(TenantA, statement.Replace("@record", $"'{recordId:D}'", StringComparison.Ordinal)));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        Assert.Equal(
            new AuditRecord("anna", "record.created", fixture.Model.Manifest.Id, null, recordId, """{"version": 1}"""),
            Assert.Single(await fixture.AuditRecordsAsync(TenantA, recordId)));
    }

    [Fact]
    public async Task An_audit_record_is_written_only_in_the_tenant_database_of_the_host()
    {
        var id = await CreateItemAsync(fixture.Client, HostA, """{ "name": "Desk" }""");

        Assert.Equal("record.created", Assert.Single(await fixture.AuditRecordsAsync(TenantA, id)).Action);
        Assert.Empty(await fixture.AuditRecordsAsync(TenantB, id));
    }

    private static void AssertDetails(string expected, string actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"The details are {actual}.");

    private async Task<Guid> CreateDepartmentAsync(string name)
    {
        using var request = Request(HttpMethod.Post, Departments, HostA, $$"""{ "values": { "name": "{{name}}" } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var record = await ReadJsonAsync(response);
        return record.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateItemAsync(HttpClient client, string host, string values)
    {
        using var request = Request(HttpMethod.Post, Items, host, $$"""{ "values": {{values}} }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var record = await ReadJsonAsync(response);
        return record.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
