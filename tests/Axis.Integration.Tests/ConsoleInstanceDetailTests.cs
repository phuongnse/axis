using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The operator console's instance detail. It has its own fixture, because the release that adds a
/// sensitive field leaves its <c>bytea</c> column in the tenant, and a later release without the field would be refused.
/// </summary>
public sealed class ConsoleInstanceDetailTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Path = "/api/console/instances/";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_operator_with_no_application_role_reads_the_detail_of_a_failed_instance()
    {
        var instance = await StartFailedAsync(TenantA);

        var (status, body) = await GetAsync("pia", TenantA, instance.ToString("D"));

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = body;
        var root = body.RootElement;
        Assert.Equal(
            ["id", "process", "subjectId", "releaseId", "state", "step", "createdAt", "endedAt", "timerDueAt", "parent", "subProcesses", "graph", "path", "tasks", "calls", "commands"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(instance, root.GetProperty("id").GetGuid());
        Assert.Equal("DepartmentOrder", root.GetProperty("process").GetString());
        Assert.Equal("failed", root.GetProperty("state").GetString());
        Assert.NotEmpty(root.GetProperty("graph").GetProperty("steps").EnumerateArray());
        Assert.Empty(root.GetProperty("commands").EnumerateArray());

        // The operator holds no grant of the application: its own inspection route refuses the same user.
        using var client = fixture.CreateClient().SignedInAs("pia");
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/DepartmentOrder/instances/{instance:D}", Host(TenantA));
        using var inspection = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, inspection.StatusCode);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("nope")]
    [InlineData("{00000000-0000-0000-0000-000000000001}")]
    public async Task An_unknown_or_malformed_instance_id_is_a_404(string id)
    {
        var (status, body) = await GetAsync("pia", TenantA, id);

        using var _ = body;
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("No instance exists with this id.", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_user_without_the_operator_role_gets_a_403_and_a_signed_out_client_a_401()
    {
        var instance = await StartFailedAsync(TenantA);

        var (status, body) = await GetAsync("otto", TenantA, instance.ToString("D"));
        body.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, status);

        using var client = fixture.CreateClient();
        using var request = RecordApiFixture.Request(Path + instance.ToString("D"), Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_call_that_maps_only_plain_fields_shows_its_input()
    {
        var instance = await StartFailedAsync(TenantA);
        await fixture.InsertConnectorCallAsync(TenantA, instance, "createOrder", """{"reference":"Sales","amount":500}""");

        var (status, body) = await GetAsync("pia", TenantA, instance.ToString("D"));

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = body;
        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.False(call.GetProperty("inputHidden").GetBoolean());
        Assert.Equal("Sales", call.GetProperty("input").GetProperty("reference").GetString());
        Assert.False(call.GetProperty("resolvable").GetBoolean());
    }

    [Fact]
    public async Task A_call_that_maps_a_sensitive_field_has_its_input_hidden()
    {
        var release = await fixture.ActivateEditedAsync(TenantB, AddSalary);
        var subject = await fixture.InsertRequestAsync(TenantB, null);
        var instance = await fixture.StartAsync(TenantB, "DepartmentOrder", subject, release);
        await fixture.ExecuteAsync(TenantB, "UPDATE axis.process_instances SET state = 'failed', ended_at = now() WHERE id = @id", ("id", instance));
        await fixture.InsertConnectorCallAsync(TenantB, instance, "createOrder", """{"reference":"Sales","amount":500}""");

        var (status, body) = await GetAsync("pia", TenantB, instance.ToString("D"));

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = body;
        var call = Assert.Single(body.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("input").ValueKind);
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
    }

    // Edits the Request entity to add the decimal field "salary", sensitive from the release that adds it. The
    // DepartmentOrder call maps "salary" as its amount.
    private static string? AddSalary(string path, string content)
    {
        if (path == System.IO.Path.Combine("entities", "request.json"))
        {
            return content.Replace(
                "\"fields\": [",
                "\"fields\": [\n    { \"name\": \"salary\", \"type\": \"decimal\", \"precision\": 20, \"scale\": 2, \"sensitive\": true },",
                StringComparison.Ordinal);
        }

        return path == System.IO.Path.Combine("processes", "department-order.json")
            ? content.Replace("\"amount\": \"amount\"", "\"amount\": \"salary\"", StringComparison.Ordinal)
            : content;
    }

    private async Task<Guid> StartFailedAsync(string tenant)
    {
        var subject = await fixture.InsertRequestAsync(tenant, null);
        var instance = await fixture.StartAsync(tenant, "DepartmentOrder", subject);
        await fixture.ExecuteAsync(tenant, "UPDATE axis.process_instances SET state = 'failed', ended_at = now() WHERE id = @id", ("id", instance));
        return instance;
    }

    private async Task<(HttpStatusCode Status, JsonDocument Body)> GetAsync(string user, string tenant, string id)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request(Path + id, Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)));
    }
}
