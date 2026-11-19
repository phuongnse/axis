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

    [Fact]
    public async Task A_sensitive_field_that_a_completed_instance_wrote_is_hidden_and_never_stored_even_for_a_user_whose_grants_cover_it()
    {
        var release = await fixture.ActivateEditedAsync(TenantA, AddSalary(mapped: false, submits: true));
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "Submit", subject, release);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        using var body = await GetOkAsync(TenantA, instance, "Submit");

        var values = body.RootElement.GetProperty("result").GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(["salary", "status", "submittedAt"], values.Select(value => value.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, values[0].GetProperty("value").ValueKind);
        Assert.True(values[0].GetProperty("hidden").GetBoolean());
        Assert.DoesNotContain("100", values[0].ToString(), StringComparison.Ordinal);
        Assert.Equal("submitted", values[1].GetProperty("value").GetString());
        Assert.False(values[1].GetProperty("hidden").GetBoolean());

        // No plaintext copy exists: the stored result holds null for the field.
        var stored = (string?)await fixture.ScalarAsync(TenantA, "SELECT result::text FROM axis.process_instances WHERE id = @id", ("id", instance));
        using var storedResult = JsonDocument.Parse(stored!);
        Assert.Equal(JsonValueKind.Null, storedResult.RootElement.GetProperty("salary").ValueKind);
    }

    // Edits the Request entity to add the decimal field "salary", sensitive from the release that adds it, and lets the
    // auditor read it. The DepartmentOrder call maps "salary" as its amount when mapped is true.
    private static Func<string, string, string?> AddSalary(bool mapped, bool submits = false) =>
        (path, content) =>
        {
            // The Submit process also writes salary, and the auditor may read it, and its status, and inspect Submit.
            if (path == Path.Combine("processes", "submit.json") && submits)
            {
                return content.Replace("\"status\": \"'submitted'\"", "\"status\": \"'submitted'\", \"salary\": \"100\"", StringComparison.Ordinal);
            }

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
                return content
                    .Replace("\"fields\": [\"title\", \"amount\"", "\"fields\": [\"title\", \"amount\", \"salary\", \"status\"", StringComparison.Ordinal)
                    .Replace(
                        "{ \"name\": \"departmentOrderInspect\"",
                        "{ \"name\": \"submitInspect\", \"process\": \"Submit\", \"actions\": [\"inspect\"] },\n    { \"name\": \"departmentOrderInspect\"",
                        StringComparison.Ordinal);
            }

            return content;
        };

    private async Task<JsonDocument> GetOkAsync(string tenant, Guid instance, string process = "DepartmentOrder")
    {
        using var client = fixture.CreateClient().SignedInAs("ada");
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/{process}/instances/{instance:D}", Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
