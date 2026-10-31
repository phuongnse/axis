using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A sensitive field in process inspection. It has its own fixture, because the release that adds the
/// field leaves its <c>bytea</c> column in the tenant, and a later release without the field would be refused.
/// </summary>
public sealed class ProcessInstanceSensitiveFieldTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sensitive_field_is_in_the_record_but_the_input_that_maps_it_is_hidden_from_a_user_who_may_read_it()
    {
        var release = await fixture.ActivateEditedAsync(TenantA, AddSalary(mapped: true));
        Assert.True(fixture.Model.TryGetEntity("Request", out var entity));
        var table = await fixture.ReadTableAsync(TenantA, entity.Id);
        Assert.Equal("bytea", Assert.Single(table.Columns, column => column.Name == "f_salary").Type);
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "DepartmentOrder", subject, release);
        await fixture.InsertConnectorCallAsync(TenantA, instance, "createOrder", """{"reference":"Sales","amount":500}""");

        using var client = fixture.CreateClient().SignedInAs("ada");
        using var recordRequest = RecordApiFixture.Request($"/api/apps/StepApp/entities/Request/records/{subject:D}", Host(TenantA));
        using var recordResponse = await client.SendAsync(recordRequest, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
        using var record = JsonDocument.Parse(await recordResponse.Content.ReadAsStringAsync(CancellationToken));
        Assert.True(record.RootElement.GetProperty("values").TryGetProperty("salary", out _));

        using var body = await GetOkAsync(TenantA, instance);
        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("input").ValueKind);
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
    }

    [Fact]
    public async Task An_instance_pinned_to_a_release_that_maps_a_sensitive_field_keeps_its_input_hidden_after_a_newer_release_maps_a_plain_field()
    {
        var release = await fixture.ActivateEditedAsync(TenantB, AddSalary(mapped: true));
        var subject = await fixture.InsertRequestAsync(TenantB, null);
        var instance = await fixture.StartAsync(TenantB, "DepartmentOrder", subject, release);
        await fixture.InsertConnectorCallAsync(TenantB, instance, "createOrder", """{"reference":"Sales","amount":500}""");
        await fixture.ActivateEditedAsync(TenantB, AddSalary(mapped: false));

        using var body = await GetOkAsync(TenantB, instance);

        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("input").ValueKind);
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
    }

    // Edits the Request entity to add the decimal field "salary", sensitive from the release that adds it, and lets the
    // auditor read it. The DepartmentOrder call maps "salary" as its amount when mapped is true.
    private static Func<string, string, string?> AddSalary(bool mapped) =>
        (path, content) =>
        {
            if (path == Path.Combine("entities", "request.json"))
            {
                return content.Replace(
                    "\"fields\": [",
                    "\"fields\": [\n    { \"name\": \"salary\", \"type\": \"decimal\", \"precision\": 20, \"scale\": 2, \"sensitive\": true },",
                    StringComparison.Ordinal);
            }

            if (path == Path.Combine("processes", "department-order.json") && mapped)
            {
                return content.Replace("\"amount\": \"amount\"", "\"amount\": \"salary\"", StringComparison.Ordinal);
            }

            if (path == Path.Combine("policies", "auditor-access.json"))
            {
                return content.Replace("\"fields\": [\"title\", \"amount\"", "\"fields\": [\"title\", \"amount\", \"salary\"", StringComparison.Ordinal);
            }

            return content;
        };

    private async Task<JsonDocument> GetOkAsync(string tenant, Guid instance)
    {
        using var client = fixture.CreateClient().SignedInAs("ada");
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/DepartmentOrder/instances/{instance:D}", Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
