using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The field defaults of the RecordsApp fixture: a create fills each field its body leaves out from
/// the field's default, a <c>null</c> stays <c>null</c>, <c>now()</c> is the write transaction's
/// start time, and an update never applies a default.
/// </summary>
public sealed class RecordDefaultEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Reminders = "/api/apps/RecordsApp/entities/Reminder/records";
    private const string Gauges = "/api/apps/RecordsApp/entities/Gauge/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_fills_left_out_fields_keeps_null_and_stamps_now_with_the_transaction_time_and_update_applies_none()
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var created = await SendAsync(anna, HttpMethod.Post, Reminders, """{ "values": { "title": "Call", "priority": null } }""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = await ReadJsonAsync(created);
        var id = createdJson.RootElement.GetProperty("id").GetString()!;

        using var read = await GetAsync(id);
        var values = read.RootElement.GetProperty("values");
        Assert.Equal("open", values.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, values.GetProperty("priority").ValueKind);
        var remindAt = values.GetProperty("remindAt").GetDateTimeOffset();
        var audited = (DateTime)(await fixture.ScalarAsync(
            TenantA,
            $"SELECT occurred_at FROM axis.audit_records WHERE record_id = '{id}' AND action = 'record.created'"))!;
        Assert.Equal(audited, remindAt.UtcDateTime);

        using var updated = await SendAsync(anna, HttpMethod.Patch, $"{Reminders}/{id}", """{ "version": 1, "values": { "title": "Call back" } }""");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var afterUpdate = await GetAsync(id);
        var after = afterUpdate.RootElement.GetProperty("values");
        Assert.Equal(2, afterUpdate.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("Call back", after.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("priority").ValueKind);
        Assert.Equal("open", after.GetProperty("state").GetString());
        Assert.Equal(remindAt, after.GetProperty("remindAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Create_fills_a_default_that_the_body_leaves_out()
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var created = await SendAsync(anna, HttpMethod.Post, Reminders, """{ "values": { "title": "Plain" } }""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = await ReadJsonAsync(created);

        var values = json.RootElement.GetProperty("values");
        Assert.Equal("open", values.GetProperty("state").GetString());
        Assert.Equal(3, values.GetProperty("priority").GetInt64());
    }

    [Fact]
    public async Task A_required_field_with_a_default_sent_as_null_is_400_required_and_writes_nothing()
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var response = await SendAsync(anna, HttpMethod.Post, Reminders, """{ "values": { "title": "Call", "state": null } }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([("/values/state", "Required.")], Errors(problem));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Reminder")).RowCount);
    }

    [Fact]
    public async Task A_default_that_fails_at_run_time_is_400_at_its_field_and_writes_nothing()
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var audits = await fixture.AuditRecordCountAsync(TenantA);

        using var response = await SendAsync(anna, HttpMethod.Post, Gauges, """{ "values": {} }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([("/values/ratio", "Could not be computed.")], Errors(problem));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Gauge")).RowCount);
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));

        // A body that sets the field never runs its default.
        using var set = await SendAsync(anna, HttpMethod.Post, Gauges, """{ "values": { "ratio": 2 } }""");
        Assert.Equal(HttpStatusCode.Created, set.StatusCode);
    }

    [Fact]
    public async Task A_create_without_a_create_grant_is_403_and_writes_nothing()
    {
        await fixture.ResetAsync();
        using var rita = fixture.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(rita, HttpMethod.Post, Reminders, """{ "values": { "title": "Call" } }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Reminder")).RowCount);
    }

    private async Task<JsonDocument> GetAsync(string id)
    {
        using var request = Request($"{Reminders}/{id}", HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string body)
    {
        using var request = Request(method, path, HostA, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();
}
