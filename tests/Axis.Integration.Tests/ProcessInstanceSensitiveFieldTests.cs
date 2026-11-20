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

    [Fact]
    public async Task A_sensitive_field_that_an_update_record_run_changed_is_hidden_and_its_stored_change_holds_only_its_name()
    {
        var release = await fixture.ActivateEditedAsync(TenantA, AddSalary(mapped: false, submits: true));
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "Submit", subject, release);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        using var body = await GetOkAsync(TenantA, instance, "Submit");

        var submit = body.RootElement.GetProperty("path").EnumerateArray().Single(entry => entry.GetProperty("step").GetString() == "submit");
        var changes = submit.GetProperty("made").GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(["salary", "status", "submittedAt"], changes.Select(change => change.GetProperty("field").GetString()));
        Assert.Equal(JsonValueKind.Null, changes[0].GetProperty("before").ValueKind);
        Assert.Equal(JsonValueKind.Null, changes[0].GetProperty("after").ValueKind);
        Assert.True(changes[0].GetProperty("hidden").GetBoolean());
        Assert.Equal("submitted", changes[1].GetProperty("after").GetString());
        Assert.False(changes[1].GetProperty("hidden").GetBoolean());

        // No plaintext copy exists: the stored change of the field holds its name and no value.
        var stored = (string?)await fixture.ScalarAsync(
            TenantA, "SELECT changes::text FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'submit'", ("id", instance));
        using var storedChanges = JsonDocument.Parse(stored!);
        var salary = storedChanges.RootElement.EnumerateArray().Single(change => change.GetProperty("field").GetString() == "salary");
        Assert.Equal(["field"], salary.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task An_email_address_read_from_a_sensitive_field_is_hidden_and_its_stored_entry_holds_no_address()
    {
        var (release, model) = await fixture.ActivateEditedWithModelAsync(TenantA, (path, content) => AddContact(path, AddSalary(mapped: false)(path, content)!), added: new Dictionary<string, string> { ["processes/mail-contact.json"] = MailContact });
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "MailContact", subject, release, model);
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        using var body = await GetOkAsync(TenantA, instance, "MailContact");

        var tell = body.RootElement.GetProperty("path").EnumerateArray().Single(entry => entry.GetProperty("step").GetString() == "tell");
        var email = Assert.Single(tell.GetProperty("made").GetProperty("notified").GetProperty("to").EnumerateArray());
        Assert.Equal("email", email.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, email.GetProperty("name").ValueKind);
        Assert.True(email.GetProperty("hidden").GetBoolean());

        // No plaintext copy exists: the stored entry holds its kind and its position and no value.
        var stored = (string?)await fixture.ScalarAsync(
            TenantA, "SELECT notified::text FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'tell'", ("id", instance));
        Assert.DoesNotContain("nora@example.com", stored, StringComparison.Ordinal);
        using var storedNotified = JsonDocument.Parse(stored!);
        Assert.Equal(["kind", "recipient"], Assert.Single(storedNotified.RootElement.EnumerateArray()).EnumerateObject().Select(property => property.Name));
    }

    // The process sets "contact" and then notifies it by email only. A recipient expression may read a sensitive field, and only a value may not.
    private const string MailContact =
        """
        {
          "id": "9d2c4e6a-7b18-4f35-a0e1-3c5d7f9b1a28",
          "kind": "process",
          "name": "MailContact",
          "formatVersion": 1,
          "entity": "Request",
          "start": "setContact",
          "steps": [
            { "name": "setContact", "type": "operation", "operation": "updateRecord", "set": { "contact": "'nora@example.com'" }, "next": "tell" },
            {
              "name": "tell",
              "type": "operation",
              "operation": "notify",
              "to": [{ "email": "contact" }],
              "channels": ["email"],
              "message": {
                "subject": { "textKey": "request.notice.subject" },
                "body": { "textKey": "request.notice.body" },
                "values": { "title": "title", "amount": "amount" }
              },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    // Lets the auditor read "contact", which AddSalary adds, and inspect MailContact.
    private static string? AddContact(string path, string content) =>
        path == Path.Combine("policies", "auditor-access.json")
            ? content
                .Replace("\"fields\": [\"title\", \"amount\"", "\"fields\": [\"title\", \"amount\", \"contact\"", StringComparison.Ordinal)
                .Replace(
                    "{ \"name\": \"departmentOrderInspect\"",
                    "{ \"name\": \"mailContactInspect\", \"process\": \"MailContact\", \"actions\": [\"inspect\"] },\n    { \"name\": \"departmentOrderInspect\"",
                    StringComparison.Ordinal)
            : content;

    // Edits the Request entity to add the decimal field "salary", sensitive from the release that adds it, and lets the
    // auditor read it. The DepartmentOrder call maps "salary" as its amount when mapped is true. It also adds the sensitive text field
    // "contact", which only the MailContact test uses: a column stays after the release that adds it, so each release of this tenant keeps both fields.
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
                    "\"fields\": [\n    { \"name\": \"salary\", \"type\": \"decimal\", \"precision\": 20, \"scale\": 2, \"sensitive\": true },\n    { \"name\": \"contact\", \"type\": \"text\", \"sensitive\": true },",
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
