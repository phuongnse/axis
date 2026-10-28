using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The process instance detail lists the operator commands the signed-in user may take, and marks a call
/// resolvable, from the user's grants and the states. The sample gives <c>inspect</c> to the <c>operator</c>
/// role, here <c>giang</c>, and <c>cancel</c>, <c>retry</c> and <c>resolve</c> to the finance reviewers.
/// <c>khanh</c> holds both roles. <c>requester</c> starts the approvals and <c>chau</c> is the manager of
/// Operations. Each test leaves no due work item behind, so the next one finds only its own.
/// </summary>
public sealed class ProcessInstanceCommandsTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private static readonly string _departments = EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("ebe6bc20-4772-4d6c-839b-65d426ebeeca")));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_waiting_instance_lists_cancel_only_for_a_user_with_the_cancel_grant()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);

        using (var khanh = await GetAsync("khanh", instance))
        {
            Assert.Equal("waiting", khanh.RootElement.GetProperty("state").GetString());
            Assert.Equal(["cancel"], Commands(khanh));
        }

        using var giang = await GetAsync("giang", instance);
        Assert.Equal("waiting", giang.RootElement.GetProperty("state").GetString());
        Assert.Empty(Commands(giang));
    }

    [Fact]
    public async Task A_failed_instance_lists_retry_only_for_a_user_with_the_retry_grant()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var instance = await FailAsync(requester);

        using (var khanh = await GetAsync("khanh", instance))
        {
            Assert.Equal("failed", khanh.RootElement.GetProperty("state").GetString());
            Assert.Equal(["retry"], Commands(khanh));
        }

        using var giang = await GetAsync("giang", instance);
        Assert.Empty(Commands(giang));
    }

    [Fact]
    public async Task An_unknown_call_is_resolvable_with_its_outputs_only_for_a_user_with_the_resolve_grant()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (instance, call) = await UnknownCallAsync(requester);

        using (var khanh = await GetAsync("khanh", instance))
        {
            var item = Assert.Single(khanh.RootElement.GetProperty("calls").EnumerateArray());
            Assert.Equal(call, item.GetProperty("id").GetGuid());
            Assert.Equal("unknown", item.GetProperty("state").GetString());
            Assert.True(item.GetProperty("resolvable").GetBoolean());
            var output = Assert.Single(item.GetProperty("outputs").EnumerateArray());
            Assert.Equal("orderNumber", output.GetProperty("name").GetString());
            Assert.Equal("text", output.GetProperty("type").GetString());
            Assert.True(output.GetProperty("required").GetBoolean());
            Assert.False(item.TryGetProperty("idempotent", out _));
            Assert.Empty(Commands(khanh));
        }

        using var giang = await GetAsync("giang", instance);
        var unresolvable = Assert.Single(giang.RootElement.GetProperty("calls").EnumerateArray());
        Assert.Equal("unknown", unresolvable.GetProperty("state").GetString());
        Assert.False(unresolvable.GetProperty("resolvable").GetBoolean());
        Assert.False(unresolvable.TryGetProperty("outputs", out _));
        Assert.Empty(Commands(giang));
    }

    private static List<string> Commands(JsonDocument body) =>
        [.. body.RootElement.GetProperty("commands").EnumerateArray().Select(command => command.GetString()!)];

    private async Task<JsonDocument> GetAsync(string user, Guid instance)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{App}/processes/PurchaseRequestApproval/instances/{instance:D}", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    // Starts an approval for a request whose department has no manager, and runs it until managerApproval fails.
    private async Task<Guid> FailAsync(HttpClient requester)
    {
        var department = Guid.NewGuid();
        await fixture.ScalarAsync(
            $"INSERT INTO {_departments} ({EntityNaming.Quote(EntityNaming.IdColumn)}, {EntityNaming.Quote(EntityNaming.Column("code"))}, {EntityNaming.Quote(EntityNaming.Column("name"))}) VALUES (@id, @code, 'No manager')",
            ("id", department),
            ("code", "T" + department.ToString("N")[..10]));
        var subject = await CreateRequestAsync(requester, 500, department);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("failed", await StateAsync(instance));
        return instance;
    }

    // Starts an approval, approves it as the manager and runs it until the purchase order call is staged. The call is
    // then set to unknown with no work item, as a connector with no lookup leaves it.
    private async Task<(Guid Instance, Guid Call)> UnknownCallAsync(HttpClient requester)
    {
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;
        using (var approved = await CompleteAsync(chau, task, """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await fixture.RunStepsUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
        var call = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instance)))!;
        await fixture.ScalarAsync("UPDATE axis.connector_calls SET state = 'unknown', attempts = 1 WHERE id = @id", ("id", call));
        await fixture.ScalarAsync("DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));
        return (instance, call);
    }

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance));
}
