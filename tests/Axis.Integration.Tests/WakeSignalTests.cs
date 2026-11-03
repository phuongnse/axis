using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Npgsql;
using static Axis.Integration.Tests.AdminTenantRestoreTests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// After a request commits a work item due now, the server sends <c>NOTIFY axis_work</c> with the tenant id on the platform database,
/// under the directory source only. A test listens on that channel, as a worker will.
/// </summary>
public sealed class WakeSignalTests(AdminBackupFixture fixture) : IClassFixture<AdminBackupFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private static readonly TimeSpan _received = TimeSpan.FromSeconds(10);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_process_start_through_the_server_on_the_directory_sends_the_wake_signal_for_its_tenant()
    {
        await using var listener = await WorkSignalListener.StartAsync(fixture.Scenario.Platform);
        await using var server = CreateDirectoryServer(fixture.Scenario.Platform);
        await RefreshAsync(server);
        using var requester = server.CreateClient().SignedInAs("requester");
        var subject = await AdminBackupFixture.CreateRequestAsync(requester);
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        listener.Drain();

        await StartAsync(requester, subject);

        Assert.True(await listener.ReceivesAsync(fixture.TenantId, _received));
    }

    [Fact]
    public async Task A_task_completion_through_the_server_on_the_directory_sends_a_fresh_wake_signal_for_its_tenant()
    {
        await using var server = CreateDirectoryServer(fixture.Scenario.Platform);
        await RefreshAsync(server);
        using var requester = server.CreateClient().SignedInAs("requester");
        var subject = await AdminBackupFixture.CreateRequestAsync(requester);
        var instance = await StartAsync(requester, subject);

        // The server has applied the platform grants that the worker needs.
        var worker = await RestoredTenantWorkTests.BuildWorkerAsync(fixture.Scenario.Platform);
        try
        {
            await Polling.WaitUntilAsync(async () => await StateAsync(instance) == "waiting:managerApproval", "The instance did not wait for its manager.");
        }
        finally
        {
            await worker.StopAsync(CancellationToken);
            await worker.DisposeAsync();
        }

        var task = (Guid)(await PostgreSqlServer.AdminScalarAsync(
            await TenantDatabaseAsync(),
            $"SELECT id FROM axis.process_tasks WHERE process_instance_id = '{instance:D}' AND state = 'open'",
            CancellationToken))!;
        await using var listener = await WorkSignalListener.StartAsync(fixture.Scenario.Platform);
        using var manager = server.CreateClient().SignedInAs("manager-2");

        using var complete = await manager.SendAsync(
            Request(HttpMethod.Post, $"{App}/tasks/{task:D}/complete", AdminBackupFixture.Host, """{ "outcome": "approve" }"""),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.True(await listener.ReceivesAsync(fixture.TenantId, _received));
    }

    [Fact]
    public async Task A_process_start_through_the_server_on_the_configuration_source_sends_no_wake_signal()
    {
        await using var listener = await WorkSignalListener.StartAsync(fixture.Scenario.Platform);
        using var requester = fixture.CreateClient();
        var subject = await AdminBackupFixture.CreateRequestAsync(requester);
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        listener.Drain();

        await StartAsync(requester, subject);

        Assert.False(await listener.ReceivesAsync(fixture.TenantId, TimeSpan.FromSeconds(2)));
    }

    private static async Task<Guid> StartAsync(HttpClient client, Guid subject)
    {
        using var request = Request(HttpMethod.Post, $"{App}/processes/PurchaseRequestApproval/instances", AdminBackupFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(
            await TenantDatabaseAsync(),
            $"SELECT state || ':' || step FROM axis.process_instances WHERE id = '{instance:D}'",
            CancellationToken);

    private async Task<string> TenantDatabaseAsync() =>
        new NpgsqlConnectionStringBuilder(fixture.Scenario.Platform) { Database = (await fixture.Scenario.RowAsync(fixture.TenantId)).DatabaseName }.ConnectionString;
}
