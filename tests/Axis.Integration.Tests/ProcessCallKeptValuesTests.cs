using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A connector call keeps its request and each attempt's response, and a package call the values it returned. The
/// instance detail gives them to a reader who may read every mapped field: <c>ada</c> does and <c>ines</c> may not
/// read <c>department.name</c>. A call that writes its outputs gives what it changed in <c>made.changes</c>, which
/// <c>carl</c> may not read for <c>comment</c>.
/// </summary>
public sealed class ProcessCallKeptValuesTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reader_sees_the_request_and_the_response_with_the_secrets_hidden()
    {
        var marker = NewMarker();
        fixture.Stub.AnswerHeaders["Set-Cookie"] = $"session={marker}";
        try
        {
            fixture.Stub.Respond(200, $$"""{"orderNumber":"PO-1","note":"{{marker}}"}""");
            var (instance, call, _) = await SendAsync();

            using var detail = await GetDetailAsync("ada", instance);

            var item = Assert.Single(detail.RootElement.GetProperty("calls").EnumerateArray());
            var request = item.GetProperty("request");
            Assert.Equal("POST", request.GetProperty("method").GetString());
            Assert.Equal($"{fixture.Stub.BaseUrl}/tenant-a/orders", request.GetProperty("url").GetString());
            Assert.Empty(request.GetProperty("query").EnumerateObject());
            Assert.Equal("[secret]", request.GetProperty("headers").GetProperty("Authorization").GetString());
            Assert.Equal(call.ToString("D"), request.GetProperty("headers").GetProperty("Idempotency-Key").GetString());
            Assert.Contains("Department ", request.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.False(request.GetProperty("truncated").GetBoolean());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("returned").ValueKind);
            Assert.DoesNotContain(fixture.Token("a"), detail.RootElement.ToString(), StringComparison.Ordinal);

            var attempt = Assert.Single(item.GetProperty("attempts").EnumerateArray());
            var response = attempt.GetProperty("response");
            Assert.Equal(["body", "headers", "status", "truncated"], response.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(200, response.GetProperty("status").GetInt32());
            Assert.Contains(marker, response.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal("[secret]", response.GetProperty("headers").GetProperty("Set-Cookie").GetString());
            Assert.False(response.GetProperty("truncated").GetBoolean());
            Assert.DoesNotContain($"session={marker}", detail.RootElement.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            fixture.Stub.AnswerHeaders.Remove("Set-Cookie");
        }
    }

    [Fact]
    public async Task An_error_answer_keeps_its_status_and_body()
    {
        var marker = NewMarker();
        fixture.Stub.Respond(400, $$"""{"error":"{{marker}}"}""");
        var (instance, _, _) = await SendAsync();

        using var detail = await GetDetailAsync("ada", instance);

        var item = Assert.Single(detail.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Object, item.GetProperty("request").ValueKind);
        var attempt = Assert.Single(item.GetProperty("attempts").EnumerateArray());
        Assert.Equal("failed", attempt.GetProperty("outcome").GetString());
        var response = attempt.GetProperty("response");
        Assert.Equal(400, response.GetProperty("status").GetInt32());
        Assert.Equal($$"""{"error":"{{marker}}"}""", response.GetProperty("body").GetString());
        Assert.False(response.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task A_reader_who_may_not_read_a_mapped_field_gets_every_kept_value_as_null()
    {
        var marker = NewMarker();
        fixture.Stub.Respond(200, $$"""{"orderNumber":"PO-1","note":"{{marker}}"}""");
        var (instance, _, subject) = await SendAsync();

        // Ines may read only a request with no department, so the instance is hers once the request has none.
        // The call's mapping still reads department.name, which she may not read.
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        await fixture.ExecuteAsync(
            TenantA,
            $"UPDATE {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} SET {EntityNaming.Quote(EntityNaming.Column("department"))} = NULL WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
            ("id", subject));
        await fixture.InsertPackageCallAsync(TenantA, instance, "createOrder", $$"""{"total":"{{marker}}"}""");

        using var allowed = await GetDetailAsync("ada", instance);
        using var denied = await GetDetailAsync("ines", instance);

        var calls = allowed.RootElement.GetProperty("calls").EnumerateArray().ToList();
        Assert.Equal(2, calls.Count);
        var returned = Assert.Single(calls, call => call.GetProperty("kind").GetString() == "package").GetProperty("returned");
        Assert.Equal(marker, returned.GetProperty("total").GetString());
        Assert.Contains(marker, Assert.Single(calls, call => call.GetProperty("kind").GetString() == "connector")
            .GetProperty("attempts")[0].GetProperty("response").GetProperty("body").GetString(), StringComparison.Ordinal);

        var hidden = denied.RootElement.GetProperty("calls").EnumerateArray().ToList();
        Assert.Equal(2, hidden.Count);
        foreach (var call in hidden)
        {
            Assert.Equal(JsonValueKind.Null, call.GetProperty("request").ValueKind);
            Assert.Equal(JsonValueKind.Null, call.GetProperty("returned").ValueKind);
            foreach (var attempt in call.GetProperty("attempts").EnumerateArray())
            {
                Assert.Equal(JsonValueKind.Null, attempt.GetProperty("response").ValueKind);
            }
        }

        Assert.NotEmpty(hidden.SelectMany(call => call.GetProperty("attempts").EnumerateArray()));
        Assert.DoesNotContain(marker, denied.RootElement.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_response_body_over_64_KiB_is_kept_cut_and_marked_truncated()
    {
        var text = $$"""{"orderNumber":"PO-1","note":"{{new string('x', 70 * 1024)}}"}""";
        fixture.Stub.Respond(200, text);
        var (instance, _, _) = await SendAsync();

        using var detail = await GetDetailAsync("ada", instance);

        var item = Assert.Single(detail.RootElement.GetProperty("calls").EnumerateArray());
        var response = Assert.Single(item.GetProperty("attempts").EnumerateArray()).GetProperty("response");
        var kept = response.GetProperty("body").GetString()!;
        Assert.True(Encoding.UTF8.GetByteCount(kept) <= 64 * 1024);
        Assert.True(kept.Length > 60 * 1024);
        Assert.StartsWith(kept, text, StringComparison.Ordinal);
        Assert.True(response.GetProperty("truncated").GetBoolean());
        Assert.Equal(200, response.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task No_history_audit_record_or_log_entry_holds_the_kept_body()
    {
        var marker = NewMarker();
        fixture.Stub.Respond(200, $$"""{"orderNumber":"PO-1","note":"{{marker}}"}""");
        var (instance, _, _) = await SendAsync();
        var pattern = $"%{marker}%";

        Assert.Equal(
            0L,
            await fixture.ScalarAsync(
                TenantA,
                """
                SELECT count(*) FROM axis.process_step_history
                WHERE process_instance_id = @instance AND (input::text LIKE @pattern OR output::text LIKE @pattern OR error LIKE @pattern)
                """,
                ("instance", instance),
                ("pattern", pattern)));
        Assert.Equal(0L, await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.audit_records WHERE details::text LIKE @pattern", ("pattern", pattern)));
        Assert.NotEmpty(fixture.CallLogs.Entries);
        Assert.DoesNotContain(fixture.CallLogs.Entries, entry => entry.Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.CallLogs.Entries, entry => entry.Contains(fixture.Token(TenantA), StringComparison.Ordinal));

        // The marker is kept in the one place that holds the response.
        Assert.Equal(1L, await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.process_call_attempts WHERE response::text LIKE @pattern", ("pattern", pattern)));
    }

    [Fact]
    public async Task A_call_that_writes_its_outputs_gives_the_field_it_changed_with_the_value_before_and_after()
    {
        var marker = NewMarker();
        fixture.Stub.Respond(200, $$"""{"orderNumber":"{{marker}}"}""");
        var (instance, _, _) = await SendAsync();

        using var detail = await GetDetailAsync("ada", instance);

        var change = Assert.Single(CreateOrderChanges(detail));
        Assert.Equal(["field", "before", "after", "hidden"], change.EnumerateObject().Select(property => property.Name));
        Assert.Equal("comment", change.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, change.GetProperty("before").ValueKind);
        Assert.Equal(marker, change.GetProperty("after").GetString());
        Assert.False(change.GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public async Task A_reader_who_may_not_read_the_changed_field_gets_its_values_null_and_hidden()
    {
        var marker = NewMarker();
        fixture.Stub.Respond(200, $$"""{"orderNumber":"{{marker}}"}""");
        var (instance, _, _) = await SendAsync();

        using var detail = await GetDetailAsync("carl", instance);

        var change = Assert.Single(CreateOrderChanges(detail));
        Assert.Equal("comment", change.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, change.GetProperty("before").ValueKind);
        Assert.Equal(JsonValueKind.Null, change.GetProperty("after").ValueKind);
        Assert.True(change.GetProperty("hidden").GetBoolean());
        Assert.DoesNotContain(marker, detail.RootElement.ToString(), StringComparison.Ordinal);
    }

    // The changes of the createOrder run that succeeded.
    private static List<JsonElement> CreateOrderChanges(JsonDocument detail)
    {
        var run = Assert.Single(
            detail.RootElement.GetProperty("path").EnumerateArray(),
            entry => entry.GetProperty("step").GetString() == "createOrder"
                && entry.GetProperty("output").ValueKind == JsonValueKind.Object
                && entry.GetProperty("output").TryGetProperty("outcome", out var outcome)
                && outcome.GetString() == "succeeded");
        return [.. run.GetProperty("made").GetProperty("changes").EnumerateArray()];
    }

    // Stages the call of a new DepartmentOrder instance and sends it to the stub. Returns the instance, the call and the subject.
    private async Task<(Guid Instance, Guid Call, Guid Subject)> SendAsync()
    {
        var department = await fixture.InsertDepartmentAsync(TenantA, null);
        var subject = await fixture.InsertRequestAsync(TenantA, department, amount: 500m);
        var instance = await fixture.StartAsync(TenantA, "DepartmentOrder", subject);
        Assert.True(await fixture.RunNextStepAsync(TenantA, CancellationToken));
        var call = (Guid)(await fixture.ScalarAsync(TenantA, "SELECT id FROM axis.connector_calls WHERE process_instance_id = @instance", ("instance", instance)))!;
        Assert.NotNull(await fixture.RunCallAsync(TenantA, CancellationToken));
        return (instance, call, subject);
    }

    private async Task<JsonDocument> GetDetailAsync(string user, Guid instance)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/DepartmentOrder/instances/{instance:D}", Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static string NewMarker() => $"marker-{Guid.NewGuid():N}";
}
